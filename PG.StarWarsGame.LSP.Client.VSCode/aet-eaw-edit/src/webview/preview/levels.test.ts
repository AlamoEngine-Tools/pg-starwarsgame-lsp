// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { stageForHull,
    costByLevel, definedLevels, levelLabel, levelSteps, proxyVisibleAt, type LevelTagged,
    withDeclaredStages,
} from './levels';

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
    it('offers no more DETAIL levels than the model defines', () => {
        // Ten sliders' worth of levels on a model with two is a lie about what the file contains.
        const levels = definedLevels([
            tagged({ alt: 0 }),
            tagged({ alt: 2 }),
            tagged({ lod: 1 }),
        ]);

        // ALT runs to the highest stage without gaps - a stage can exist with no geometry of its
        // own, so 1 here is a real damage state this model simply does not change for.
        assert.deepEqual(levels.alt, [0, 1, 2]);
        // Zero is always there, so a defined level 1 reads as [0, 1].
        assert.deepEqual(levels.lod, [0, 1]);
    });

    it('always includes zero, which is the state a model opens in', () => {
        assert.deepEqual(definedLevels([tagged({ lod: 3 })]).lod, [0, 3]);
        // And on the damage axis, everything up to the stage that IS tagged.
        assert.deepEqual(definedLevels([tagged({ alt: 3 })]).alt, [0, 1, 2, 3]);
    });

    it('puts the close-up LOD last, because the engine numbers detail backwards', () => {
        // LOD0 is the DISTANT mesh: Ei_trooper is 282 triangles at LOD0 and 1078 at LOD2. Callers
        // default to the last entry to open a model on its detailed version rather than its crudest.
        const levels = definedLevels([tagged({ lod: 2 }), tagged({ lod: 1 })]);

        assert.deepEqual(levels.lod, [0, 1, 2]);
        assert.equal(levels.lod[levels.lod.length - 1], 2);
    });

    it('sorts numerically rather than as text', () => {
        // 10 before 2 would put the levels in the wrong order on the slider. Asserted on LOD, the
        // axis that can still be sparse - ALT is filled in, so its order cannot go wrong.
        const levels = definedLevels([tagged({ lod: 10 }), tagged({ lod: 2 })]);

        assert.deepEqual(levels.lod, [0, 2, 10]);
    });

    it('reports nothing beyond zero for a model with no tags at all', () => {
        assert.deepEqual(definedLevels([tagged(), tagged()]), { alt: [0], lod: [0] });
    });

    it('de-duplicates a level many proxies share', () => {
        const levels = definedLevels([tagged({ alt: 1 }), tagged({ alt: 1 }), tagged({ alt: 1 })]);

        assert.deepEqual(levels.alt, [0, 1]);
    });
});

describe('levelSteps', () => {
    it('walks the levels the model DEFINES, not the ten the engine allows', () => {
        // The whole reason a slider is indexed rather than valued: a model defining ALT 0, 2 and 5
        // has three positions, and dragging must visit exactly those three. A slider over the raw
        // number would offer 1, 3 and 4, which name nothing in the file.
        const steps = levelSteps([0, 2, 5]);

        assert.equal(steps.count, 3);
        assert.equal(steps.levelAt(0), 0);
        assert.equal(steps.levelAt(1), 2);
        assert.equal(steps.levelAt(2), 5);
    });

    it('finds the position of the level currently shown', () => {
        const steps = levelSteps([0, 2, 5]);

        assert.equal(steps.positionOf(5), 2);
        assert.equal(steps.positionOf(2), 1);
    });

    it('falls back to the first position for a level the model does not define', () => {
        // The subject changes under the control - open another model and the old level may not
        // exist. Answering "nowhere" would leave the thumb off the track.
        assert.equal(levelSteps([0, 2, 5]).positionOf(3), 0);
    });

    it('clamps a position that ran off either end', () => {
        const steps = levelSteps([0, 2, 5]);

        assert.equal(steps.levelAt(-1), 0);
        assert.equal(steps.levelAt(99), 5);
    });

    it('survives an empty list rather than reading undefined', () => {
        const steps = levelSteps([]);

        assert.equal(steps.count, 0);
        assert.equal(steps.levelAt(0), 0);
        assert.equal(steps.positionOf(0), 0);
    });
});

