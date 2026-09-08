// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { DEFAULT_ATTACKER } from './attacker';
import { viewerSettingsFrom } from './viewerSettings';
import { projectSettingsFrom, DEFAULT_PROJECT_SETTINGS } from './projectSettings';

describe('projectSettingsFrom', () => {
    it('reads back a weapon that was saved', () => {
        const bench = projectSettingsFrom({
            attacker: { damage: 250, damageType: 'Damage_Laser', shield: true },
            presets: [{ id: 'a', name: 'Broadside missile', damage: 900 }],
        });

        assert.equal(bench.attacker.damage, 250);
        assert.equal(bench.attacker.damageType, 'Damage_Laser');
        assert.equal(bench.presets.length, 1);
        assert.equal(bench.presets[0].name, 'Broadside missile');
    });

    it('defaults field by field rather than throwing', () => {
        // The blob outlives the build that wrote it. A string where a number was expected must cost
        // the reader their weapon, not their whole bench.
        const bench = projectSettingsFrom({ attacker: { damage: 'lots', damageType: 7 } });

        assert.equal(bench.attacker.damage, DEFAULT_ATTACKER.damage);
        assert.equal(bench.attacker.damageType, DEFAULT_ATTACKER.damageType);
    });

    it('answers the default bench for nothing at all', () => {
        assert.deepEqual(projectSettingsFrom(undefined), DEFAULT_PROJECT_SETTINGS);
        assert.deepEqual(projectSettingsFrom('not a bench'), DEFAULT_PROJECT_SETTINGS);
    });

    it('drops a preset that cannot name itself', () => {
        const bench = projectSettingsFrom({
            presets: [{ id: 'a', name: 'Keep' }, { id: 'b' }, 'rubbish'],
        });

        assert.deepEqual(bench.presets.map(p => p.name), ['Keep']);
    });
});

// The reported fault: a damage type from one mod came back in another, where the tree declares no
// such type. The panel already SAYS so - the picker offers it as "(not in this tree)" rather than
// silently choosing whatever sorts first - but saying so every time is not the fix. The setting was
// at the wrong tier.
//
// The rule that decides it is the repo's own: a setting lives at the tier of the thing it describes,
// and you would expect the answer to change when a different mod is opened. So the bench is
// workspace-scoped now, and the ROOM - the grid, the lights, the camera presets - stays global.
// The faction is named by the mod's own tree, exactly as a damage type is - a mod can declare
// factions the next one has never heard of - and the colour picked to match one belongs with it.
describe('the faction and its colour', () => {
    it('reads back what this project was left set to', () => {
        const read = projectSettingsFrom({ faction: 'Rebel', customColour: '#88ccff' });

        assert.equal(read.faction, 'Rebel');
        assert.equal(read.customColour, '#88ccff');
    });

    it('opens on the model`s own colours when the project has none stored', () => {
        assert.equal(DEFAULT_PROJECT_SETTINGS.faction, null);
        assert.equal(DEFAULT_PROJECT_SETTINGS.customColour, null);
    });

    it('refuses anything that is not a name', () => {
        const read = projectSettingsFrom({ faction: 7, customColour: { r: 1 } });

        assert.equal(read.faction, null);
        assert.equal(read.customColour, null);
    });
});

describe('the project settings are not part of the room', () => {
    it('leaves all four out of the viewer settings entirely', () => {
        const room = viewerSettingsFrom({}) as unknown as Record<string, unknown>;

        assert.equal(room.attacker, undefined);
        assert.equal(room.attackerPresets, undefined);
        assert.equal(room.faction, undefined);
        assert.equal(room.customColour, undefined);
    });

    it('refuses to hand back an attacker an older global blob still carries', () => {
        // Every existing install has one: the bench used to live in `globalState` and that blob is
        // still on disk. Reading it back would put the leak straight back, so the room drops those
        // fields on the way through rather than passing them along.
        const room = viewerSettingsFrom({
            grid: false,
            attacker: { damage: 999, damageType: 'Damage_FromAnotherMod' },
            attackerPresets: [{ id: 'x', name: 'Leaked' }],
            faction: 'Faction_FromAnotherMod',
            customColour: '#ff0000',
        }) as unknown as Record<string, unknown>;

        assert.equal(room.attacker, undefined);
        assert.equal(room.attackerPresets, undefined);
        assert.equal(room.faction, undefined);
        assert.equal(room.customColour, undefined);
        // The rest of the room is untouched - this is a tier move, not a reset.
        assert.equal(room.grid, false);
    });
});
