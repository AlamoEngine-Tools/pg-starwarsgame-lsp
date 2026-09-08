// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

const assert = require('node:assert/strict');
const { describe, it } = require('node:test');

const { stringOffences } = require('./asciiStringRules');

/** The characters reported for a file, in order. */
function chars(text, lang) {
    return stringOffences(text, { lang }).map(o => o.char);
}

describe('non-ASCII in a string literal', () => {
    it('reports an arrow in a C# log message', () => {
        assert.deepEqual(chars('_log.LogDebug("Rename {Id} \u2192 {New}");', 'cs'), ['\u2192']);
    });

    it('reports an en dash in a range, which is the one that reads as correct', () => {
        assert.deepEqual(chars('const string F = "integer 0\u2013255";', 'cs'), ['\u2013']);
    });

    it('reports an emoji as ONE offence, not as its two code units', () => {
        assert.deepEqual(chars('var s = "\u{1F4E6} Packed";', 'cs'), ['\u{1F4E6}']);
    });

    it('reports every offence in one literal', () => {
        assert.deepEqual(
            chars('var s = "\u2192 a \u2026 b \u2192";', 'cs'), ['\u2192', '\u2026', '\u2192']);
    });

    it('says what to write instead, where there is an obvious answer', () => {
        const [arrow] = stringOffences('var s = "a \u2192 b";', { lang: 'cs' });

        assert.equal(arrow.suggestion, "'->'");
    });

    it('names the line the character is on', () => {
        const [hit] = stringOffences('var a = 1;\nvar b = 2;\nvar s = "\u2192";', { lang: 'cs' });

        assert.equal(hit.line, 3);
    });
});

describe('what it deliberately does not report', () => {
    /**
     * The repo's section dividers are box-drawing runs - 62,000 characters of them across both
     * trees - and not one reaches a reader. Comments are not scanned at all, and the divider is
     * additionally allowed inside a literal, since a stylesheet occasionally draws one.
     */
    it('ignores a comment, whatever it carries', () => {
        assert.deepEqual(chars('// \u2500\u2500 Right dock \u2500\u2500 the \u2717 button\n', 'ts'), []);
        assert.deepEqual(chars('/* an arrow \u2192 in prose */\n', 'cs'), []);
    });

    it('ignores a quote inside a comment, which used to end the scan early', () => {
        assert.deepEqual(chars('// it\'s fine\nvar s = "\u2192";', 'cs'), ['\u2192']);
    });

    it('ignores a CSS comment inside a styled template', () => {
        // `storyGraph.tsx` names the glyph its rename button draws, inside the stylesheet.
        assert.deepEqual(chars('const S = styled.div`\n  /* the \u270E button */\n  .a { top: 0; }\n`;', 'ts'), []);
    });

    it('reads a C# verbatim string, where a backslash is not an escape', () => {
        assert.deepEqual(chars('var p = @"C:\\dir\\";\nvar s = "\u2192";', 'cs'), ['\u2192']);
    });

    it('reads a doubled quote in a verbatim string as one quote', () => {
        assert.deepEqual(chars('var p = @"say ""hi""";\nvar s = "\u2192";', 'cs'), ['\u2192']);
    });

    it('reads a raw string literal', () => {
        assert.deepEqual(chars('var p = """\na "quoted" line\n""";\nvar s = "\u2192";', 'cs'), ['\u2192']);
    });

    it('accepts a file that is entirely ASCII', () => {
        assert.deepEqual(chars('var s = "Rename {Id} -> {New}";', 'cs'), []);
    });
});