describe('levelLabel', () => {
    it('says what the ends of the DAMAGE axis mean', () => {
        assert.equal(levelLabel('alt', 0, [0, 2]), '0 (undamaged)');
        assert.equal(levelLabel('alt', 2, [0, 2]), '2');
    });

    it('says what the ends of the DETAIL axis mean, which run the opposite way', () => {
        // LOD 0 is the DISTANT mesh and the highest is the close-up - the engine's numbering is
        // inverted against every reader's expectation, so both ends are named.
        assert.equal(levelLabel('lod', 0, [0, 1, 2]), '0 (distant)');
        assert.equal(levelLabel('lod', 2, [0, 1, 2]), '2 (close-up)');
        assert.equal(levelLabel('lod', 1, [0, 1, 2]), '1');
    });

    it('names a single-level axis by both ends at once, since it is both', () => {
        assert.equal(levelLabel('lod', 0, [0]), '0 (distant)');
    });

    // The plate is sized to hold a word, so where the object declares a damage table the space
    // carries the health band the stage covers instead - which is the one thing about a stage the
    // reader cannot get anywhere else in the preview. The bands come from the same reading as
    // `stageForHull`: a row's threshold is its ceiling, the next row's is its floor, and the last
    // row runs to zero.
    describe('the health band, where the object declares one', () => {
        // 98 of the 161 shipped tables, and the only shape that names all four stages.
        const full = [
            { threshold: 1, stage: 0 },
            { threshold: 0.66, stage: 1 },
            { threshold: 0.33, stage: 2 },
            { threshold: 0, stage: 3 },
        ];

        it('reads a band from its own ceiling to the next row down', () => {
            assert.equal(levelLabel('alt', 0, [0, 1, 2, 3], { table: full }), '0 (100-66%)');
            assert.equal(levelLabel('alt', 1, [0, 1, 2, 3], { table: full }), '1 (66-33%)');
        });

        it('runs the last row to zero rather than to its own threshold', () => {
            // 26 shipped tables stop at `1, 0.66, 0.33` and their bottom stage covers what is left.
            const three = full.slice(0, 3);

            assert.equal(levelLabel('alt', 2, [0, 1, 2], { table: three }), '2 (33-0%)');
        });

        it('writes a band with no width as the one number it is', () => {
            // A trailing `0` row is entered at exactly zero health and nowhere else. `0-0%` would
            // read as a range the stage does not have.
            assert.equal(levelLabel('alt', 3, [0, 1, 2, 3], { table: full }), '3 (0%)');
        });

        it('gives stage 0 the room above the highest threshold', () => {
            // 2 shipped objects declare `0.66, 0.33, 0` against `1, 2, 3` and rely on the engine
            // answering stage 0 above the top row. The band is real; the table just never names it.
            const topless = full.slice(1);

            assert.equal(levelLabel('alt', 0, [0, 1, 2, 3], { table: topless }), '0 (100-66%)');
        });

        it('keeps the word where stage 0 is unreachable', () => {
            // 29 shipped objects declare the single row `1 -> 3`. Every health fraction is at or
            // under 1, so stage 0 is never entered and its band has no width to name.
            const single = [{ threshold: 1, stage: 3 }];

            assert.equal(levelLabel('alt', 0, [0, 3], { table: single }), '0 (undamaged)');
            assert.equal(levelLabel('alt', 3, [0, 3], { table: single }), '3 (100-0%)');
        });

        it('falls back to the word for a stage the table says nothing about', () => {
            // The axis is the UNION of what the model tags and what the XML declares, so a model
            // may offer a stage the table never places.
            assert.equal(levelLabel('alt', 4, [0, 1, 2, 3, 4], { table: full }), '4');
            assert.equal(levelLabel('alt', 0, [0, 2], { table: [] }), '0 (undamaged)');
        });

        it('leaves the DETAIL axis alone - nothing in the XML has an opinion about it', () => {
            assert.equal(levelLabel('lod', 0, [0, 1], { table: full }), '0 (distant)');
        });
    });

    // What a detail level COSTS is the one thing about it a reader cannot get anywhere else, and
    // unlike `distant` it is measured per model rather than generic. It also says which way the
    // axis runs more plainly than the word does - 1,507 up to 1,882 is not ambiguous.
    describe('the triangle count, where the geometry has arrived', () => {
        it('names the count instead of the end word', () => {
            assert.equal(levelLabel('lod', 0, [0, 1, 2], { cost: { meshes: 4, triangles: 1507 } }),
                '0 (4 meshes, 1,507 tris)');
            assert.equal(levelLabel('lod', 2, [0, 1, 2], { cost: { meshes: 4, triangles: 1882 } }),
                '2 (4 meshes, 1,882 tris)');
        });

        it('groups thousands with ASCII commas whatever the reader locale is', () => {
            // The house rule is ASCII only, and a bare `toLocaleString` writes a non-breaking space
            // in about half the locales VS Code ships.
            const label = levelLabel('lod', 3, [0, 3], { cost: { meshes: 1070, triangles: 103220 } });

            assert.equal(label, '3 (1,070 meshes, 103,220 tris)');
            assert.ok(/^[\x20-\x7e]*$/.test(label), label);
        });

        it('counts one of either as one', () => {
            assert.equal(levelLabel('lod', 1, [0, 1], { cost: { meshes: 1, triangles: 1 } }),
                '1 (1 mesh, 1 tri)');
        });

        it('keeps the word until the geometry has actually arrived', () => {
            // A level whose meshes have not loaded is not a level of zero triangles, and saying so
            // would put a wrong number on screen for the whole of a big model's load.
            assert.equal(levelLabel('lod', 0, [0, 1], { cost: { meshes: 0, triangles: 0 } }),
                '0 (distant)');
            assert.equal(levelLabel('lod', 0, [0, 1], { cost: null }), '0 (distant)');
            assert.equal(levelLabel('lod', 1, [0, 1], {}), '1 (close-up)');
        });

        it('leaves the DAMAGE axis alone - a stage is not a detail level', () => {
            assert.equal(levelLabel('alt', 0, [0, 1], { cost: { meshes: 4, triangles: 1507 } }),
                '0 (undamaged)');
        });
    });
});

