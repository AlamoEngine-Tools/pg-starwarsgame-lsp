// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

const assert = require('node:assert/strict');
const {describe, it} = require('node:test');

const {codiconOffences, hostOffences, tokenOffences, KINDS} = require('./styleTokenRules');

/** Offence kinds found in a template body, in order. */
function kinds(body, options) {
    return tokenOffences(body, options).map(o => o.kind);
}

const defined = new Set(['--space-4', '--space-6', '--radius-3', '--colour-ink']);

describe('raw lengths on a scale property', () => {
    it('reports a padding written as a literal', () => {
        assert.deepEqual(kinds('.a { padding: 7px; }', {defined}), [KINDS.length]);
    });

    it('reports each literal in a shorthand, since each is its own decision', () => {
        assert.deepEqual(
            kinds('.a { padding: 4px 8px; }', {defined}),
            [KINDS.length, KINDS.length]);
    });

    it('accepts the same property written as a token', () => {
        assert.deepEqual(kinds('.a { padding: var(--space-4); }', {defined}), []);
    });

    /**
     * A dimension is a fitted measurement, not a step: 128px is 128 because 136 overflowed once the
     * dock grew a scrollbar. Flagging these would either force a meaningless token per value or
     * train everyone to ignore the rule.
     */
    it('leaves fitted dimensions alone', () => {
        // No hex in the shadow: that would be a colour offence, correctly, and this case is about
        // lengths. The shipped shadows use rgba() with an alpha, which is not a themed colour.
        const body = '.a { width: 128px; height: 78px; top: 3px;'
            + ' box-shadow: 0 6px 24px rgba(0, 0, 0, 0.4); }';
        assert.deepEqual(kinds(body, {defined}), []);
    });

    it('leaves a negative length alone, since those are usually coupled to something else', () => {
        assert.deepEqual(kinds('.a { margin: -14px -16px 0; }', {defined}), []);
    });

    it('does not read a px out of a comment', () => {
        assert.deepEqual(kinds('/* 136px overflowed here */ .a { gap: var(--space-6); }', {defined}), []);
    });
});

describe('raw colours', () => {
    it('reports a hex literal', () => {
        assert.deepEqual(kinds('.a { color: #ff0000; }', {defined}), [KINDS.colour]);
    });

    /**
     * The fallback in var(--vscode-x, #hex) is the shipped value for a host variable, not a colour
     * decision - it is what the panel draws when the host defines nothing.
     */
    it('accepts a hex as the fallback of a host variable', () => {
        assert.deepEqual(
            kinds('.a { color: var(--vscode-foreground, #cccccc); }', {defined}), []);
    });

    it('accepts a raw colour where the file is allowed them', () => {
        assert.deepEqual(
            kinds('.a { color: #ff0000; }', {defined, allowRawColours: true}), []);
    });
});

describe('token references', () => {
    /**
     * The failure this exists to catch. A misspelled custom property is not an error anywhere - the
     * declaration is simply dropped, so `padding: var(--space-5)` is not 5px and not 4px, it is
     * NOTHING. On a portalled tooltip that took a working layout to zero padding with no warning
     * from the compiler, the bundler or the browser.
     */
    it('reports a token nothing defines', () => {
        assert.deepEqual(kinds('.a { padding: var(--space-5); }', {defined}), [KINDS.unknown]);
    });

    it('accepts a token the layer defines', () => {
        assert.deepEqual(kinds('.a { padding: var(--space-6); }', {defined}), []);
    });

    // The host's own variables are defined by VS Code, not by this repo, so they cannot be checked
    // against the layer - only that they look like host variables.
    it('accepts a host variable', () => {
        assert.deepEqual(kinds('.a { color: var(--vscode-foreground); }', {defined}), []);
    });

    it('accepts a property the file declares for itself', () => {
        const body = '.a { --crawl-to: 10px; transform: translateY(var(--crawl-to)); }';
        assert.deepEqual(kinds(body, {defined}), []);
    });

    // Set from JS as a computed key, which is how a component drives an animation it has measured.
    it('accepts a property the file declares outside the stylesheet', () => {
        const body = '.a { transform: translateY(var(--crawl-to)); }';
        const alsoDeclared = new Set(['--crawl-to']);

        assert.deepEqual(kinds(body, {defined, alsoDeclared}), []);
    });

    /**
     * A reference WITH a fallback is not the failure this rule is about. `var(--x)` alone drops the
     * whole declaration when --x is undefined; `var(--x, 100%)` resolves to 100% and behaves. The
     * message says the declaration is dropped, so it must only fire where that is true.
     */
    it('accepts an undefined token that carries a fallback', () => {
        assert.deepEqual(kinds('.a { transform: translateY(var(--nope, 100%)); }', {defined}), []);
    });
});

describe('reporting', () => {
    it('gives the offset and the text, so the driver can name a line', () => {
        const [offence] = tokenOffences('.a { gap: 7px; }', {defined});

        assert.equal(typeof offence.index, 'number');
        assert.match(offence.text, /7px/);
        assert.match(offence.hint, /--space-6|--space-8/);
    });
});

// #206: a panel reads the host's colours and fonts through the layer's roles, so the host is named
// once, in tokens.ts, and a theme problem is fixed in one place.
describe('host variables outside the token layer', () => {
    it('reports one in a stylesheet, at its line', () => {
        const text = 'const Shell = styled.div`\n  color: var(--vscode-foreground, #ccc);\n`;\n';
        assert.deepEqual(hostOffences(text).map(o => [o.line, o.name]), [[2, '--vscode-foreground']]);
    });

    it('reports one in a plain string, where a style object takes it', () => {
        const text = 'export const BLUE = "var(--vscode-charts-blue)";\n';
        assert.deepEqual(hostOffences(text).map(o => o.name), ['--vscode-charts-blue']);
    });

    it('names the role to use when the layer maps the variable', () => {
        const roles = new Map([['--vscode-foreground', '--colour-ink']]);
        const [offence] = hostOffences('color: var(--vscode-foreground);', roles);
        assert.match(offence.hint, /var\(--colour-ink\)/);
    });

    it('leaves a comment alone, which may name what a role stands for', () => {
        const text = '// --colour-muted is var(--vscode-descriptionForeground)\n * var(--vscode-foreground)\n';
        assert.deepEqual(hostOffences(text), []);
    });
});

// #207: a glyph is asked for by meaning through Icon.tsx, so the set it comes from is decided in one
// table. A raw codicon class anywhere else is reported at its line.
describe('codicon classes outside the icon seam', () => {
    it('reports a codicon class in JSX, at its line', () => {
        const text = 'const a = 1;\nreturn <span className="codicon codicon-close" />;\n';
        assert.deepEqual(codiconOffences(text).map(o => [o.line, o.name]), [[2, 'codicon-close']]);
    });

    it('reports a class built from a template, by its prefix', () => {
        const text = 'return <span className={`codicon codicon-${icon}`} />;\n';
        assert.deepEqual(codiconOffences(text).map(o => o.name), ['codicon-']);
    });

    it('leaves a comment line and a plain codicon selector alone', () => {
        const text = '// drawn as codicon-close before\n * the codicon-add glyph\n.dock-tile .codicon { font-size: 1px; }\n';
        assert.deepEqual(codiconOffences(text), []);
    });
});
