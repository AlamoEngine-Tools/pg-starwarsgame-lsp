// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { flattenIncludes, type ShaderReader } from './includes';

const reader = (files: Record<string, string>): ShaderReader =>
    name => files[name.toLowerCase()] ?? null;

describe('flattenIncludes', () => {
    it('splices a header in where it was included', () => {
        const result = flattenIncludes('a\n#include "H.fxh"\nb',
            reader({ 'h.fxh': 'header' }));

        assert.equal(result.text.replace(/\s+/g, ' ').trim(), 'a header b');
        assert.deepEqual(result.included, ['H.fxh']);
    });

    it('follows a header that includes another', () => {
        const result = flattenIncludes('#include "A.fxh"',
            reader({ 'a.fxh': '#include "B.fxh"\nfromA', 'b.fxh': 'fromB' }));

        // Depth first, so B's declarations land before A's code, which needs them.
        assert.match(result.text.replace(/\s+/g, ' '), /fromB fromA/);
        assert.deepEqual(result.included, ['A.fxh', 'B.fxh']);
    });

    it('splices a shared header only once', () => {
        // The shipped headers include each other freely. Repeating one redeclares every uniform in
        // it, which will not compile.
        const result = flattenIncludes('#include "A.fxh"\n#include "B.fxh"',
            reader({
                'a.fxh': '#include "Shared.fxh"\nfromA',
                'b.fxh': '#include "Shared.fxh"\nfromB',
                'shared.fxh': 'uniform float x;',
            }));

        assert.equal(result.text.match(/uniform float x;/g)?.length, 1);
    });

    it('survives headers that include each other', () => {
        const result = flattenIncludes('#include "A.fxh"',
            reader({ 'a.fxh': '#include "B.fxh"\nfromA', 'b.fxh': '#include "A.fxh"\nfromB' }));

        assert.match(result.text, /fromA/);
        assert.match(result.text, /fromB/);
    });

    it('reports a header it cannot read rather than failing', () => {
        // Usually means the managed copy is incomplete. The caller falls back to an archetype, and
        // knowing which header is missing is what makes that explainable.
        const result = flattenIncludes('#include "Absent.fxh"\nbody', reader({}));

        assert.deepEqual(result.missing, ['Absent.fxh']);
        assert.match(result.text, /body/);
    });

    it('matches the name case-insensitively, as the files themselves are inconsistent', () => {
        const result = flattenIncludes('#include "AlamoEngine.fxh"',
            reader({ 'alamoengine.fxh': 'engine' }));

        assert.match(result.text, /engine/);
    });

    it('leaves an include inside a comment alone', () => {
        // Anchored to its own line, so a commented-out include is not resurrected.
        const result = flattenIncludes('// #include "H.fxh"', reader({ 'h.fxh': 'header' }));

        assert.doesNotMatch(result.text, /header/);
    });
});
