// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { definedLevels, proxyVisibleAt, type LevelTagged } from './levels';

const tagged = (over: Partial<LevelTagged> = {}): LevelTagged => ({
    alt: null,
    lod: null,
    altDecreaseStayHidden: false,
    ...over,
});

describe('proxyVisibleAt', () => {
    it('shows an untagged proxy at every level', () => {
        // The engine only ever touches tagged proxies; untagged ones are the model's normal effects.
        assert.equal(proxyVisibleAt(tagged(), 0, 0, false), true);
        assert.equal(proxyVisibleAt(tagged(), 7, 3, true), true);
    });

    it('shows a tagged proxy only at its own level', () => {
        assert.equal(proxyVisibleAt(tagged({ alt: 2 }), 2, 0, false), true);
        assert.equal(proxyVisibleAt(tagged({ alt: 2 }), 1, 0, false), false);
    });

    it('requires both levels to match when both are tagged', () => {
        const both = tagged({ alt: 1, lod: 2 });

        assert.equal(proxyVisibleAt(both, 1, 2, false), true);
        assert.equal(proxyVisibleAt(both, 1, 3, false), false);
        assert.equal(proxyVisibleAt(both, 0, 2, false), false);
    });

    it('keeps a stay-hidden proxy out while the damage state winds down', () => {
        // The repair asymmetry, straight from RenderObject::CheckAltLod: fire lit on the way to
        // destruction must not flicker back on as the hardpoint is repaired.
        const fire = tagged({ alt: 1, altDecreaseStayHidden: true });

        assert.equal(proxyVisibleAt(fire, 1, 0, false), true, 'increasing');
        assert.equal(proxyVisibleAt(fire, 1, 0, true), false, 'decreasing');
    });

    it('ignores the descending flag for a proxy that does not set stay-hidden', () => {
        assert.equal(proxyVisibleAt(tagged({ alt: 1 }), 1, 0, true), true);
    });
});

describe('definedLevels', () => {
    it('offers only the levels a model actually defines', () => {
        // Ten sliders' worth of levels on a model with two is a lie about what the file contains.
        const levels = definedLevels([
            tagged({ alt: 0 }),
            tagged({ alt: 2 }),
            tagged({ lod: 1 }),
        ]);

        assert.deepEqual(levels.alt, [0, 2]);
        // Zero is always there, so a defined level 1 reads as [0, 1].
        assert.deepEqual(levels.lod, [0, 1]);
    });

    it('always includes zero, which is the state a model opens in', () => {
        assert.deepEqual(definedLevels([tagged({ alt: 3 })]).alt, [0, 3]);
    });

    it('puts the close-up LOD last, because the engine numbers detail backwards', () => {
        // LOD0 is the DISTANT mesh: Ei_trooper is 282 triangles at LOD0 and 1078 at LOD2. Callers
        // default to the last entry to open a model on its detailed version rather than its crudest.
        const levels = definedLevels([tagged({ lod: 2 }), tagged({ lod: 1 })]);

        assert.deepEqual(levels.lod, [0, 1, 2]);
        assert.equal(levels.lod[levels.lod.length - 1], 2);
    });

    it('sorts numerically rather than as text', () => {
        // 10 before 2 would put the levels in the wrong order on the slider.
        const levels = definedLevels([tagged({ alt: 10 }), tagged({ alt: 2 })]);

        assert.deepEqual(levels.alt, [0, 2, 10]);
    });

    it('reports nothing beyond zero for a model with no tags at all', () => {
        assert.deepEqual(definedLevels([tagged(), tagged()]), { alt: [0], lod: [0] });
    });

    it('de-duplicates a level many proxies share', () => {
        const levels = definedLevels([tagged({ alt: 1 }), tagged({ alt: 1 }), tagged({ alt: 1 })]);

        assert.deepEqual(levels.alt, [0, 1]);
    });
});
