// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { LspGateway, LspMessageSink, LspRequestSender, SERVER_NOT_RUNNING } from './lspGateway';

function sink(): LspMessageSink & { warnings: string[]; errors: string[] } {
    const warnings: string[] = [];
    const errors: string[] = [];
    return {
        warnings, errors,
        warn: (m: string) => { warnings.push(m); },
        error: (m: string) => { errors.push(m); },
    };
}

function answering(value: unknown): LspRequestSender {
    return { sendRequest: <T>() => Promise.resolve(value as T) };
}

function rejecting(reason: string): LspRequestSender {
    return { sendRequest: <T>() => Promise.reject(new Error(reason)) as Promise<T> };
}

describe('LspGateway.request', () => {
    it('reports offline without sending when there is no client', async () => {
        const gateway = new LspGateway(() => undefined);

        const outcome = await gateway.request('aet/anything');

        assert.equal(outcome.ok, false);
        assert.equal(outcome.ok === false && outcome.reason, 'offline');
    });

    // The hole this closes: four requests in the localisation panel had no try/catch, and they run
    // inside a webview message handler whose promise nobody awaits. A rejection there became an
    // unhandled rejection - the editor stopped saving and said nothing.
    it('turns a rejected request into an outcome rather than throwing', async () => {
        const gateway = new LspGateway(() => rejecting('socket closed'));

        const outcome = await gateway.request('aet/anything');

        assert.equal(outcome.ok, false);
        assert.equal(outcome.ok === false && outcome.reason, 'failed');
        assert.match(outcome.ok === false ? outcome.message : '', /socket closed/);
    });

    it('carries the payload through on success', async () => {
        const gateway = new LspGateway(() => answering({ languages: ['ENGLISH'] }));

        const outcome = await gateway.request<{ languages: string[] }>('aet/getLanguages');

        assert.equal(outcome.ok, true);
        assert.deepEqual(outcome.ok === true ? outcome.value : null, { languages: ['ENGLISH'] });
    });

    // The client is replaced on every server restart. A gateway that captured it once would keep
    // talking to the dead process.
    it('resolves the client per call rather than holding it', async () => {
        let client: LspRequestSender | undefined;
        const gateway = new LspGateway(() => client);

        assert.equal((await gateway.request('aet/x')).ok, false);
        client = answering(42);
        assert.equal((await gateway.request('aet/x')).ok, true);
    });
});

describe('LspGateway.requireRunning', () => {
    it('warns once and refuses when the server is down', () => {
        const messages = sink();
        const gateway = new LspGateway(() => undefined, messages);

        assert.equal(gateway.requireRunning(), false);
        assert.deepEqual(messages.warnings, [SERVER_NOT_RUNNING]);
    });

    it('says nothing and allows when the server is up', () => {
        const messages = sink();
        const gateway = new LspGateway(() => answering(null), messages);

        assert.equal(gateway.requireRunning(), true);
        assert.deepEqual(messages.warnings, []);
    });
});

describe('LspGateway.requestOrReport', () => {
    it('warns rather than errors when the server is simply not running', async () => {
        const messages = sink();
        const gateway = new LspGateway(() => undefined, messages);

        assert.equal(await gateway.requestOrReport('aet/x', {}, 'load the graph'), undefined);
        assert.deepEqual(messages.warnings, [SERVER_NOT_RUNNING]);
        assert.deepEqual(messages.errors, []);
    });

    // A user told only "something failed" cannot tell which of two actions it was.
    it('names the action in a failure message', async () => {
        const messages = sink();
        const gateway = new LspGateway(() => rejecting('boom'), messages);

        assert.equal(await gateway.requestOrReport('aet/x', {}, 'save the batch'), undefined);
        assert.equal(messages.warnings.length, 0);
        assert.match(messages.errors[0], /save the batch/);
        assert.match(messages.errors[0], /boom/);
    });

    it('reports nothing on success', async () => {
        const messages = sink();
        const gateway = new LspGateway(() => answering({ ok: 1 }), messages);

        assert.deepEqual(await gateway.requestOrReport('aet/x'), { ok: 1 });
        assert.deepEqual(messages.errors, []);
        assert.deepEqual(messages.warnings, []);
    });
});

