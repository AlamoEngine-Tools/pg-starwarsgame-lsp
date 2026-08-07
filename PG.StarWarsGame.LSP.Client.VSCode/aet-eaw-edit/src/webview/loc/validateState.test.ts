// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { severityIconFor, validateTitle, worstSeverity } from './validateState';

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

    // Warnings are not errors: keys differing only in case are two real entries the game reads,
    // worth flagging without claiming the file is broken.
    it('shows a warning glyph when the worst reported level is a warning', () => {
        assert.equal(severityIconFor('warning'), 'warning');
    });

    it('shows an info glyph when nothing worse than information was reported', () => {
        assert.equal(severityIconFor('info'), 'info');
    });
});

describe('worstSeverity', () => {
    // The tag reads out the highest level reported, the same rule the story graph editor's does:
    // unvalidated beats error beats warning beats clean.
    it('is clean when nothing was reported', () => {
        assert.equal(worstSeverity([]), 'ok');
    });

    it('is a warning when only warnings were reported', () => {
        assert.equal(worstSeverity([{ severity: 'warning' }, { severity: 'warning' }]), 'warning');
    });

    // Information is not a problem: a credits heading with no entries under it renders exactly as
    // written, so the tag must not colour the file as though something were wrong with it.
    it('is info when only information was reported', () => {
        assert.equal(worstSeverity([{ severity: 'info' }, { severity: 'info' }]), 'info');
    });

    it('lets a warning outrank information', () => {
        assert.equal(worstSeverity([{ severity: 'info' }, { severity: 'warning' }]), 'warning');
    });

    it('lets an error outrank information', () => {
        assert.equal(worstSeverity([{ severity: 'info' }, { severity: 'error' }]), 'error');
    });

    it('is an error when anything was an error', () => {
        assert.equal(worstSeverity([{ severity: 'warning' }, { severity: 'error' }]), 'error');
    });

    // Order must not decide it - the worst wins wherever it appears in the list.
    it('is an error when the error comes first', () => {
        assert.equal(worstSeverity([{ severity: 'error' }, { severity: 'warning' }]), 'error');
    });

    // An unrecognised severity must not silently read as clean, and must not be softened to info
    // either - a level this does not know about could be anything.
    it('treats an unknown severity as a warning rather than ignoring it', () => {
        assert.equal(worstSeverity([{ severity: 'something-new' }]), 'warning');
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

    it('names warnings as warnings rather than calling them problems', () => {
        assert.match(validateTitle('warning', 2, 0), /2 warnings/i);
    });

    it('names information as notes, never as problems', () => {
        const title = validateTitle('info', 2, 0);

        assert.match(title, /2 notes/i);
        assert.doesNotMatch(title, /problem/i);
    });

    it('never claims a verdict it does not have', () => {
        assert.doesNotMatch(validateTitle('unvalidated', 0, 0), /no problems/i);
    });
});
