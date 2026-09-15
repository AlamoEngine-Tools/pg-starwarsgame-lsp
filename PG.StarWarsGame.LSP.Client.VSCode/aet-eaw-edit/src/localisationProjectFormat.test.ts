// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { formatChoices, formatOutcome } from './localisationProjectFormat';

describe('formatChoices', () => {
    /**
     * All four formats a .pgproj can declare - DAT included. #121's reporter needed exactly CSV to DAT,
     * which the conversion command deliberately cannot offer.
     */
    it('offers every format a project can declare, DAT included', () => {
        assert.deepEqual(formatChoices(null).map(c => c.format), ['DAT', 'CSV', 'XML', 'NLS']);
    });

    // The declared format arrives however the .pgproj spelled it.
    it('marks the format the project already loads, whatever its casing', () => {
        const choices = formatChoices('Csv');

        assert.deepEqual(choices.filter(c => c.current).map(c => c.format), ['CSV']);
        assert.match(choices.find(c => c.format === 'CSV')!.description, /^Current/);
    });

    it('marks nothing when the project declares no format', () => {
        assert.equal(formatChoices(null).some(c => c.current), false);
    });

    it('keeps labels ASCII with no trailing full stop', () => {
        for (const choice of formatChoices('DAT')) {
            assert.match(`${choice.label}${choice.description}`, /^[\x20-\x7e]*$/);
            assert.doesNotMatch(choice.description, /\.$/);
        }
    });
});

describe('formatOutcome', () => {
    it('reports a refusal as an error, in the server\'s words', () => {
        const outcome = formatOutcome({ changed: false, filesInFormat: 0, error: 'No mod project is open.' });

        assert.equal(outcome.kind, 'error');
        assert.match(outcome.text, /No mod project is open/);
    });

    it('says when nothing changed', () => {
        const outcome = formatOutcome({ changed: false, format: 'CSV', previousFormat: 'CSV', filesInFormat: 1 });

        assert.equal(outcome.kind, 'info');
        assert.match(outcome.text, /already loads CSV/);
    });

    it('names both formats and the file count on a change', () => {
        const outcome = formatOutcome({ changed: true, format: 'DAT', previousFormat: 'CSV', filesInFormat: 2 });

        assert.equal(outcome.kind, 'info');
        assert.match(outcome.text, /CSV/);
        assert.match(outcome.text, /DAT/);
        assert.match(outcome.text, /2 files/);
    });

    /** A switch to a format with no files on disk leaves the project loading nothing - that is a warning. */
    it('warns when the new format has no files to load', () => {
        const outcome = formatOutcome({ changed: true, format: 'XML', previousFormat: 'CSV', filesInFormat: 0 });

        assert.equal(outcome.kind, 'warning');
        assert.match(outcome.text, /no XML files/);
    });

    it('counts one file in the singular', () => {
        assert.match(formatOutcome({ changed: true, format: 'CSV', previousFormat: 'DAT', filesInFormat: 1 }).text, /1 file\b/);
    });
});
