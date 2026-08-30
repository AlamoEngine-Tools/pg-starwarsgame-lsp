// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Damage states and detail levels, the way the engine gates them.
//
// ALT and LOD are encoded in mesh and proxy NAMES - `_ALT2`, `_LOD1` - which the reader strips into
// numbers. The rule is `RenderObject::CheckAltLod`: only TAGGED things are ever switched, so an
// untagged mesh is the hull itself and must never be hidden by a level change.

import { isVisibleAtLevel, type MaterialExtras } from './materials';

/** The level tags something carries, with null meaning untagged. */
export interface LevelTagged {
    alt: number | null;
    lod: number | null;
    /** See {@link proxyVisibleAt}. Meshes do not have this; only proxies do. */
    altDecreaseStayHidden: boolean;
}

/**
 * Whether a proxy is drawn at the given levels.
 *
 * `altDescending` is a property of the last CHANGE, not a state: the engine computes it as
 * `alt < m_alt` inside `SetALT` and passes `false` for every LOD change. A proxy that sets
 * `altDecreaseStayHidden` is held back on a decreasing step, which is what stops a repaired
 * hardpoint's fire flickering back on as the damage state winds down.
 */
export function proxyVisibleAt(
    proxy: LevelTagged, alt: number, lod: number, altDescending: boolean,
): boolean {
    // Untagged proxies are never touched by a level change.
    if (proxy.alt === null && proxy.lod === null) {
        return true;
    }

    if (proxy.lod !== null && proxy.lod !== lod) {
        return false;
    }

    if (proxy.alt !== null && proxy.alt !== alt) {
        return false;
    }

    return !(proxy.altDecreaseStayHidden && altDescending);
}

/** The ALT and LOD levels a model actually defines, ascending, always including zero. */
export interface DefinedLevels {
    alt: number[];
    lod: number[];
}

/**
 * Which levels are worth offering.
 *
 * `NUM_ALTS` and `NUM_LODS` are both 10 in the engine, but showing ten steps for a model that
 * defines two claims the file contains something it does not.
 *
 * Zero is always included, though it means different things on the two axes. ALT 0 is the undamaged
 * state, which is where a model starts. LOD 0 is the DISTANT, lowest-detail mesh - the engine's
 * numbering runs the opposite way to most, measured across the shipped models: Ei_trooper is 282
 * triangles at LOD0 and 1078 at LOD2. Callers wanting the close-up mesh want the HIGHEST level.
 *
 * **The two axes are NOT symmetric, and only one of them may have gaps.**
 *
 * ALT is a sequence of damage STAGES, and a stage is a stage whether or not this model draws
 * anything different for it: one may exist purely as an explosion declared in the XML, with no mesh
 * or proxy tagged for it anywhere in the file. Since this function can only ever see what the MODEL
 * tags, a gap between two tagged levels is a real stage it cannot observe - so the range is filled
 * in to the highest tagged level rather than left sparse. Stepping past a state the unit actually
 * has is the worse error.
 *
 * LOD has no such life outside the model: a detail level IS geometry, so an untagged level between
 * two tagged ones is nothing at all, and offering it would ask the viewport for meshes that do not
 * exist. That axis stays exactly as tagged.
 *
 * **Known limit**: a damage stage ABOVE the highest one the model tags is invisible here, for the
 * same reason - nothing in the geometry mentions it. Reading the true stage count needs the XML,
 * which this side does not have.
 */
export function definedLevels(tagged: Iterable<LevelTagged>): DefinedLevels {
    const alt = new Set<number>([0]);
    const lod = new Set<number>([0]);

    for (const item of tagged) {
        if (item.alt !== null) {
            alt.add(item.alt);
        }
        if (item.lod !== null) {
            lod.add(item.lod);
        }
    }

    // Numeric sort: the default is lexicographic, which puts 10 before 2.
    const ascending = (values: Set<number>): number[] => [...values].sort((a, b) => a - b);

    // ALT fills its gaps; LOD does not. See the note above - a damage stage can exist without the
    // model drawing anything for it, and a detail level cannot.
    const stages = ascending(alt);
    const contiguous = Array.from({ length: stages[stages.length - 1] + 1 }, (_, i) => i);

    return { alt: contiguous, lod: ascending(lod) };
}

/** A slider's worth of positions over the levels a model actually defines. */
export interface LevelSteps {
    /** How many positions the slider has. Zero when the model defines nothing to choose between. */
    count: number;
    /** The level shown at a slider position, clamped to the ends. */
    levelAt(position: number): number;
    /** Where the thumb belongs for a level, or the first position when it is not defined here. */
    positionOf(level: number): number;
}

