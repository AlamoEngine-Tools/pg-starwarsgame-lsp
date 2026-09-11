// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    ACCEPT, DECLINE, declinedMessage, diffTitle, outcomeOf, proposalMessage,
} from './pgprojMigrationPrompt';

const proposal = {
    path: '/mods/mymod/mymod.pgproj',
    fileName: 'mymod.pgproj',
    proposedText: '{}',
    notices: [] as string[],
};

describe('the proposal message', () => {
    it('names the file', () => {
        assert.match(proposalMessage(proposal), /mymod\.pgproj/);
    });

    // The one cost the diff cannot show: comments are gone once the file is rewritten, and they do
    // not appear in either side of the comparison because the parser drops them.
    it('says what will be lost and that a backup is taken', () => {
        const message = proposalMessage(proposal);

        assert.match(message, /[Cc]omments/);
        assert.match(message, /backup/);
    });

    // Declining stops the server, which is a consequence worth knowing BEFORE choosing rather than
    // discovering afterwards.
    it('says what declining does', () => {
        assert.match(proposalMessage(proposal), /stops the language server/);
    });

    it('carries each migration notice', () => {
        const message = proposalMessage({
            ...proposal, notices: ['Move your credits file under data/text.'],
        });

        assert.match(message, /Move your credits file under data\/text\./);
    });

    it('reads cleanly with no notices', () => {
        assert.ok(!proposalMessage(proposal).includes('  '), 'no double spaces where a notice would go');
    });
});

describe('the answer', () => {
    it('accepts only the explicit yes', () => {
        assert.equal(outcomeOf(ACCEPT), 'accept');
    });

    // Dismissing is not "ask me later". Treating it as consent would rewrite somebody's project
    // file because they pressed Escape.
    it('treats a dismissal as a decline', () => {
        assert.equal(outcomeOf(undefined), 'decline');
        assert.equal(outcomeOf(DECLINE), 'decline');
        assert.equal(outcomeOf('something else entirely'), 'decline');
    });
});

describe('the diff title', () => {
    it('names the file and both sides', () => {
        assert.equal(diffTitle('mymod.pgproj'), 'mymod.pgproj - on disk <-> updated format');
    });
});

describe('the message after stopping', () => {
    // A server that vanished without saying why reads as a crash.
    it('says the file is untouched, that the server stopped, and how to get back', () => {
        const message = declinedMessage('mymod.pgproj');

        assert.match(message, /unchanged/);
        assert.match(message, /stopped/);
        assert.match(message, /reload the window/i);
    });
});
