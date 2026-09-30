// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import type {StoryCampaignDto, StoryFactionDto} from './protocol';
import {
    definitionLabel, factionRole, orderedDefinitions, orderedFactions, roleLabel, setSummary,
} from './storyNavigatorModel';

function faction(name: string, control?: string | null): StoryFactionDto {
    return {faction: name, manifestFile: `Story_Plots_${name}.xml`, threads: [], luaScripts: [], control};
}

function campaign(name: string, playerFaction: string | null, factions: StoryFactionDto[], set?: string): StoryCampaignDto {
    return {name, factions, set, playerFaction};
}

describe('factionRole', () => {
    it('is the player for the Starting_Active_Player faction, keeping the AI it is also given', () => {
        // The human's player gets an AI too (perception and the rest of the AI setup) - e.g.
        // SandboxHuman - since Assign_AI_Control runs for every active player.
        const c = campaign('Sandbox_Rebel', 'Rebel', [faction('rebel', 'SandboxHuman')]);
        assert.deepEqual(factionRole(c, c.factions[0]), {kind: 'player', aiType: 'SandboxHuman'});
    });

    it('is the player without an AI when its control is Human or absent', () => {
        const c = campaign('Story', 'Rebel', [faction('Rebel', 'Human'), faction('Empire')]);
        assert.deepEqual(factionRole(c, c.factions[0]), {kind: 'player'});
        const d = campaign('Story', 'Empire', [faction('Empire')]);
        assert.deepEqual(factionRole(d, d.factions[0]), {kind: 'player'});
    });

    it('is human only for the Human player type, read however it is cased', () => {
        // Measured: Set_AI_Control compares against "Human" with _stricmp and drops the AI.
        const c = campaign('Quick_Demo', 'Rebel', [faction('Empire', 'HUMAN')]);
        assert.deepEqual(factionRole(c, c.factions[0]), {kind: 'human'});
    });

    it('is an AI for every other player type - None included, which is a real AI type', () => {
        const c = campaign('Full_Story_Campaign_Rebel', 'Rebel', [faction('Empire', 'None'), faction('Pirates', 'BasicEmpire')]);
        assert.deepEqual(factionRole(c, c.factions[0]), {kind: 'ai', type: 'None'});
        assert.deepEqual(factionRole(c, c.factions[1]), {kind: 'ai', type: 'BasicEmpire'});
    });

    it('is unset when the campaign names no player and has no control pair for it', () => {
        const c = campaign('Multiplayer_Campaign_Hoth', null, [faction('Rebel', null)]);
        assert.deepEqual(factionRole(c, c.factions[0]), {kind: 'unset'});
    });
});

describe('roleLabel', () => {
    it('names each role tersely', () => {
        assert.equal(roleLabel({kind: 'player'}), 'Player');
        assert.equal(roleLabel({kind: 'player', aiType: 'SandboxHuman'}), 'Player, AI - SandboxHuman');
        assert.equal(roleLabel({kind: 'human'}), 'Human');
        assert.equal(roleLabel({kind: 'ai', type: 'BasicRebel'}), 'AI - BasicRebel');
        assert.equal(roleLabel({kind: 'unset'}), undefined);
    });
});

describe('orderedFactions', () => {
    it('puts the player first and keeps the declared order otherwise', () => {
        const c = campaign('GC', 'Empire', [faction('Rebel', 'None'), faction('Hutts'), faction('Empire')]);
        assert.deepEqual(orderedFactions(c).map(f => f.faction), ['Empire', 'Rebel', 'Hutts']);
    });
});

describe('setSummary', () => {
    it('counts factions when every member is one playable perspective', () => {
        const members = [
            campaign('Story_Rebel', 'Rebel', []), campaign('Story_Empire', 'Empire', []),
            campaign('Story_Underworld', 'Underworld', []),
        ];
        assert.equal(setSummary(members, true), '3 factions');
    });

    it('reads one perspective in the singular', () => {
        assert.equal(setSummary([campaign('Tutorial_One', 'Rebel', [])], true), '1 faction');
    });

    it('counts campaigns when a member names no player - a set of maps, not of perspectives', () => {
        const members = [campaign('Multiplayer_Campaign_Hoth', null, []), campaign('Multiplayer_Campaign_Yavin', null, [])];
        assert.equal(setSummary(members, true), '2 campaigns');
    });

    it('counts campaigns for the ungrouped bucket, which is not one campaign', () => {
        const members = [campaign('A', 'Rebel', []), campaign('B', 'Empire', [])];
        assert.equal(setSummary(members, false), '2 campaigns');
    });
});

describe('definitionLabel', () => {
    it('labels a set member by the faction it is played as, the definition name beside it', () => {
        assert.deepEqual(definitionLabel(campaign('Full_Story_Campaign_Empire', 'Empire', []), true),
            {label: 'Empire', description: 'Full_Story_Campaign_Empire'});
    });

    it('falls back to the definition name without a player or outside a set', () => {
        assert.deepEqual(definitionLabel(campaign('Multiplayer_Campaign_Hoth', null, []), true),
            {label: 'Multiplayer_Campaign_Hoth', description: undefined});
        assert.deepEqual(definitionLabel(campaign('Loose', 'Rebel', []), false),
            {label: 'Loose', description: undefined});
    });
});

describe('orderedDefinitions', () => {
    it('sorts set members by their label', () => {
        const members = [campaign('Story_Rebel', 'Rebel', []), campaign('Story_Empire', 'Empire', [])];
        assert.deepEqual(orderedDefinitions(members, true).map(c => c.name), ['Story_Empire', 'Story_Rebel']);
    });
});
