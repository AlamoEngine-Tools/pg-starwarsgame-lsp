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