/**
 * Indexes a slider over the DEFINED levels rather than over the raw numbers.
 *
 * This is the whole reason the control is indexed. A level list may be sparse - a model can define
 * LOD 0, 2 and 5 and nothing between - so a slider running 0..5 would offer three positions that
 * name nothing in the file, and dragging through them would ask the viewport for meshes that do not
 * exist. Three defined levels means three stops.
 *
 * ALT arrives here already filled in (see {@link definedLevels}), so its positions and its levels
 * happen to coincide. That is a property of the input, not of this function.
 */
export function levelSteps(levels: readonly number[]): LevelSteps {
    return {
        count: levels.length,

        levelAt(position: number): number {
            if (levels.length === 0) {
                return 0;
            }

            const clamped = Math.min(levels.length - 1, Math.max(0, Math.round(position)));

            return levels[clamped];
        },

        // The subject changes under the control: open another model and the level the reader left
        // set may not exist there. Answering "nowhere" would put the thumb off its own track, so
        // an unknown level reads as the first position - which is where a fresh subject opens.
        positionOf(level: number): number {
            const at = levels.indexOf(level);

            return at < 0 ? 0 : at;
        },
    };
}

/**
 * The health band one damage stage covers, as whole percentages, or null where it covers none.
 *
 * Read exactly as {@link stageForHull} reads it, from the same rows: a row's own threshold is its
 * ceiling, the next row's is its floor, and the LAST row runs to zero. Stage 0 is the one stage
 * that need not appear in the table at all - it is what the engine answers above the highest
 * declared threshold - so it takes the room above the top row, which is nothing at all on the 34
 * shipped tables whose top row already tops out at full health.
 *
 * Measured over the shipped corpus: 161 objects declare a usable table, in six distinct shapes, and
 * NOT ONE names a stage twice. So a stage has at most one band and this cannot be ambiguous.
 */
function stageBand(table: readonly DamageBand[], stage: number): string | null {
    const at = table.findIndex(band => band.stage === stage);

    let top: number;
    let bottom: number;

    if (at >= 0) {
        top = table[at].threshold;
        bottom = at === table.length - 1 ? 0 : table[at + 1].threshold;
    } else if (stage === 0) {
        top = 1;
        bottom = Math.max(...table.map(band => band.threshold));
    } else {
        return null;
    }

    const high = Math.round(top * 100);
    const low = Math.round(bottom * 100);

    if (high < low) {
        return null;
    }

    // A trailing `0` row is entered at exactly zero health and nowhere else, and stage 0 above a
    // top row of 1 is never entered at all. Writing either as `0-0%` or `100-100%` would name a
    // range the stage does not have.
    if (high === low) {
        return high === 0 ? '0%' : null;
    }

    return `${high}-${low}%`;
}

/** What one detail level draws. */
export interface LevelCost {
    meshes: number;
    triangles: number;
}

/**
 * What each detail level costs, at the CURRENT damage stage.
 *
 * The gate is {@link isVisibleAtLevel}, the same one the viewport switches meshes with, because
 * this must answer the identical question - which meshes does level N draw - and a second copy of
 * that rule is how this codebase has twice ended up with two readers disagreeing.
 *
 * It is also the whole of why an untagged mesh cannot simply be skipped. `W_tree_alien_00_hi`
 * carries 4 meshes: three tagged LOD0/1/2 at 171, 352 and 546 triangles, and one UNTAGGED trunk at
 * 1336 that draws at every level. Counting the tagged meshes alone would report a tree that draws
 * 1507 triangles as 171.
 *
 * Counts what the LEVEL holds, not what survives the reader's own visibility toggles: a number that
 * changed when a tree row was unticked would not be a property of the level at all.
 */
export function costByLevel(
    meshes: Iterable<{ extras: MaterialExtras; triangles: number }>,
    alt: number, levels: readonly number[],
): Map<number, LevelCost> {
    const counts = new Map<number, LevelCost>(
        levels.map(level => [level, { meshes: 0, triangles: 0 }]));

    for (const mesh of meshes) {
        for (const level of levels) {
            const cost = counts.get(level);
            if (cost !== undefined && isVisibleAtLevel(mesh.extras, alt, level)) {
                cost.meshes++;
                cost.triangles += mesh.triangles;
            }
        }
    }

    return counts;
}

/** What a level's label may say beyond its number. Both are per-axis; neither is always known. */
export interface LevelDetail {
    /** The object's damage table, for the health band a stage covers. The DAMAGE axis only. */
    table?: readonly DamageBand[];
    /** What this detail level draws. The DETAIL axis only; null or empty until geometry arrives. */
    cost?: LevelCost | null;
}

