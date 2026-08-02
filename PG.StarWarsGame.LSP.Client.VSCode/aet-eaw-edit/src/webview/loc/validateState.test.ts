// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { severityIconFor, validateTitle } from './validateState';

describe('severityIconFor', () => {
    it('shows a tick when the file checked out clean', () => {
        assert.equal(severityIconFor('ok'), 'check');
    });

    it('shows an error glyph when something is wrong', () => {
        assert.equal(severityIconFor('error'), 'error');
    });

    /**
     * Only reachable after staging an edit: the file is checked as soon as it opens, so an
     * untouched file is never in this state.
     */
    it('shows a question mark when the result no longer describes what is staged', () => {
        assert.equal(severityIconFor('unvalidated'), 'question');
    });
});

describe('validateTitle', () => {
    it('says the file is clean when it is', () => {
        assert.match(validateTitle('ok', 0, 0), /no problems/i);
    });

    it('counts the problems it found', () => {
        assert.match(validateTitle('error', 3, 0), /3 problems/);
    });

    it('uses the singular for one problem', () => {
        assert.match(validateTitle('error', 1, 0), /1 problem\b/);
    });

    /**
     * With edits staged the last result is about a document that no longer exists, so the tooltip
     * has to say the button will re-check rather than report a stale verdict.
     */
    it('offers to re-check when edits are staged', () => {
        assert.match(validateTitle('unvalidated', 0, 2), /check/i);
    });

    it('never claims a verdict it does not have', () => {
        assert.doesNotMatch(validateTitle('unvalidated', 0, 0), /no problems/i);
    });
});
