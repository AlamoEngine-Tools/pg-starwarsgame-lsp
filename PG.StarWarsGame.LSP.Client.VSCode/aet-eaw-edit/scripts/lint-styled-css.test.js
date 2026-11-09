// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The styled-CSS lint over the file it once missed (#195): a backtick planted in a CSS comment of the
// shared stylesheet in dockChrome.ts is named at its line, by the rule and by the lint run itself.

const assert = require('node:assert/strict');
const {describe, it} = require('node:test');
const fs = require('fs');
const os = require('os');
const path = require('path');
const {spawnSync} = require('child_process');

const {offences} = require('./lint-styled-css');

const SRC = path.join(__dirname, '..', 'src');
const DOCK_CHROME = path.join(SRC, 'webview', 'shared', 'dockChrome.ts');
const TOKENS = path.join(SRC, 'webview', 'shared', 'tokens.ts');

/**
 * dockChrome.ts with a backtick planted in the first CSS comment of its shared stylesheet, and the
 * line that comment is on.
 */
function planted() {
    const text = fs.readFileSync(DOCK_CHROME, 'utf8');
    const opener = text.indexOf('dockChromeCss = `');
    const comment = text.indexOf('/*', opener);
    assert.ok(opener >= 0 && comment > opener, 'the shared stylesheet opens with a comment to plant into');
    const line = text.slice(0, comment).split('\n').length;
    return {text: text.slice(0, comment + 2) + ' the `.dock` class' + text.slice(comment + 2), line};
}

describe('a backtick in a CSS comment', () => {
    it('is reported in the shared stylesheet, at the line of the comment', () => {
        const {text, line} = planted();
        assert.deepEqual(offences(text).map(o => o.line), [line]);
    });

    it('is reported in a styled component', () => {
        const text = 'const Shell = styled.div`\n  /* the `.x` class */\n  color: red;\n`;\n';
        assert.deepEqual(offences(text).map(o => o.line), [2]);
    });

    it('is not confused by a backtick inside an interpolation', () => {
        const text = 'const Shell = styled.div`\n  color: ${`red`};\n  /* fine */\n`;\n';
        assert.deepEqual(offences(text), []);
    });

    it('is absent from the shipped shared stylesheet', () => {
        assert.deepEqual(offences(fs.readFileSync(DOCK_CHROME, 'utf8')), []);
    });
});

describe('the lint run', () => {
    it('fails on a planted backtick in dockChrome.ts and names the line', () => {
        const root = fs.mkdtempSync(path.join(os.tmpdir(), 'styled-css-'));
        try {
            const shared = path.join(root, 'webview', 'shared');
            fs.mkdirSync(shared, {recursive: true});
            fs.copyFileSync(TOKENS, path.join(shared, 'tokens.ts'));
            const {text, line} = planted();
            fs.writeFileSync(path.join(shared, 'dockChrome.ts'), text);

            const run = spawnSync(process.execPath, [path.join(__dirname, 'lint-styled-css.js'), root],
                {encoding: 'utf8'});

            assert.equal(run.status, 1, run.stderr);
            assert.match(run.stderr, new RegExp('dockChrome\\.ts:' + line + '  a backtick in this CSS comment'));
        } finally {
            fs.rmSync(root, {recursive: true, force: true});
        }
    });
});