// One rule decides this, and it is `isVisibleAtLevel` - the same one the viewport gates meshes
// with. Writing a second copy here is the trap that has already cost this codebase twice.
describe('costByLevel', () => {
    const mesh = (triangles: number, alt?: number, lod?: number) =>
        ({ extras: { alamoAlt: alt, alamoLod: lod }, triangles });

    it('counts an untagged mesh at EVERY level', () => {
        // `W_tree_alien_00_hi`, measured: 4 meshes, one of them untagged at 1336 triangles, and the
        // three tagged ones at 171, 352 and 546. The untagged trunk draws at all three levels, so
        // the level totals are 1507/1688/1882 - not the tagged meshes alone, which would report a
        // 1507-triangle tree as 171.
        const counts = costByLevel([
            mesh(1336),
            mesh(171, undefined, 0), mesh(352, undefined, 1), mesh(546, undefined, 2),
        ], 0, [0, 1, 2]);

        assert.deepEqual([...counts.entries()], [
            [0, { meshes: 2, triangles: 1507 }],
            [1, { meshes: 2, triangles: 1688 }],
            [2, { meshes: 2, triangles: 1882 }],
        ]);
    });

    it('counts only the meshes the CURRENT damage stage draws', () => {
        // A mesh tagged for another stage is not part of this level's cost - it is not drawn.
        const counts = costByLevel([
            mesh(100, 0, 0), mesh(900, 1, 0), mesh(7, undefined, 0),
        ], 0, [0]);

        assert.deepEqual([...counts.entries()], [[0, { meshes: 2, triangles: 107 }]]);
    });

    it('reports the levels asked for, sparse ones included', () => {
        // A model can define LOD 0, 2 and 5 and nothing between - see `levelSteps`.
        const counts = costByLevel([mesh(60, undefined, 5)], 0, [0, 2, 5]);

        assert.deepEqual([...counts.entries()], [
            [0, { meshes: 0, triangles: 0 }],
            [2, { meshes: 0, triangles: 0 }],
            [5, { meshes: 1, triangles: 60 }],
        ]);
    });

    it('answers zero rather than nothing before any geometry has arrived', () => {
        assert.deepEqual([...costByLevel([], 0, [0, 1]).entries()], [
            [0, { meshes: 0, triangles: 0 }],
            [1, { meshes: 0, triangles: 0 }],
        ]);
    });
});

