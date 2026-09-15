// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { filterProblems, resolveProblemJump } from './problemFilter';

interface Row { id: string | null; message: string }

const row = (id: string | null, message = 'anything'): Row => ({ id, message });

/** The story graph's shape of predicate: a row belongs to the view when its node is on screen. */
const inGraph = (...ids: string[]) =>
    (r: Row): boolean => r.id === null || ids.includes(r.id);

describe('filterProblems without a predicate', () => {
    // The whole point of the hook being optional: an editor that has not implemented a view filter
    // must behave exactly as it did before this existed.
    it('shows everything and reports nothing held back', () => {
        const view = filterProblems([row('a'), row('b')], undefined, false);

        assert.equal(view.shown.length, 2);
        assert.equal(view.hidden, 0);
        assert.equal(view.filterable, false);
    });

    it('is unaffected by the show-all flag', () => {
        assert.equal(filterProblems([row('a')], undefined, true).shown.length, 1);
    });

    it('labels itself with a plain count', () => {
        assert.equal(filterProblems([row('a'), row('b')], undefined, false).label, '2');
    });
});

describe('filterProblems with a predicate', () => {
    it('keeps what the view is showing', () => {
        const view = filterProblems([row('a')], inGraph('a'), false);

        assert.equal(view.shown.length, 1);
        assert.equal(view.hidden, 0);
    });

    it('holds back what the view is not', () => {
        const view = filterProblems([row('a'), row('hidden')], inGraph('a'), false);

        assert.deepEqual(view.shown.map(r => r.id), ['a']);
        assert.equal(view.hidden, 1);
        assert.equal(view.total, 2);
    });

    // The predicate decides. A story-graph finding about a FILE rather than a node has no node to
    // be filtered out by, and its predicate says so - this only has to not get in the way.
    it('leaves the predicate to decide what has no view of its own', () => {
        const view = filterProblems([row(null)], inGraph('a'), false);

        assert.equal(view.shown.length, 1);
    });

    it('shows everything when asked to, and still counts what the filter would hold', () => {
        const view = filterProblems([row('a'), row('hidden')], inGraph('a'), true);

        assert.equal(view.shown.length, 2);
        assert.equal(view.hidden, 1);
    });

    it('preserves the order it was given', () => {
        const view = filterProblems(
            [row('a', 'first'), row('b', 'second')], inGraph('a', 'b'), false);

        assert.deepEqual(view.shown.map(r => r.message), ['first', 'second']);
    });

    // `filterable` is what the panel disables its control on. A filter that is holding nothing back
    // has nothing to toggle, and the control says so rather than disappearing.
    it('is filterable only while it is actually holding something back', () => {
        assert.equal(filterProblems([row('a')], inGraph('a'), false).filterable, false);
        assert.equal(filterProblems([row('x')], inGraph('a'), false).filterable, true);
    });

    it('stays filterable while showing all, so the toggle can be turned back', () => {
        assert.equal(filterProblems([row('x')], inGraph('a'), true).filterable, true);
    });
});

describe('filterProblems label', () => {
    // "1 of 2" rather than "1": the second number is the whole point, and a bare count reads as
    // the total - which is how someone concludes a graph is clean while a filter hides an error.
    it('says how many of how many when some are held back', () => {
        assert.equal(filterProblems([row('a'), row('x')], inGraph('a'), false).label, '1 of 2');
    });

    it('still says how many of how many while showing all', () => {
        assert.equal(filterProblems([row('a'), row('x')], inGraph('a'), true).label, '2 of 2');
    });

    it('says a plain count when the filter holds nothing back', () => {
        assert.equal(filterProblems([row('a')], inGraph('a'), false).label, '1');
    });

    it('says zero rather than nothing at all', () => {
        assert.equal(filterProblems([], inGraph('a'), false).label, '0');
    });
});

describe('resolveProblemJump', () => {
    // The bug this exists for (#129): with a branch selected, a problem outside it was reachable
    // through show-all, and clicking it hit a centre-on-node call that returned immediately
    // because the node was never in the filtered graph. Silently doing nothing is the one
    // outcome a click must never have.
    it('asks for the filter to be cleared when the node is not in view', () => {
        assert.equal(resolveProblemJump('evt-9', new Set(['evt-1'])), 'unfilter');
    });

    it('centres directly when the node is already in view', () => {
        assert.equal(resolveProblemJump('evt-1', new Set(['evt-1', 'evt-2'])), 'centre');
    });

    // Before the first graph arrives there is nothing to compare against, and clearing the filter
    // on a click the view cannot yet judge would throw away the reader's filter for no reason.
    it('centres when no graph has arrived yet', () => {
        assert.equal(resolveProblemJump('evt-1', null), 'centre');
    });

    it('treats an empty graph as not in view', () => {
        assert.equal(resolveProblemJump('evt-1', new Set()), 'unfilter');
    });
});
