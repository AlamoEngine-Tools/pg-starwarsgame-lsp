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
