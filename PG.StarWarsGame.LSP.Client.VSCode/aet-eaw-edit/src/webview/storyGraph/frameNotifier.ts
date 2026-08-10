// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A subscription list that fires at most once per animation frame.
//
// The graph had two of these written out separately - one for viewport changes, one for node
// geometry - identical down to the `scheduled` flag guarding the rAF. They are separate lists on
// purpose (the swimlane overlay must not recompute its bounds on every pan frame, since it rides
// inside rete's transformed content holder and pans for free), but that is an argument for two
// instances, not for two copies of the mechanism.

export type Unsubscribe = () => void;

/**
 * Notifies its subscribers once per frame, however many times it is poked.
 *
 * Coalescing is the point: rete fires a change event per node per drag frame, and a minimap that
 * redrew on each of them would redraw dozens of times for one frame of movement.
 */
export class FrameNotifier {
    private readonly _subscribers = new Set<() => void>();
    private _scheduled = false;

    /**
     * @param _schedule How to defer to the next frame. Injected so this is testable without a
     *     browser; the default calls the global.
     *
     * The default WRAPS `requestAnimationFrame` rather than referencing it. Passing the bare
     * function means it is later invoked as `this._schedule(...)`, i.e. with the notifier as
     * receiver - and the browser rejects that with "Illegal invocation", because a WebIDL
     * operation on the global accepts no receiver but its own. That threw on the very first poke,
     * and since the flag below is raised before the call, both notifiers then went silently dead
     * and the story graph stopped rendering.
     */
    constructor(private readonly _schedule: (fn: () => void) => void = fn => requestAnimationFrame(fn)) {}

    subscribe(callback: () => void): Unsubscribe {
        this._subscribers.add(callback);
        return () => { this._subscribers.delete(callback); };
    }

    /** Asks for a notification. Repeated calls before the frame fires collapse into one. */
    schedule(): void {
        if (this._scheduled) { return; }
        this._scheduled = true;

        try {
            this._doSchedule();
        } catch (error) {
            // Lower the flag before rethrowing. Leaving it raised is what turned a single failed
            // schedule into a permanently dead notifier: every later call took the early return
            // above, so the failure was loud once and silent forever after.
            this._scheduled = false;
            throw error;
        }
    }

    private _doSchedule(): void {
        this._schedule(() => {
            // Cleared before the callbacks run, so a subscriber that pokes this again gets the
            // next frame rather than being swallowed by the flag it is still inside.
            this._scheduled = false;
            // Copied, so a subscriber that unsubscribes itself while being notified does not
            // mutate the set being iterated.
            for (const callback of [...this._subscribers]) { callback(); }
        });
    }
}
