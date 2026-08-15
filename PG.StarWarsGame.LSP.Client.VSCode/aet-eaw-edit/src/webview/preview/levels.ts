// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Damage states and detail levels, the way the engine gates them.
//
// ALT and LOD are encoded in mesh and proxy NAMES - `_ALT2`, `_LOD1` - which the reader strips into
// numbers. The rule is `RenderObject::CheckAltLod`: only TAGGED things are ever switched, so an
// untagged mesh is the hull itself and must never be hidden by a level change.

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

    return { alt: ascending(alt), lod: ascending(lod) };
}
