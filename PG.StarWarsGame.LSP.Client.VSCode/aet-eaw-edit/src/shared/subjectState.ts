// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the reader had set up for THIS subject, for as long as the window lives.
//
// Tier 2 of the settings model (see `viewerSettings.ts` for the rule): these describe one model
// rather than the room, so they follow the subject rather than the person, and they are deliberately
// NOT written to disk. The opening rules beat persistence - a preview opened fresh must show the
// model undamaged, at full detail and quiet - and restoring a damage state from last week would
// look like a broken file rather than like a preference.
//
// Within a session it is the opposite: closing a model and opening it again should pick up where it
// left off, which is what this carries.
//

/** One subject's session state. Every field is safe to apply blind; the caller checks levels. */
export interface SubjectState {
    /** Damage state. Checked against the model's own levels before it is applied. */
    alt: number;
    /**
     * Detail level, same - or null while the reader has not PICKED one.
     *
     * Null rather than 0 because every model tags LOD 0, and in Alamo LOD 0 is the LOWEST detail: a
     * recorded 0 nobody chose read back as a choice, and a preview closed before its geometry arrived
     * came back at LOD 0 on the next open. See {@link lodToRecord} and {@link lodOnArrival}.
     */
    lod: number | null;
    /** Hardpoints the reader blew off, by id. */
    destroyed: string[];
    /** Emitters switched off, by their index in the opened system's file. */
    hiddenEmitters: number[];
    /** The clip that was picked, by name. */
    animation: string | null;
    /** The tree's own filter, so a search survives a reopen. */
    filterText: string;
    /** Rows that were collapsed. Bone ids are stable; a stale one simply never matches. */
    collapsed: string[];
    /**
     * Rows switched off by hand.
     *
     * Restorable since the tree keys a mesh by its part, bone and position rather than by a
     * three.js uuid that was a different value every load.
     */
    hidden: string[];
    /**
     * Rows switched ON by hand.
     *
     * The counterpart to {@link hidden}, and needed because the two kinds of row start from
     * opposite defaults. A mesh is drawn until someone hides it, so its hidden list round-trips it.
     * A particle system is mostly the reverse - the model opens quiet, so all but the engines start
     * off - and the interesting act is lighting one. With only `hidden` recorded, every effect the
     * reader had deliberately switched on was lost on reopening.
     */
    shown: string[];
}

export const EMPTY_SUBJECT_STATE: SubjectState = {
    alt: 0,
    lod: null,
    destroyed: [],
    hiddenEmitters: [],
    animation: null,
    filterText: "",
    collapsed: [],
    hidden: [],
    shown: [],
};

/**
 * Reads a subject's state back, field by field.
 *
 * Same contract as `viewerSettingsFrom`: nothing throws, and one unreadable field costs only itself.
 * This one crosses a webview boundary rather than a storage one, which is the same trust problem.
 */
export function subjectStateFrom(stored: unknown): SubjectState {
    const raw = asRecord(stored);

    return {
        alt: index_(raw.alt, EMPTY_SUBJECT_STATE.alt),
        lod: index_(raw.lod, EMPTY_SUBJECT_STATE.lod),
        destroyed: strings(raw.destroyed),
        hiddenEmitters: indices(raw.hiddenEmitters),
        animation: typeof raw.animation === "string" ? raw.animation : null,
        filterText: typeof raw.filterText === "string" ? raw.filterText : "",
        collapsed: strings(raw.collapsed),
        hidden: strings(raw.hidden),
        shown: strings(raw.shown),
    };
}

/**
 * The detail level a subject opens at, from the levels its geometry defines (ascending).
 *
 * The level recorded for it, if the geometry still defines it - a mesh's `_LOD2` suffix can be
 * edited away between one open and the next, and a level that no longer exists shows nothing at
 * all. Otherwise the HIGHEST level: Alamo numbers detail the other way up, so the last level is the
 * close-up one and LOD 0 the distant one.
 */
export function openingLod(
    defined: readonly number[],
    recorded: number | null,
): number {
    if (recorded !== null && defined.includes(recorded)) {
        return recorded;
    }

    return defined.length > 0 ? defined[defined.length - 1] : 0;
}

/**
 * The detail level to show as a subject's geometry arrives, from the levels defined so far.
 *
 * The reader's pick stands. Until there is one the opening rule FOLLOWS the levels as they arrive
 * rather than choosing once: a unit's parts arrive one by one, and a hardpoint's model that tags no
 * level, arriving before the hull, defines only LOD 0 - choosing from that left the unit at its
 * lowest detail once the hull arrived. The levels only grow while a subject loads, so following
 * them ends on the opening rule's answer for the whole subject.
 */
export function lodOnArrival(
    defined: readonly number[],
    shown: number,
    picked: boolean,
    recorded: number | null,
): number {
    return picked ? shown : openingLod(defined, recorded);
}

/**
 * The detail level to record for a subject.
 *
 * The one on screen once the reader PICKED it. The opening rule's choice is not recorded: it is made
 * again on every open, and one made from part of the geometry - or the placeholder 0 shown before
 * any arrived - would be restored as if it were a pick. Until the reader picks, what was recorded
 * before stands, so a preview reopened and closed again keeps the level it was left at.
 */
export function lodToRecord(
    shown: number,
    picked: boolean,
    recorded: number | null,
): number | null {
    return picked ? shown : recorded;
}

function asRecord(value: unknown): Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value)
        ? (value as Record<string, unknown>)
        : {};
}

function index_<T extends number | null>(
    value: unknown,
    fallback: T,
): number | T {
    return typeof value === "number" && Number.isInteger(value) && value >= 0
        ? value
        : fallback;
}

/** An array of strings, or nothing. A mixed array is refused whole - half a list is not a list. */
function strings(value: unknown): string[] {
    return Array.isArray(value) &&
        value.every((item) => typeof item === "string")
        ? ([...value] as string[])
        : [];
}

function indices(value: unknown): number[] {
    return Array.isArray(value) &&
        value.every(
            (item) =>
                typeof item === "number" && Number.isInteger(item) && item >= 0,
        )
        ? ([...value] as number[])
        : [];
}
