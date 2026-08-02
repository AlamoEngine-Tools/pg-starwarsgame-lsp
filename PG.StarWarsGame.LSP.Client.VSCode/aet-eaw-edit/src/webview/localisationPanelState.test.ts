// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { LocalisationPanelState } from './localisationPanelState';

function command(index: number): Record<string, unknown> {
    return { kind: 'setCell', index, language: 'ENGLISH', value: `v${index}` };
}

describe('LocalisationPanelState', () => {
    it('starts clean', () => {
        const state = new LocalisationPanelState();

        assert.equal(state.pendingCount, 0);
        assert.equal(state.isDirty, false);
        assert.equal(state.shouldPromptOnClose(), false);
    });

    it('tracks the queue the webview mirrors to it', () => {
        const state = new LocalisationPanelState();

        state.syncPending([command(0), command(1)]);

        assert.equal(state.pendingCount, 2);
        assert.equal(state.isDirty, true);
        assert.equal(state.shouldPromptOnClose(), true);
    });

    // The mirror is a replacement, not an append: the webview owns the queue and re-sends it whole,
    // so treating it as additive would double-count every edit.
    it('replaces the queue rather than appending to it', () => {
        const state = new LocalisationPanelState();

        state.syncPending([command(0), command(1)]);
        state.syncPending([command(0)]);

        assert.equal(state.pendingCount, 1);
    });

    // ── the content hash ─────────────────────────────────────────────────────

    it('carries the hash from the last read into the next save', () => {
        const state = new LocalisationPanelState();

        state.noteRead('hash-1');

        assert.equal(state.contentHash, 'hash-1');
    });

    // Without this the second save of a session always fails the guard: the file has moved on, but
    // the panel would still be quoting the hash it first loaded.
    it('adopts the hash a successful save returns', () => {
        const state = new LocalisationPanelState();
        state.noteRead('hash-1');
        state.syncPending([command(0)]);

        state.noteSaved('hash-2');

        assert.equal(state.contentHash, 'hash-2');
    });

    it('clears the queue on a successful save', () => {
        const state = new LocalisationPanelState();
        state.syncPending([command(0), command(1)]);

        state.noteSaved('hash-2');

        assert.equal(state.pendingCount, 0);
        assert.equal(state.isDirty, false);
    });

    // A failed save must not look like a clean one, or the user loses their staged work believing
    // it was written.
    it('keeps the queue and the hash when a save fails', () => {
        const state = new LocalisationPanelState();
        state.noteRead('hash-1');
        state.syncPending([command(0), command(1)]);

        state.noteSaveFailed();

        assert.equal(state.pendingCount, 2);
        assert.equal(state.contentHash, 'hash-1');
        assert.equal(state.shouldPromptOnClose(), true);
    });

    // A save returning no hash cannot be trusted to be current, so the old one is kept rather than
    // being replaced with undefined - which would fail the guard as "no hash provided".
    it('keeps the previous hash when a save returns none', () => {
        const state = new LocalisationPanelState();
        state.noteRead('hash-1');

        state.noteSaved(undefined);

        assert.equal(state.contentHash, 'hash-1');
    });

    // ── reload ───────────────────────────────────────────────────────────────

    // Re-reading after a stale-hash refusal has to discard the staged edits with the hash they were
    // built against; keeping them would re-apply indices that no longer mean the same rows.
    it('drops the queue on a reload', () => {
        const state = new LocalisationPanelState();
        state.noteRead('hash-1');
        state.syncPending([command(0)]);

        state.noteRead('hash-2');

        assert.equal(state.pendingCount, 0);
        assert.equal(state.contentHash, 'hash-2');
    });

    // ── re-delivery ──────────────────────────────────────────────────────────
    //
    // A save triggers a localisation reload on the server, and the OS file watcher then reports the
    // very same write - so one save produced two "the index moved" notifications, each making every
    // open tab re-read the file and hand the webview 19,000 rows again. Re-rendering that also
    // resets the tab (selection, sort, inherited toggle) and re-fetches the baseline. Nothing about
    // the file changed, so none of it should happen.

    it('does not re-deliver a file whose contents have not changed', () => {
        const state = new LocalisationPanelState();
        state.noteRead('hash-1');

        assert.equal(state.shouldDeliver('hash-1'), false);
    });

    it('delivers when the contents did change', () => {
        const state = new LocalisationPanelState();
        state.noteRead('hash-1');

        assert.equal(state.shouldDeliver('hash-2'), true);
    });

    it('delivers the first read', () => {
        assert.equal(new LocalisationPanelState().shouldDeliver('hash-1'), true);
    });

    // Without a hash there is nothing to compare, so the read has to go through - suppressing it
    // would leave a tab showing nothing at all.
    it('delivers when there is no hash to compare', () => {
        const state = new LocalisationPanelState();
        state.noteRead('hash-1');

        assert.equal(state.shouldDeliver(undefined), true);
    });

    // A save adopts the hash it wrote. The watcher echo that follows carries that same hash, and
    // must not be mistaken for someone else editing the file.
    it('does not re-deliver the hash a save just adopted', () => {
        const state = new LocalisationPanelState();
        state.noteRead('hash-1');
        state.noteSaved('hash-2');

        assert.equal(state.shouldDeliver('hash-2'), false);
    });

    it('exposes the queue for a save', () => {
        const state = new LocalisationPanelState();
        const queue = [command(0), command(1)];

        state.syncPending(queue);

        assert.deepEqual(state.pending, queue);
    });
});