describe('definedLevels: ALT is contiguous, LOD is not', () => {
    const tagged = (alt: number | null, lod: number | null): LevelTagged =>
        ({ alt, lod, altDecreaseStayHidden: false });

    it('fills the gaps in the DAMAGE axis', () => {
        // A damage stage is a stage whether or not the model changes for it. One may exist purely
        // as an explosion declared in XML, with no mesh tagged for it at all - so a gap between two
        // tagged levels is a real stage that this model happens to draw no differently, and
        // skipping it would step the reader straight past a state the unit has.
        const levels = definedLevels([tagged(0, null), tagged(3, null)]);

        assert.deepEqual(levels.alt, [0, 1, 2, 3]);
    });

    it('leaves the DETAIL axis exactly as the model tags it', () => {
        // LOD is only ever a property of geometry - there is no such thing as a detail level that
        // exists outside the model - so an untagged level between two tagged ones is nothing, and
        // offering it would ask the viewport for meshes that do not exist.
        const levels = definedLevels([tagged(null, 0), tagged(null, 3)]);

        assert.deepEqual(levels.lod, [0, 3]);
    });

    it('still answers a single level on each axis for an untagged model', () => {
        const levels = definedLevels([tagged(null, null)]);

        assert.deepEqual(levels.alt, [0]);
        assert.deepEqual(levels.lod, [0]);
    });
});

describe('withDeclaredStages', () => {
    it('reaches a stage the model draws nothing for', () => {
        // 35 shipped structures declare the lone alternate 3. The geometry mentions no such stage,
        // so without the declaration the slider stopped at 0 and the reader could never reach the
        // state the building actually has.
        const levels = withDeclaredStages({ alt: [0], lod: [0, 1] }, [3]);

        assert.deepEqual(levels.alt, [0, 1, 2, 3]);
        assert.deepEqual(levels.lod, [0, 1], 'detail is not the declaration s business');
    });

    it('keeps stage zero even when the declaration omits it', () => {
        // 7 shipped objects declare 1, 2, 3 with no zero. Zero is still where a model opens - it is
        // the undamaged state - so the axis runs from it regardless.
        assert.deepEqual(withDeclaredStages({ alt: [0], lod: [0] }, [1, 2, 3]).alt, [0, 1, 2, 3]);
    });

    it('keeps the model s own stages when they run higher than the declaration', () => {
        // Neither source is authoritative alone: the XML names stages with no geometry, and a model
        // may tag one the XML forgot. The union is what the reader can actually inspect.
        assert.deepEqual(withDeclaredStages({ alt: [0, 1, 2, 3, 4], lod: [0] }, [2]).alt,
            [0, 1, 2, 3, 4]);
    });

    it('changes nothing when no stages are declared', () => {
        // Every space unit: the trio is a ground-structure thing.
        const levels = { alt: [0, 1], lod: [0, 2] };

        assert.deepEqual(withDeclaredStages(levels, []).alt, [0, 1]);
        assert.deepEqual(withDeclaredStages(levels, []).lod, [0, 2]);
    });
});

