// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { resolveColourWith } from './resolveColour';

/** A stand-in computed style: every property not listed is undefined, as the browser reports it. */
function reader(defined: Record<string, string>) {
    return (name: string): string => defined[name] ?? '';
}

describe('resolveColourWith', () => {
    it('takes the token when the layer is on the element', () => {
        const read = reader({
            '--colour-data-blue': ' #0000ff ',
            '--vscode-charts-blue': '#111111',
        });

        assert.equal(resolveColourWith(read, '--colour-data-blue'), '#0000ff');
    });

    /**
     * The step that earns its place. If reading a token whose value is itself a var() turns out to
     * report nothing, the host variable it defers to is still readable, and the colour is the one
     * the theme actually wants rather than the shipped fallback.
     */
    it('falls through to the host variable when the token reads back empty', () => {
        const read = reader({ '--vscode-charts-blue': '#222222' });

        assert.equal(resolveColourWith(read, '--colour-data-blue'), '#222222');
    });

    it('uses the token fallback only when the host defines neither', () => {
        assert.equal(resolveColourWith(reader({}), '--colour-data-blue'), '#3794ff');
    });

    // A caller painting with an empty string draws nothing, which someone notices. A plausible grey
    // would hide the typo for as long as the panel survives.
    it('resolves an unknown token to nothing rather than to a guess', () => {
        assert.equal(resolveColourWith(reader({}), '--colour-not-a-token'), '');
    });

    it('does not confuse a whitespace-only value for a defined one', () => {
        const read = reader({ '--colour-muted': '   ', '--vscode-descriptionForeground': '#333333' });

        assert.equal(resolveColourWith(read, '--colour-muted'), '#333333');
    });
});
