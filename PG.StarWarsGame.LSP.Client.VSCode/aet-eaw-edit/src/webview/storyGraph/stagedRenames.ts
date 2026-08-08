// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a staged rename leaves an event's name.
//
// A gesture reads the node's `dto.label`, which lags a staged rename until the preview lands - so
// an edit made in that window would carry a name the batch no longer knows by the time its rename
// command runs. Resolving through the chain keeps every staged command pointed at the event's
// latest name, so the batch composes in order without "event not found".

/** How many links to follow before giving up. Guards a chain that loops back on itself. */
const MAX_CHAIN = 64;

/**
 * The chain of staged renames, old name to next name.
 *
 * Keys are lower-cased: the engine resolves event names case-insensitively, so a rename staged
 * against `Intro_Event` has to be found by a later gesture that read `INTRO_EVENT` off the graph.
 */
export class StagedRenames {
    private readonly _chain = new Map<string, string>();

    /** Records that `from` is now called `to`. */
    record(from: string, to: string): void {
        this._chain.set(from.toLowerCase(), to);
    }

    clear(): void {
        this._chain.clear();
    }

    get size(): number {
        return this._chain.size;
    }

    /**
     * Follows the chain to the event's latest name.
     *
     * A name never renamed comes back unchanged, which is the common case - every gesture runs
     * through here, not just the ones after a rename.
     */
    resolve(name: string): string {
        let current = name;

        for (let i = 0; i < MAX_CHAIN; i++) {
            const next = this._chain.get(current.toLowerCase());
            // A self-rename is a fixed point, not a link: following it would spin until the bound.
            if (next === undefined || next === current) { break; }
            current = next;
        }

        return current;
    }
}
