// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {
    breakAvailability, type LuaScriptsResult, scriptRows, sessionSummary, threadRows,
} from './luaScriptsModel';

// A payload as the adapter answers eawLua/scripts against the fake game: two instances of the
// same file, one of them stopped, one script under no source root, one with its threads loaded.
const captured: LuaScriptsResult = {
    runState: 'suspended',
    scripts: [
        {
            scriptId: 344,
            gamePath: 'Data\\Scripts\\AI\\SpaceMode\\Attack.lua',
            name: 'Attack.lua',
            path: null,
            attached: false,
            isContext: false,
            isSuspended: false,
            threads: null,
        },
        {
            scriptId: 343,
            gamePath: 'Data\\Scripts\\GameObject\\Hero.lua',
            name: 'Hero.lua',
            path: 'D:\\Mod\\Data\\Scripts\\GameObject\\Hero.lua',
            attached: true,
            isContext: true,
            isSuspended: true,
            threads: [{threadIndex: 0, name: 'main'}, {threadIndex: 2, name: 'patrol'}],
        },
        {
            scriptId: 345,
            gamePath: 'Data\\Scripts\\GameObject\\Hero.lua',
            name: 'Hero.lua',
            path: 'D:\\Mod\\Data\\Scripts\\GameObject\\Hero.lua',
            attached: true,
            isContext: false,
            isSuspended: false,
            threads: [],
        },
    ],
};

describe('scriptRows', () => {
    it('keeps the adapter order and names each instance by file and id', () => {
        const rows = scriptRows(captured);

        assert.deepEqual(rows.map(r => r.label), ['Attack.lua', 'Hero.lua', 'Hero.lua']);
        assert.deepEqual(rows.map(r => r.scriptId), [344, 343, 345]);
        assert.equal(rows[0].description, '#344');
    });

    it('marks the stopped instance, the context script and attached scripts in the description', () => {
        const rows = scriptRows(captured);

        assert.equal(rows[1].description, '#343 - stopped');
        assert.equal(rows[2].description, '#345 - attached');
        assert.equal(rows[1].icon, 'debug-pause');
        assert.equal(rows[2].icon, 'debug-breakpoint-log');
        assert.equal(rows[0].icon, 'file-code');
    });

    it('tells a resolved file from one under no source root', () => {
        const rows = scriptRows(captured);

        assert.equal(rows[1].path, 'D:\\Mod\\Data\\Scripts\\GameObject\\Hero.lua');
        assert.match(rows[1].tooltip, /Hero\.lua \[343\]/);
        assert.equal(rows[0].path, null);
        assert.match(rows[0].tooltip, /not under any source root/);
    });

    it('gives every script row the same context value so the inline actions apply to all', () => {
        assert.ok(scriptRows(captured).every(r => r.contextValue === 'aetLuaScript'));
    });
});

describe('threadRows', () => {
    it('offers the main state first, then the named threads the game reported', () => {
        const rows = threadRows(scriptRows(captured)[1]);

        assert.ok(rows);
        assert.deepEqual(rows.map(r => r.label), ['main state', 'main', 'patrol']);
        assert.deepEqual(rows.map(r => r.threadIndex), [-1, 0, 2]);
        assert.ok(rows.every(r => r.scriptId === 343 && r.contextValue === 'aetLuaThread'));
    });

    it('is only the main state when the game reported no named threads', () => {
        const rows = threadRows(scriptRows(captured)[2]);

        assert.ok(rows);
        assert.deepEqual(rows.map(r => r.label), ['main state']);
    });

    it('is undefined until the threads have been fetched, so the view knows to ask', () => {
        assert.equal(threadRows(scriptRows(captured)[0]), undefined);
    });
});

describe('breakAvailability', () => {
    it('allows a break while the game runs', () => {
        assert.deepEqual(breakAvailability('running'), {allowed: true});
    });

    it('refuses with the engine rule while a break is armed or a script is stopped', () => {
        assert.deepEqual(breakAvailability('breakArmed'),
            {allowed: false, reason: 'A break is already armed and waiting for the next Lua line'});
        assert.deepEqual(breakAvailability('suspended'),
            {allowed: false, reason: 'A script is stopped; continue or step first'});
    });
});

describe('sessionSummary', () => {
    it('counts the instances and names the run state', () => {
        assert.equal(sessionSummary(captured), '3 scripts - stopped in Hero.lua [343]');
        assert.equal(sessionSummary({
            runState: 'running',
            scripts: captured.scripts.slice(0, 1)
        }), '1 script - running');
        assert.equal(sessionSummary({runState: 'breakArmed', scripts: []}), '0 scripts - break armed');
    });
});