// USER RULE, 2026-09-04: the thresholds are the upper and LOWER bound of each stage, not a list of
// trip points. `1, 0.66, 0.33, 0` against `0, 1, 2, 3` reads
//     100% > h > 66% -> ALT0,  66% > h > 33% -> ALT1,  33% > h > 0% -> ALT2,  then ALT3.
// So a row's own threshold is its ceiling, the next row's is its floor, and the last runs to zero.
//
// This is what drives the stage in GAMEPLAY. Until now nothing did: `Land_Damage_Thresholds` was
// never read anywhere in the solution, and the ALT slider was hidden in Gameplay on the stated
// grounds that the damage state followed from play - which it never did.
describe('stageForHull', () => {
    const table = [
        { threshold: 1, stage: 0 },
        { threshold: 0.66, stage: 1 },
        { threshold: 0.33, stage: 2 },
        { threshold: 0, stage: 3 },
    ];

    it('opens undamaged at full health', () => {
        assert.equal(stageForHull(table, 1), 0);
    });

    it('holds the first band down to its floor', () => {
        assert.equal(stageForHull(table, 0.9), 0);
        assert.equal(stageForHull(table, 0.67), 0);
    });

    it('steps at each boundary', () => {
        assert.equal(stageForHull(table, 0.66), 1);
        assert.equal(stageForHull(table, 0.5), 1);
        assert.equal(stageForHull(table, 0.33), 2);
        assert.equal(stageForHull(table, 0.1), 2);
    });

    it('reaches the last stage at nothing left', () => {
        assert.equal(stageForHull(table, 0), 3);
    });

    // 45 shipped objects stop at three bands. Below the last threshold there is no further row, so
    // the last one runs to zero rather than falling back to undamaged.
    it('runs the last band to zero when the table stops short', () => {
        const short = [
            { threshold: 1, stage: 0 },
            { threshold: 0.66, stage: 1 },
            { threshold: 0.33, stage: 2 },
        ];

        assert.equal(stageForHull(short, 0.2), 2);
        assert.equal(stageForHull(short, 0), 2);
    });

    // 7 objects declare no band at the top - `0.66, 0.33, 0` against `1, 2, 3`. Above the highest
    // threshold nothing is declared, and the undamaged model is what the engine opens with.
    it('is undamaged above the highest declared band', () => {
        const late = [
            { threshold: 0.66, stage: 1 },
            { threshold: 0.33, stage: 2 },
            { threshold: 0, stage: 3 },
        ];

        assert.equal(stageForHull(late, 1), 0);
        assert.equal(stageForHull(late, 0.8), 0);
        assert.equal(stageForHull(late, 0.66), 1);
    });

    // 35 objects declare exactly `1 -> 3`. One band with no floor covers the whole range, so they
    // sit at ALT3 from full health. Odd, and it is what the data says.
    it('covers the whole range from a single band', () => {
        const one = [{ threshold: 1, stage: 3 }];

        assert.equal(stageForHull(one, 1), 3);
        assert.equal(stageForHull(one, 0.5), 3);
        assert.equal(stageForHull(one, 0), 3);
    });

    it('answers undamaged when there is no table at all', () => {
        assert.equal(stageForHull([], 0.1), 0);
    });

    // A hull pool that has not been established yet, or a unit with no health to speak of.
    it('takes a fraction outside 0..1 without inventing a stage', () => {
        assert.equal(stageForHull(table, 1.5), 0);
        assert.equal(stageForHull(table, -1), 3);
        assert.equal(stageForHull(table, Number.NaN), 0);
    });
});