/**
 * What to call one level, with the ends of each axis named.
 *
 * A bare number is not enough on either axis and is actively misleading on one: the engine's LOD
 * numbering runs the OPPOSITE way to every reader's expectation - 0 is the distant, lowest-detail
 * mesh and the highest number is the close-up. Measured across the shipped models, `Ei_trooper` is
 * 282 triangles at LOD0 and 1078 at LOD2. Naming both ends is what stops a reader dragging the
 * wrong way.
 *
 * Both axes say more than that where they can, because the plate is sized for a word either way and
 * the measured fact is worth more than the generic one.
 *
 * The DAMAGE axis spends the room on the health band the stage covers, where the object declares a
 * table. `undamaged` stays as the fallback for stage 0 wherever there is no band to name, which is
 * every space unit and every ground structure whose table cannot reach stage 0.
 *
 * The DETAIL axis spends it on what the level draws, in meshes and triangles. That REPLACES the end
 * words rather than joining them - all three together measure 195px against a plate the damage axis
 * fills at 88 - and it makes the same point the words were there to make: 1,507 rising to 1,882
 * says which way the axis runs, per model, without the reader taking `distant` on trust. The words
 * come back while the geometry is still loading, since a level whose meshes have not arrived is not
 * a level of zero triangles.
 */
export function levelLabel(
    axis: 'alt' | 'lod', level: number, levels: readonly number[],
    detail: LevelDetail = {},
): string {
    if (axis === 'alt') {
        const table = detail.table ?? [];
        const band = table.length > 0 ? stageBand(table, level) : null;

        if (band !== null) {
            return `${level} (${band})`;
        }

        return level === 0 ? '0 (undamaged)' : String(level);
    }

    const cost = detail.cost ?? null;
    if (cost !== null && cost.triangles > 0) {
        // `en-US` rather than the reader's locale: the house rule is ASCII only, and about half the
        // locales VS Code ships group thousands with a non-breaking space.
        const count = (value: number): string => value.toLocaleString('en-US');

        return `${level} (${count(cost.meshes)} mesh${cost.meshes === 1 ? '' : 'es'}, `
            + `${count(cost.triangles)} tri${cost.triangles === 1 ? '' : 's'})`;
    }

    if (level === 0) {
        return '0 (distant)';
    }

    return level === levels[levels.length - 1] ? `${level} (close-up)` : String(level);
}

/**
 * Folds the object's DECLARED damage stages into the levels the model tags.
 *
 * Neither source is authoritative alone, which is the whole reason this exists.
 *
 * `definedLevels` sees only what the geometry tags, and a damage stage need not touch the geometry
 * at all - it may be nothing but an explosion and a sound in the XML. The shipped data proves it
 * rather than merely allowing for it: 35 objects declare the lone alternate `3`, and 7 declare
 * `1, 2, 3` with no stage zero. Read from the model alone, those buildings offer one stage.
 *
 * The declaration is not complete either - it is a ground-structure convention, absent from every
 * space unit, and a model may tag a stage the XML forgot. So this takes the UNION, and always runs
 * from zero: zero is the undamaged state a model opens in whether or not the table names it.
 *
 * LOD is untouched. A detail level IS geometry, and nothing in the XML has an opinion about it.
 */
export function withDeclaredStages(
    levels: DefinedLevels, declared: readonly number[],
): DefinedLevels {
    if (declared.length === 0) {
        return levels;
    }

    const highest = Math.max(
        levels.alt[levels.alt.length - 1] ?? 0,
        ...declared.filter(stage => Number.isFinite(stage) && stage >= 0));

    return {
        alt: Array.from({ length: highest + 1 }, (_, stage) => stage),
        lod: levels.lod,
    };
}

/** One band of the damage table: the health fraction it tops out at, and the stage inside it. */
export interface DamageBand {
    threshold: number;
    stage: number;
}

/**
 * The damage stage a hull fraction puts the model in.
 *
 * The thresholds are the upper and LOWER bound of each stage rather than a list of trip points.
 * `1, 0.66, 0.33, 0` against `0, 1, 2, 3` reads: 100% > h > 66% is ALT0, 66% > h > 33% is ALT1,
 * 33% > h > 0% is ALT2, and 0 is ALT3. A row's own threshold is its ceiling, the next row's is its
 * floor, and the LAST row runs all the way to zero - 45 shipped objects stop at three bands, and
 * their bottom stage has to cover what is left rather than falling back to undamaged.
 *
 * Above the highest declared threshold the answer is stage 0, the undamaged model the engine opens
 * with. 7 objects declare `0.66, 0.33, 0` against `1, 2, 3` and rely on exactly that.
 *
 * Rows are taken in WRITTEN order and a stage may repeat; nothing here sorts or de-duplicates,
 * because the pairing is positional and the order is the whole meaning of it.
 */
export function stageForHull(table: readonly DamageBand[], fraction: number): number {
    if (table.length === 0 || Number.isNaN(fraction)) {
        return 0;
    }

    // The last band whose ceiling the hull is still at or under. Walking forwards and keeping the
    // last match is what makes the final row run to zero without a special case.
    let stage = 0;
    let found = false;

    for (const band of table) {
        if (fraction <= band.threshold) {
            stage = band.stage;
            found = true;
        }
    }

    return found ? stage : 0;
}
