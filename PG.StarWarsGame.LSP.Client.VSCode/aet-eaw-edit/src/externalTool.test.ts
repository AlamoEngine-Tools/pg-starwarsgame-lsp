// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {EventEmitter} from 'node:events';
import {describe, it} from 'node:test';

import {handoffFile, launchDetached, type SpawnFn} from './externalTool';

/** A child process that reports whichever outcome the test hands it, on the next tick. */
function fakeSpawn(outcome: 'spawn' | Error): { spawn: SpawnFn; calls: unknown[][] } {
    const calls: unknown[][] = [];
    const spawn: SpawnFn = (command, args, options) => {
        calls.push([command, args, options]);
        const child = Object.assign(new EventEmitter(), {unref: () => undefined});
        setImmediate(() => outcome === 'spawn' ? child.emit('spawn') : child.emit('error', outcome));
        return child;
    };
    return {spawn, calls};
}

describe('handoffFile', () => {
    it('prefers the file the preview was opened from', () => {
        // The server resolves by NAME, so with a mod shadowing the base game it would hand over the
        // mod's copy even when the author opened the base game's.
        assert.equal(
            handoffFile('D:/Mod/Data/Art/Models/HULL.ALO', {path: 'C:/Game/HULL.ALO', packed: false}),
            'D:/Mod/Data/Art/Models/HULL.ALO');
    });

    it('falls back to the file the server resolved the name to', () => {
        assert.equal(handoffFile(null, {path: 'C:/Game/HULL.ALO', packed: false}), 'C:/Game/HULL.ALO');
    });

    it('has nothing to hand over for a packed model', () => {
        assert.equal(handoffFile(null, {path: null, packed: true}), null);
    });

    it('has nothing to hand over when the name resolved nowhere', () => {
        assert.equal(handoffFile(null, null), null);
    });
});

describe('launchDetached', () => {
    it('starts the executable directly with the file as its one argument', async () => {
        // No shell: the old launch typed a PowerShell command line into whatever the default
        // terminal was, which in cmd or Git Bash never started the tool at all.
        const {spawn, calls} = fakeSpawn('spawn');

        await launchDetached('C:/Tools/AloViewer.exe', 'C:/Game/HULL.ALO', spawn);

        assert.equal(calls.length, 1);
        const [command, args, options] = calls[0] as [string, string[], { detached: boolean; shell?: boolean }];
        assert.equal(command, 'C:/Tools/AloViewer.exe');
        assert.deepEqual(args, ['C:/Game/HULL.ALO']);
        assert.equal(options.detached, true);
        assert.notEqual(options.shell, true);
    });

    it('rejects with the reason when the executable cannot be started', async () => {
        const {spawn} = fakeSpawn(new Error('spawn C:/Tools/Missing.exe ENOENT'));

        await assert.rejects(
            launchDetached('C:/Tools/Missing.exe', 'C:/Game/HULL.ALO', spawn),
            /ENOENT/);
    });
});
