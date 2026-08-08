// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { StagedRenames } from './stagedRenames';

describe('StagedRenames', () => {
    it('leaves a name that was never renamed alone', () => {
        const renames = new StagedRenames();

        assert.equal(renames.resolve('INTRO_EVENT'), 'INTRO_EVENT');
    });

    it('follows a single rename', () => {
        const renames = new StagedRenames();
        renames.record('INTRO_EVENT', 'OPENING_EVENT');

        assert.equal(renames.resolve('INTRO_EVENT'), 'OPENING_EVENT');
    });

    // The case this exists for: a gesture reads dto.label off the graph, which lags the staged
    // rename until the preview lands. Without the chain the command carries a name the batch no
    // longer knows by the time its rename runs, and the save fails with "event not found".
    it('follows a chain of renames to the latest name', () => {
        const renames = new StagedRenames();
        renames.record('A', 'B');
        renames.record('B', 'C');
        renames.record('C', 'D');

        assert.equal(renames.resolve('A'), 'D');
        assert.equal(renames.resolve('B'), 'D');
    });

    // The engine resolves event names case-insensitively, so a rename staged against one casing
    // has to be found by a later gesture that read another off the graph.
    it('matches regardless of the casing the name was read in', () => {
        const renames = new StagedRenames();
        renames.record('Intro_Event', 'OPENING_EVENT');

        assert.equal(renames.resolve('INTRO_EVENT'), 'OPENING_EVENT');
        assert.equal(renames.resolve('intro_event'), 'OPENING_EVENT');
    });

    it('preserves the casing the new name was given', () => {
        const renames = new StagedRenames();
        renames.record('A', 'Opening_Event');

        assert.equal(renames.resolve('a'), 'Opening_Event');
    });

    it('treats a rename to the same name as the end of the chain', () => {
        const renames = new StagedRenames();
        renames.record('A', 'A');

        assert.equal(renames.resolve('A'), 'A');
    });

    // A cycle should not be reachable, but the resolver runs on every staged gesture and a spin
    // here would hang the webview rather than fail visibly.
    it('terminates on a cycle instead of spinning', () => {
        const renames = new StagedRenames();
        renames.record('A', 'B');
        renames.record('B', 'A');

        // The value is not meaningful - not hanging is the assertion.
        assert.ok(['A', 'B'].includes(renames.resolve('A')));
    });

    it('forgets everything when the queue is discarded', () => {
        const renames = new StagedRenames();
        renames.record('A', 'B');
        renames.clear();

        assert.equal(renames.size, 0);
        assert.equal(renames.resolve('A'), 'A');
    });
});
