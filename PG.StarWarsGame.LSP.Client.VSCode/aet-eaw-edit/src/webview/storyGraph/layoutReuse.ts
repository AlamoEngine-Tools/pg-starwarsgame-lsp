// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Whether a saved graph layout is still worth using.

/**
 * True when the stored layout covers at least one of the graph's events.
 *
 * Deliberately "some", not "every". The all-or-nothing version discarded the entire saved
 * arrangement the moment a single event lacked an entry - which is the normal state of a campaign
 * being edited, since every newly added event is unplaced until the layout is next written. The
 * graph then fell through to the first-open path: mount everything, re-run elk, and move every
 * node the user had positioned by hand. On a big campaign that also meant no windowing, so the
 * stutter came back too.
 *
 * Partial reuse is strictly better. Events with a stored position keep it; the rest are placed
 * beside their neighbours by the same pass that has always placed junctions. A user who does want
 * the whole thing re-flowed has Rearrange for that - it should be their choice, not a side effect
 * of adding an event.
 *
 * Only a layout with nothing recognisable in it is refused, which is a genuine first open.
 */
export function canReuseStoredLayout(eventKeys: readonly string[], stored: ReadonlySet<string>): boolean {
    return eventKeys.some(key => stored.has(key));
}

/** The shape of a node and of a stored entry this module keys by; both the DTOs satisfy it. */
export interface LayoutNodeLike {
    kind?: string;
    id?: string;
    threadUri?: string | null;
    label?: string;
}

export interface LayoutEntryLike {
    threadUri: string;
    eventName: string;
    nodeId?: string | null;
}

/**
 * How a node is looked up in a saved layout.
 *
 * An event is named by its thread and event name, folded, since that is what survives a rename of
 * nothing but casing and what the sidecar keys. A junction, portal, tactical stub or script state
 * has neither, so its node id is the name - the server keys it by the same id with the URIs inside
 * made relative. The two spaces cannot collide: an event key holds a space, a node id never does.
 */
export function nodeLayoutKey(node: LayoutNodeLike): string {
    return node.kind === 'Event'
        ? `${node.threadUri ?? ''} ${node.label ?? ''}`.toLowerCase()
        : node.id ?? '';
}

/** The same key for a stored entry, so the two sides meet in one map. */
export function layoutEntryKey(entry: LayoutEntryLike): string {
    return entry.nodeId ? entry.nodeId : `${entry.threadUri} ${entry.eventName}`.toLowerCase();
}
