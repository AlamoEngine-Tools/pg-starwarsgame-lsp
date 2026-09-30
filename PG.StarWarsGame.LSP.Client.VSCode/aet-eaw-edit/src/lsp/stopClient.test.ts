// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {STOP_TIMEOUT_MS, StoppableClient, stopClient} from './stopClient';

function logger(): StopLog {
    const lines: string[] = [];
    return {
        lines, log: (m: string) => {
            lines.push(m);
        }
    };
}

interface StopLog {
    lines: string[];

    log(message: string): void;
}

function stopping(): StoppableClient & { calls: (number | undefined)[] } {
    const calls: (number | undefined)[] = [];
    return {
        calls, stop: (t?: number) => {
            calls.push(t);
            return Promise.resolve();
        }
    };
}

function refusing(reason: string): StoppableClient {
    return {stop: () => Promise.reject(new Error(reason))};
}

describe('stopClient', () => {
    it('reports the server stopped when the handshake completes', async () => {
        const l = logger();

        assert.equal(await stopClient(stopping(), l.log), 'stopped');
    });

    it('does nothing when there is no client', async () => {
        const l = logger();

        assert.equal(await stopClient(undefined, l.log), 'idle');
        assert.deepEqual(l.lines, []);
    });

    /**
     * The whole point. The old call site let this throw, which skipped clearing the module's client
     * reference AND skipped the restart that was meant to follow.
     */
    it('does not throw when the server will not stop in time', async () => {
        const l = logger();

        const outcome = await stopClient(refusing('Stopping the server timed out'), l.log);

        assert.equal(outcome, 'abandoned');
    });

    it('says in the log why the stop failed and that the server is being killed', async () => {
        const l = logger();

        await stopClient(refusing('Stopping the server timed out'), l.log);

        const line = l.lines.join('\n');
        assert.match(line, /Stopping the server timed out/);
        assert.match(line, /Killing it/);
    });

    it('gives the handshake longer than the library default of 2000ms', async () => {
        const client = stopping();

        await stopClient(client, logger().log);

        assert.deepEqual(client.calls, [STOP_TIMEOUT_MS]);
        assert.ok(STOP_TIMEOUT_MS > 2000,
            'the point of passing a timeout at all is that it beats the library default');
    });

    it('survives a rejection that is not an Error', async () => {
        const l = logger();
        const odd: StoppableClient = {stop: () => Promise.reject('just a string')};

        assert.equal(await stopClient(odd, l.log), 'abandoned');
        assert.match(l.lines.join('\n'), /just a string/);
    });

    /**
     * The library's own safety net is `checkProcessDied`, which force-terminates the server two
     * seconds after shutdown settles. On deactivate the extension host is gone long before that
     * timer fires, so the net never catches anything - which is how a stop that times out leaves a
     * server running with the workspace still open. Killing has to happen now, not on a timer.
     */
    it('kills the server when the handshake fails', async () => {
        const killed: string[] = [];

        const outcome = await stopClient(refusing('Stopping the server timed out'), logger().log,
            {forceKill: () => killed.push('killed')});

        assert.equal(outcome, 'abandoned');
        assert.deepEqual(killed, ['killed'], 'an abandoned server must be killed, not left running');
    });

    it('leaves a server that stopped cleanly alone', async () => {
        const killed: string[] = [];

        await stopClient(stopping(), logger().log, {forceKill: () => killed.push('killed')});

        assert.deepEqual(killed, [],
            'the server exits on its own after a clean stop - killing it would cut its log flush short');
    });

    it('still reports abandoned when the kill itself throws', async () => {
        const l = logger();

        const outcome = await stopClient(refusing('timed out'), l.log, {
            forceKill: () => {
                throw new Error('ESRCH');
            },
        });

        assert.equal(outcome, 'abandoned');
        assert.match(l.lines.join('\n'), /ESRCH/);
    });

    it('takes the timeout from the options object', async () => {
        const client = stopping();

        await stopClient(client, logger().log, {timeoutMs: 250});

        assert.deepEqual(client.calls, [250]);
    });
});