describe('LspGateway.requestOr', () => {
    // A suggestion dropdown awaits this reply. It must not hang, and it must not raise a modal over
    // the file the user is typing into.
    it('falls back silently on either failure', async () => {
        const messages = sink();
        const offline = new LspGateway(() => undefined, messages);
        const broken = new LspGateway(() => rejecting('nope'), messages);

        assert.deepEqual(await offline.requestOr('aet/x', {}, { options: [] }), { options: [] });
        assert.deepEqual(await broken.requestOr('aet/x', {}, { options: [] }), { options: [] });
        assert.deepEqual(messages.warnings, []);
        assert.deepEqual(messages.errors, []);
    });

    it('prefers the answer when there is one', async () => {
        const gateway = new LspGateway(() => answering({ entries: [1, 2] }));

        assert.deepEqual(await gateway.requestOr('aet/x', {}, { entries: [] }), { entries: [1, 2] });
    });
});

describe('LspGateway.executeCommand', () => {
    it('sends the workspace/executeCommand envelope', async () => {
        const sent: { method: string; params: unknown }[] = [];
        const gateway = new LspGateway(() => ({
            sendRequest: <T>(method: string, params: unknown) => {
                sent.push({ method, params });
                return Promise.resolve(null as T);
            },
        }));

        assert.equal(await gateway.executeCommand('aet-eaw-edit.lsp.reloadProject', [{ a: 1 }]), true);
        assert.deepEqual(sent, [{
            method: 'workspace/executeCommand',
            params: { command: 'aet-eaw-edit.lsp.reloadProject', arguments: [{ a: 1 }] },
        }]);
    });

    it('reports a failure and says it did not run', async () => {
        const messages = sink();
        const gateway = new LspGateway(() => rejecting('nope'), messages);

        assert.equal(await gateway.executeCommand('aet-eaw-edit.lsp.x', [], 'create the project'), false);
        assert.match(messages.errors[0], /create the project/);
    });
});

describe('LspGateway.notify', () => {
    it('swallows a failure and says so', async () => {
        const messages = sink();
        const gateway = new LspGateway(() => rejecting('nope'), messages);

        assert.equal(await gateway.notify('aet/setStoryLayout', { entries: [] }), false);
        assert.deepEqual(messages.errors, []);
    });

    it('reports success', async () => {
        const gateway = new LspGateway(() => answering(null));

        assert.equal(await gateway.notify('aet/setWorkspaceSettings', {}), true);
    });
});

describe('LspGateway.whenReady', () => {
    it('resolves at once when the server has already started', async () => {
        const gateway = new LspGateway(() => answering({}));
        gateway.markReady();

        assert.equal(await gateway.whenReady(50), true);
    });

    /**
     * The bug this exists for: a preview tab restored when the window opens resolves BEFORE the
     * server has started, its scene request failed once, and nothing ever asked again - so the tab
     * stayed blank until the file was closed and reopened by hand.
     */
    it('waits for a server that starts after the caller asked', async () => {
        const gateway = new LspGateway(() => answering({}));

        const waited = gateway.whenReady(5000);
        setTimeout(() => { gateway.markReady(); }, 20);

        assert.equal(await waited, true);
    });

    it('releases every caller waiting at once', async () => {
        const gateway = new LspGateway(() => answering({}));

        const all = Promise.all([gateway.whenReady(5000), gateway.whenReady(5000)]);
        gateway.markReady();

        assert.deepEqual(await all, [true, true]);
    });

    /**
     * A server that never comes - the extension can be configured with the LSP off - must not leave
     * the caller hanging for ever. It gives up and lets the caller report offline as it always did.
     */
    it('gives up rather than waiting for ever', async () => {
        const gateway = new LspGateway(() => undefined);

        assert.equal(await gateway.whenReady(20), false);
    });

    /** A restart puts it back to not-ready, or the next wait would sail past a stopped server. */
    it('blocks again once the server has stopped', async () => {
        const gateway = new LspGateway(() => undefined);
        gateway.markReady();
        gateway.markStopped();

        assert.equal(await gateway.whenReady(20), false);
    });
});
