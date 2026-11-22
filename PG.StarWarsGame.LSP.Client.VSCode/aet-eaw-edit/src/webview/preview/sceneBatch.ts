// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Several steps as ONE change to the scene.
//
// Two pieces of work answer for the whole scene and cost by its size, not by the size of the change:
// the row chain, which decides what every row draws, and the depth range, which measures every
// drawn vertex. Each step that changes the scene asks for them, which is right for one step and was
// the whole cost of many. MEASURED on a mod's capital ship with every hardpoint destroyed at one
// press: 145 explosions put in the scene one by one and one rules write per effect after them, and
// 4.0 of the 5.9 seconds the press froze the preview for were the chain and the measuring run again
// for each.
//
// A layer holds its own work while a batch is open and does it once when the outermost one ends -
// see `ModelRows.batch` and `CameraRig.batch`. This is the one place that says in which ORDER.

/** A layer that can hold its whole-scene work over several steps and do it once after them. */
export interface Batching {
    /** Runs `work` with the layer's work held, then does it once if any step asked. */
    batch(work: () => void): void;
}

/**
 * Runs `work` as one change: the rows are refreshed and the depth range refitted once after it,
 * however many of its steps asked for either.
 *
 * The rows first. The depth range is measured by what is DRAWN, and the rows are what decide that:
 * fitted before them, it would be fitted to the scene as it stood before the change.
 */
export function asOneChange(
    rows: Batching,
    depthRange: Batching,
    work: () => void,
): void {
    depthRange.batch(() => rows.batch(work));
}
