// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/** One thing the schema says about an event or reward type, as it arrives on the wire. */
export interface StoryNote {
    kind: string;
    text?: string | null;
    value?: string | null;
}

/** How a node shows what its type's notes say. */
export interface NoteBadge {
    /** Extra class for the node, or empty when the note changes nothing about how it looks. */
    className: string;
    /** The tooltip, always worth showing even when the node looks no different. */
    title: string;
}

/**
 * The worst thing the schema says about a type.
 *
 * Simply the first note: the server ranks them before sending, worst first, and the client does not
 * get to disagree with the schema about what matters most.
 */
export function worstNote(notes: readonly StoryNote[] | undefined): StoryNote | null {
    return notes?.[0] ?? null;
}

/** How each kind changes a node. A kind not listed here shows its words and nothing more. */
const STYLING: Record<string, { className: string; lead: string }> = {
    BuggedInEngine: {className: 'note-bugged', lead: 'Does not work'},
    Deprecated: {className: 'note-deprecated', lead: 'Deprecated'},
    // The dashed border predates the note model and readers already know it.
    Untested: {className: 'untested', lead: 'Untested'},
};

/**
 * What a node shows for its type's notes, or null when there is nothing worth showing.
 *
 * A `Since` note says when a type appeared, which on a node is noise, so it is dropped. Everything
 * else gets at least a tooltip - including a kind this build has never heard of, because the schema
 * may be newer than the extension and the reader is better off seeing the words than nothing.
 */
export function noteBadge(notes: readonly StoryNote[] | undefined): NoteBadge | null {
    const note = worstNote(notes);
    if (!note || note.kind === 'Since') {
        return null;
    }

    const styling = STYLING[note.kind];
    const lead = styling?.lead ?? note.kind;
    const text = note.text?.trim();

    return {
        className: styling?.className ?? '',
        title: text ? `${lead}: ${text}` : lead,
    };
}
