// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { problemLook, problemTag, problemWhere } from './problemLook';

describe('problemLook', () => {
    it('keeps the three severities the server sends apart', () => {
        // The defect this exists to prevent. The panel drew
        // `severity === 'error' ? 'error' : 'warning'`, so every `info` arrived wearing the warning
        // triangle - and the reader could not tell whether an unbound ability effect was something
        // they had to fix. Two of the server's twenty preview problems are `info`.
        assert.equal(problemLook('error').icon, 'error');
        assert.equal(problemLook('warning').icon, 'warning');
        assert.equal(problemLook('info').icon, 'info');
    });

    it('gives each severity its own tone', () => {
        const tones = ['error', 'warning', 'info'].map(s => problemLook(s).tone);

        assert.deepEqual(tones, ['error', 'warning', 'info']);
        assert.equal(new Set(tones).size, 3);
    });

    it('reads a severity however it is cased or padded', () => {
        assert.equal(problemLook(' Info ').icon, 'info');
        assert.equal(problemLook('ERROR').icon, 'error');
    });

    it('shows an unknown severity as a warning rather than quietly playing it down', () => {
        // `severity` crosses the wire as a plain string, so a severity added on the server and not
        // here is a question of which way to be wrong. Loud: an unrecognised finding shown as info
        // is one a reader skips.
        for (const severity of ['', 'critical', 'hint', 'nonsense']) {
            assert.equal(problemLook(severity).tone, 'warning', severity);
        }
    });
});

describe('problemWhere', () => {
    it('says nothing when the sentence already names the hardpoint', () => {
        // Every shipped hardpoint problem writes the id into its own message, so a badge beside it
        // reads "HP_Tartan_Cruiser_00  Hardpoint 'HP_Tartan_Cruiser_00' names no Attachment_Bone"
        // - the id twice before the reader reaches a single word of the finding.
        assert.equal(
            problemWhere('HP_Tartan_Cruiser_00',
                "Hardpoint 'HP_Tartan_Cruiser_00' names no Attachment_Bone, so it sits at the "
                + "hull's origin."),
            null);
    });

    it('names the hardpoint when the sentence does not', () => {
        assert.equal(problemWhere('HP_Lft_MDC', 'The reticle catalog has no entry for this type.'),
            'HP_Lft_MDC');
    });

    it('says nothing for a problem that belongs to no hardpoint', () => {
        // Which is most of them - the unbound ability effects among them.
        assert.equal(problemWhere(null, 'anything'), null);
        assert.equal(problemWhere(undefined, 'anything'), null);
        assert.equal(problemWhere('   ', 'anything'), null);
    });

    it('matches the id however either side is cased', () => {
        assert.equal(problemWhere('hp_lft_mdc', "Hardpoint 'HP_Lft_MDC' is adrift."), null);
    });
});

describe('problemTag', () => {
    it('shows the id a reader can act on', () => {
        const tag = problemTag('aetswg-014-0007');

        assert.equal(tag.text, 'aetswg-014-0007');
        assert.match(tag.title, /suppress|search|diagnostic/i);
    });

    // The client-side findings are a different kind of thing: they describe what the viewport could
    // resolve at one instant, have no file and no position, and are legitimately transient. They
    // cannot be diagnostics, so the row says what they are instead of leaving a blank where the id
    // goes and letting the reader wonder which ones are missing theirs.
    it('marks a finding with no id as render-time', () => {
        const tag = problemTag(undefined);

        assert.equal(tag.text, 'render-time');
        assert.match(tag.title, /viewport|render/i);
    });

    it('treats a null id the same as an absent one', () => {
        assert.equal(problemTag(null).text, 'render-time');
    });

    // A server that predates the ids sends the field as an empty string rather than omitting it.
    it('treats an empty id as absent rather than showing a blank chip', () => {
        assert.equal(problemTag('   ').text, 'render-time');
    });
});
