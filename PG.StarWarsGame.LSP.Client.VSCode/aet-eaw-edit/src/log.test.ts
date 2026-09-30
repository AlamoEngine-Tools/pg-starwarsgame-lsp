// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {logLine, setLogChannel} from './log';

/**
 * A channel stands in for VS Code's, including the one behaviour that matters here: once the host
 * has disposed it, `appendLine` throws rather than becoming a no-op.
 */
function fakeChannel(): { lines: string[]; close(): void; appendLine(line: string): void } {
    let closed = false;
    const lines: string[] = [];
    return {
        lines,
        close: () => {
            closed = true;
        },
        appendLine(line: string) {
            if (closed) {
                throw new Error('Channel has been closed');
            }
            lines.push(line);
        },
    };
}

describe('logLine', () => {
    it('writes a timestamped line to the channel', () => {
        const channel = fakeChannel();
        setLogChannel(channel as never);

        logLine('hello');

        assert.equal(channel.lines.length, 1);
        assert.match(channel.lines[0], /^\[\d{4}-\d{2}-\d{2} [\d:.]+] hello$/);
        setLogChannel(undefined);
    });

    /**
     * On shutdown VS Code disposes the output channel before every extension's `deactivate` has
     * finished, so the last few lines - the ones that say how the server was stopped - land on a
     * dead channel and throw `Error: Channel has been closed`. That reached the extension host's
     * error log on every single exit, through `stopClient` inside `deactivate`, and an exception
     * there can abandon the rest of the shutdown.
     */
    it('does not throw once the host has disposed the channel', () => {
        const channel = fakeChannel();
        setLogChannel(channel as never);
        channel.close();

        assert.doesNotThrow(() => logLine('stopping the server'));
        setLogChannel(undefined);
    });

    it('is a no-op before a channel is set', () => {
        setLogChannel(undefined);

        assert.doesNotThrow(() => logLine('too early'));
    });
});
