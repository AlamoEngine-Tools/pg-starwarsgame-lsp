// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {battleKeyOfNode, battleLabelOf, factionTree} from './storyBattles';
import type {StoryFactionDto} from './protocol';

const faction: StoryFactionDto = {
    faction: 'Rebel',
    manifestFile: 'Story_Plots_Rebel.xml',
    threads: [
        {file: 'Story_Rebel_Act_I.xml', suspended: false, uri: 'file:///ws/Data/XML/Story_Rebel_Act_I.xml'},
        // A faction manifest that also lists a battle's file: the battle keeps it, whatever the casing.
        {file: 'Story_M5_Space.xml', suspended: false, uri: 'file:///ws/Data/XML/Story_M5_Space.xml'},
        {file: 'Story_Unresolved.xml', suspended: false, uri: null},
    ],
    luaScripts: [],
    battles: [
        // Listed out of play order on purpose: the tree must follow rank, not this list.
        {
            key: 'story_plots_m5_space.xml', label: 'Story_Plots_M5_Space', rank: 1, entryEventIds: [],
            threads: [{file: 'story_m5_SPACE.xml', suspended: false, uri: 'file:///ws/data/xml/story_m5_space.xml'}],
        },
        {
            key: 'story_plots_m2_land.xml', label: 'Story_Plots_M2_Land', rank: 0, entryEventIds: [],
            threads: [
                {file: 'Story_M2_Land.xml', suspended: true, uri: 'file:///ws/data/xml/story_m2_land.xml'},
                {
                    file: 'Story_M2_Land_Objectives.xml',
                    suspended: false,
                    uri: 'file:///ws/data/xml/story_m2_land_objectives.xml'
                },
            ],
        },
    ],
};

describe('factionTree', () => {
    it('lists the battles in play order, each with the plot files the server gave it', () => {
        const tree = factionTree(faction);

        assert.deepEqual(tree.battles.map(b => b.battle.label), ['Story_Plots_M2_Land', 'Story_Plots_M5_Space']);
        assert.deepEqual(tree.battles[0].threads.map(t => t.file), ['Story_M2_Land.xml', 'Story_M2_Land_Objectives.xml']);
        assert.deepEqual(tree.battles[1].threads.map(t => t.file), ['story_m5_SPACE.xml']);
    });

    /**
     * The galactic level is the faction manifest's list, less what a battle already shows - a file
     * is never listed twice - and including a thread the server could not resolve, since dropping
     * it would hide a broken chain link, which is the one thread the author most needs to see.
     */
    it('keeps the galactic threads in feed order, less the battles files, resolved or not', () => {
        const tree = factionTree(faction);

        assert.deepEqual(tree.galacticThreads.map(t => t.file), ['Story_Rebel_Act_I.xml', 'Story_Unresolved.xml']);
    });

    it('is the plain thread list when the server reports no battles at all', () => {
        const tree = factionTree({...faction, battles: undefined});

        assert.deepEqual(tree.battles, []);
        assert.equal(tree.galacticThreads.length, faction.threads.length);
    });
});

describe('battleLabelOf', () => {
    /**
     * The stub on the galactic graph carries the reference as the XML wrote it, the navigator
     * carries the server's label; a panel opened from either must get the same title.
     */
    it('is the file name without directory or extension, keeping the casing', () => {
        assert.equal(battleLabelOf('Story_Plots_Underworld_M02_SPACE.xml'), 'Story_Plots_Underworld_M02_SPACE');
        assert.equal(battleLabelOf('DATA\\XML\\Story_Plots_M2_Land.XML'), 'Story_Plots_M2_Land');
        assert.equal(battleLabelOf('data/xml/story_plots_m2_land.xml'), 'story_plots_m2_land');
    });

    it('leaves a name with no extension alone', () => {
        assert.equal(battleLabelOf('Story_Plots_M2_Land'), 'Story_Plots_M2_Land');
    });
});

describe('battleKeyOfNode', () => {
    /** The server names a battle's stand-in `tactical#<key>`; the key is what opens its panel. */
    it('reads the battle key out of a tactical stub id', () => {
        assert.equal(battleKeyOfNode('tactical#story_plots_m2_land.xml'), 'story_plots_m2_land.xml');
    });

    it('is undefined for any other node', () => {
        assert.equal(battleKeyOfNode('file:///ws/data/xml/story.xml#start'), undefined);
        assert.equal(battleKeyOfNode('galactic#story_plots_m2_land.xml#file:///x#e'), undefined);
    });
});
