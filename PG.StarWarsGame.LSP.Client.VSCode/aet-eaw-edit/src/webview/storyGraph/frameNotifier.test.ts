// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { FrameNotifier } from './frameNotifier';

/** A hand-cranked frame scheduler, so a test can decide when the frame happens. */
function manualFrames(): { schedule: (fn: () => void) => void; run: () => void } {
    let queued: (() => void)[] = [];
    return {
        schedule: fn => { queued.push(fn); },
        run: () => {
            const due = queued;
            queued = [];
            for (const fn of due) { fn(); }
        },
    };
}

describe('FrameNotifier', () => {
    it('notifies subscribers when the frame arrives', () => {
        const frames = manualFrames();
        const notifier = new FrameNotifier(frames.schedule);
        let calls = 0;
        notifier.subscribe(() => { calls++; });

        notifier.schedule();
        assert.equal(calls, 0, 'must not fire before the frame');

        frames.run();
        assert.equal(calls, 1);
    });

    // The whole point: rete fires a change event per node per drag frame, and a minimap that
    // redrew on each would redraw dozens of times for one frame of movement.
    it('collapses repeated pokes within a frame into one notification', () => {
        const frames = manualFrames();
        const notifier = new FrameNotifier(frames.schedule);
        let calls = 0;
        notifier.subscribe(() => { calls++; });

        for (let i = 0; i < 50; i++) { notifier.schedule(); }
        frames.run();

        assert.equal(calls, 1);
    });

    it('is ready to fire again on the next frame', () => {
        const frames = manualFrames();
        const notifier = new FrameNotifier(frames.schedule);
        let calls = 0;
        notifier.subscribe(() => { calls++; });

        notifier.schedule();
        frames.run();
        notifier.schedule();
        frames.run();

        assert.equal(calls, 2);
    });

    // The flag is cleared before the callbacks run, so a subscriber that reacts by asking for
    // another notification gets the next frame rather than being swallowed.
    it('honours a notification requested from inside a notification', () => {
        const frames = manualFrames();
        const notifier = new FrameNotifier(frames.schedule);
        let calls = 0;
        notifier.subscribe(() => {
            calls++;
            if (calls === 1) { notifier.schedule(); }
        });

        notifier.schedule();
        frames.run();
        frames.run();

        assert.equal(calls, 2);
    });

    it('stops notifying once unsubscribed', () => {
        const frames = manualFrames();
        const notifier = new FrameNotifier(frames.schedule);
        let calls = 0;
        const off = notifier.subscribe(() => { calls++; });

        off();
        notifier.schedule();
        frames.run();

        assert.equal(calls, 0);
    });

    // A React effect cleanup can unsubscribe while the list is being walked.
    it('survives a subscriber unsubscribing itself mid-notification', () => {
        const frames = manualFrames();
        const notifier = new FrameNotifier(frames.schedule);
        const seen: string[] = [];

        const off = notifier.subscribe(() => { seen.push('first'); off(); });
        notifier.subscribe(() => { seen.push('second'); });

        notifier.schedule();
        frames.run();

        assert.deepEqual(seen, ['first', 'second']);
    });

    // ── the default scheduler ────────────────────────────────────────────────
    //
    // Every test above injects a scheduler, so the DEFAULT parameter - the only one production
    // uses - was never executed. It was broken for exactly that reason: it captured
    // `requestAnimationFrame` as a bare reference, which then got invoked as `this._schedule(...)`
    // with the notifier as receiver. Browsers reject that with "Illegal invocation", both
    // notifiers died on their first poke, and the story graph stopped rendering.

    /**
     * Stands in for the browser's requestAnimationFrame, including its receiver rule: WebIDL
     * operations on the global accept no receiver (or the global), and throw for anything else.
     * Node's own functions do not care about `this`, so without this the bug is invisible here.
     */
    function browserLikeRaf(): { install: () => () => void; run: () => void } {
        let queued: (() => void)[] = [];
        function raf(this: unknown, fn: () => void): void {
            if (this !== undefined && this !== globalThis) {
                throw new TypeError('Illegal invocation');
            }
            queued.push(fn);
        }
        return {
            install: () => {
                const had = 'requestAnimationFrame' in globalThis;
                const previous = (globalThis as Record<string, unknown>).requestAnimationFrame;
                (globalThis as Record<string, unknown>).requestAnimationFrame = raf;
                return () => {
                    if (had) { (globalThis as Record<string, unknown>).requestAnimationFrame = previous; }
                    else { delete (globalThis as Record<string, unknown>).requestAnimationFrame; }
                };
            },
            run: () => { const due = queued; queued = []; for (const fn of due) { fn(); } },
        };
    }

    it('defaults to a scheduler that does not detach requestAnimationFrame', () => {
        const frames = browserLikeRaf();
        const restore = frames.install();
        try {
            const notifier = new FrameNotifier();
            let calls = 0;
            notifier.subscribe(() => { calls++; });

            notifier.schedule();
            frames.run();

            assert.equal(calls, 1);
        } finally {
            restore();
        }
    });

    // The flag is raised before the scheduler is called, so a throwing scheduler used to leave it
    // raised forever - every later schedule() returned early and the notifier was silently dead.
    // That is what turned one exception into a permanently broken subsystem.
    it('is not wedged permanently by a scheduler that throws', () => {
        let failNext = true;
        const queued: (() => void)[] = [];
        const notifier = new FrameNotifier(fn => {
            if (failNext) { throw new Error('scheduler unavailable'); }
            queued.push(fn);
        });
        let calls = 0;
        notifier.subscribe(() => { calls++; });

        assert.throws(() => notifier.schedule(), /scheduler unavailable/);

        failNext = false;
        notifier.schedule();
        for (const fn of queued) { fn(); }

        assert.equal(calls, 1, 'a later schedule must still be able to fire');
    });

    it('keeps its lists separate from another notifier', () => {
        const frames = manualFrames();
        const area = new FrameNotifier(frames.schedule);
        const geometry = new FrameNotifier(frames.schedule);
        let areaCalls = 0;
        let geometryCalls = 0;
        area.subscribe(() => { areaCalls++; });
        geometry.subscribe(() => { geometryCalls++; });

        area.schedule();
        frames.run();

        assert.equal(areaCalls, 1);
        assert.equal(geometryCalls, 0);
    });
});
