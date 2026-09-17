// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {buildModChain, collectSourceRoots, type LaunchLayer} from './launchLayers';

function layer(name: string, rank: number, modPath: string | null, scriptRoots: string[], reason: string | null = null): LaunchLayer {
    return {
        name,
        rank,
        projectPath: modPath === null ? null : `${modPath}/${name}.pgproj`,
        projectDirectory: modPath,
        scriptRoots,
        modPath,
        notRunnableReason: reason,
    };
}

describe('collectSourceRoots', () => {
    it('lists the mod first and its dependencies after, whatever order the layers arrive in', () => {
        const roots = collectSourceRoots([
            layer('Dep', 0, 'D:/Mods/Dep', ['D:/Mods/Dep/Data/Scripts']),
            layer('Mod', 1, 'D:/Mods/Mod', ['D:/Mods/Mod/Data/Scripts', 'D:/Mods/Mod/Data/Scripts/Story']),
        ]);

        assert.deepEqual(roots, ['D:/Mods/Mod/Data/Scripts', 'D:/Mods/Mod/Data/Scripts/Story', 'D:/Mods/Dep/Data/Scripts']);
    });

    it('drops a root two layers share, keeping the first spelling', () => {
        const roots = collectSourceRoots([
            layer('Mod', 1, 'D:/Mods/Mod', ['D:/Shared/Scripts']),
            layer('Dep', 0, 'D:/Mods/Dep', ['d:\\shared\\scripts']),
        ]);

        assert.deepEqual(roots, ['D:/Shared/Scripts']);
    });
});

describe('buildModChain', () => {
    it('emits one MODPATH per layer, leaf first', () => {
        const chain = buildModChain([
            layer('DepB', 0, 'D:/Mods/DepB', []),
            layer('Mod', 2, 'D:/Mods/Mod', []),
            layer('DepA', 1, 'D:/Mods/DepA', []),
        ]);

        assert.deepEqual(chain, {
            ok: true,
            args: ['MODPATH=D:/Mods/Mod', 'MODPATH=D:/Mods/DepA', 'MODPATH=D:/Mods/DepB']
        });
    });

    it('refuses the whole launch when one layer is not runnable, naming it and its reason', () => {
        const chain = buildModChain([
            layer('Mod', 1, 'D:/Mods/Mod', []),
            layer('Dep', 0, null, [], "'D:/Mods/Dep/src' is not under 'D:/Mods/Dep/Data'"),
        ]);

        assert.equal(chain.ok, false);
        if (!chain.ok) {
            assert.match(chain.message, /'Dep'/);
            assert.match(chain.message, /not under/);
            assert.match(chain.message, /modPaths/);
        }
    });

    it('refuses when no layer is loaded at all', () => {
        const chain = buildModChain([]);

        assert.equal(chain.ok, false);
    });

    it('takes an explicit override verbatim and in the order given', () => {
        const chain = buildModChain([layer('Mod', 0, null, [], 'whatever')], ['Mods\\Leaf', 'Mods\\Root']);

        assert.deepEqual(chain, {ok: true, args: ['MODPATH=Mods\\Leaf', 'MODPATH=Mods\\Root']});
    });

    it('refuses an override containing a space, because the game cannot parse it', () => {
        const chain = buildModChain([], ['Mods\\My Mod']);

        assert.equal(chain.ok, false);
        if (!chain.ok) {
            assert.match(chain.message, /space/);
        }
    });
});
