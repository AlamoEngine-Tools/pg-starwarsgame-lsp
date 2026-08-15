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
    /** Detail level, same. */
    lod: number;
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
    lod: 0,
    destroyed: [],
    hiddenEmitters: [],
    animation: null,
    filterText: '',
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
        animation: typeof raw.animation === 'string' ? raw.animation : null,
        filterText: typeof raw.filterText === 'string' ? raw.filterText : '',
        collapsed: strings(raw.collapsed),
        hidden: strings(raw.hidden),
        shown: strings(raw.shown),
    };
}

function asRecord(value: unknown): Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
        ? value as Record<string, unknown>
        : {};
}

function index_(value: unknown, fallback: number): number {
    return typeof value === 'number' && Number.isInteger(value) && value >= 0 ? value : fallback;
}

/** An array of strings, or nothing. A mixed array is refused whole - half a list is not a list. */
function strings(value: unknown): string[] {
    return Array.isArray(value) && value.every(item => typeof item === 'string')
        ? [...value] as string[]
        : [];
}

function indices(value: unknown): number[] {
    return Array.isArray(value)
        && value.every(item => typeof item === 'number' && Number.isInteger(item) && item >= 0)
        ? [...value] as number[]
        : [];
}
