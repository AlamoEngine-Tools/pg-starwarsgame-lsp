// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {battlesToEnter, portalPickAction, shouldResumeAfterAnswer} from './simModel';

// A pick on a battle's portal: in Simulation it selects the battle's decision beside the dock -
// the picker, not the other panel; in View it goes through to the battle's graph; in Edit a pick
// starts a drag and does nothing else. The jump arrow always opens the graph, into Simulation
// while the galaxy simulates.
describe('portalPickAction', () => {
    it('selects in Simulation, opens in View, does nothing in Edit', () => {
        assert.equal(portalPickAction('simulate'), 'select');
        assert.equal(portalPickAction('view'), 'open');
        assert.equal(portalPickAction('edit'), 'none');
    });
});

// The galaxy chose to fight (the author on the picker, or a forced click on the fight button): the
// battle's panel opens into its session once, when the status turns, never again on a re-fetch.
describe('battlesToEnter', () => {
    const battle = (key: string, status: string) => ({key, label: key, status, tick: 0});

    it('names a battle whose status just turned to fight', () => {
        assert.deepEqual(battlesToEnter([battle('m1', 'pending')], [battle('m1', 'fight')]), ['m1']);
        assert.deepEqual(battlesToEnter(null, [battle('m1', 'fight')]), ['m1']);
    });

    it('names nothing for a battle already fighting, running, pending or resolved', () => {
        assert.deepEqual(battlesToEnter([battle('m1', 'fight')], [battle('m1', 'fight')]), []);
        assert.deepEqual(battlesToEnter([battle('m1', 'fight')], [battle('m1', 'running')]), []);
        assert.deepEqual(battlesToEnter([battle('m1', 'notStarted')], [battle('m1', 'pending')]), []);
        assert.deepEqual(battlesToEnter([battle('m1', 'running')], [battle('m1', 'won')]), []);
        assert.deepEqual(battlesToEnter([battle('m1', 'fight')], null), []);
    });
});

describe('shouldResumeAfterAnswer', () => {
    const base = {autoResume: true, pausedForWait: true, stillWaiting: false, halted: false};

    it('resumes when play paused itself for a decision that is now answered', () => {
        assert.equal(shouldResumeAfterAnswer(base), true);
    });

    it('stays paused when the setting is off, play was never on, a breakpoint holds, or another decision waits', () => {
        assert.equal(shouldResumeAfterAnswer({...base, autoResume: false}), false);
        assert.equal(shouldResumeAfterAnswer({...base, pausedForWait: false}), false);
        assert.equal(shouldResumeAfterAnswer({...base, halted: true}), false);
        assert.equal(shouldResumeAfterAnswer({...base, stillWaiting: true}), false);
    });
});
import {describe, it} from 'node:test';

import {
    StoryGraphEdgeDto,
    StoryGraphNodeDto,
    StorySimInterventionDto,
    StorySimStepDto,
    StorySimWorldDto
} from '../../protocol/story';
import {
    armingLines, filterTrace, groupDecisions, labelFor, paceIntervalMs, parsePace, pickerCandidates, traceRows,
} from './simModel';

function intervention(name: string, kind: string, facet: string | null, options: string[] = []): StorySimInterventionDto {
    return {
        kind,
        nodeId: 'file:///a.xml#' + name.toLowerCase(),
        eventName: name,
        eventType: null,
        options,
        facet,
        suggested: null
    };
}

describe('groupDecisions', () => {
    it('groups by answer kind in a fixed order and keeps wire order inside a group', () => {
        const groups = groupDecisions([
            intervention('Fire', 'manual', null),
            intervention('Talk', 'lua', null, ['ID']),
            intervention('Kuat', 'manual', 'capturePlanet', ['Kuat']),
            intervention('Win', 'tactical', 'battleWon'),
            intervention('Hoth', 'manual', 'capturePlanet', ['Hoth']),
        ]);

        assert.deepEqual(groups.map(g => [g.kind, g.items.map(i => i.eventName)]),
            [['world', ['Kuat', 'Hoth']], ['tactical', ['Win']], ['lua', ['Talk']], ['assume', ['Fire']]]);
    });
});

describe('trace rows', () => {
    const labelOf = (id: string): string | undefined => ({'x#a': 'Alpha', 'x#b': 'Beta'})[id];
    const steps: StorySimStepDto[] = [
        {tick: 0, seq: 0, nodeId: 'x#a', from: 'Waiting', to: 'Armed', sourceNodeId: null, cause: 'load', detail: null},
        {
            tick: 1,
            seq: 1,
            nodeId: 'x#b',
            from: 'Waiting',
            to: 'Fired',
            sourceNodeId: 'x#a',
            cause: 'prereq',
            detail: null
        },
        {
            tick: 1,
            seq: 2,
            nodeId: 'file:///s.lua#lua#act_i',
            from: null,
            to: null,
            sourceNodeId: 'x#b',
            cause: 'luaEnter',
            detail: 'entered'
        },
    ];

    it('names nodes by label, and a script state by its name after the marker', () => {
        const rows = traceRows(steps, labelOf);
        assert.deepEqual(rows.map(r => r.node), ['Alpha', 'Beta', 'act_i']);
        assert.deepEqual(rows.map(r => r.via), [null, 'Alpha', 'Beta']);
    });

    it('filters by substring and by tick', () => {
        const rows = traceRows(steps, labelOf);
        assert.deepEqual(filterTrace(rows, 'beta').map(r => r.seq), [1, 2]);
        assert.deepEqual(filterTrace(rows, 'T1').map(r => r.seq), [1, 2]);
        assert.deepEqual(filterTrace(rows, 'luaenter').map(r => r.seq), [2]);
        assert.equal(filterTrace(rows, '   ').length, 3);
    });

    it('falls back to the id tail for an unknown node', () => {
        assert.equal(labelFor('file:///t.xml#some_event', () => undefined), 'some_event');
    });
});

describe('armingLines', () => {
    const node = (id: string, kind: string): StoryGraphNodeDto =>
        ({
            id,
            kind,
            label: id.toUpperCase(),
            threadUri: null,
            line: null,
            eventType: null,
            rewardType: null,
            branch: null,
            lifecycle: null,
            reachable: true
        }) as unknown as StoryGraphNodeDto;
    const edge = (fromId: string, toId: string, kind = 'Prereq'): StoryGraphEdgeDto => ({
        fromId,
        toId,
        kind,
        label: null
    });
    const nodes = [node('a', 'Event'), node('b', 'Event'), node('c', 'Event'), node('t', 'Event'), node('t#g0', 'AndJunction'), node('t#or', 'OrJunction')];
    const edges = [edge('a', 't#g0'), edge('b', 't#g0'), edge('t#g0', 't#or'), edge('c', 't#or'), edge('t#or', 't')];

    it('reads OR of AND lines through the junctions with each member fired state', () => {
        const lines = armingLines('t', nodes, edges, id => (id === 'a' ? 'Fired' : 'Armed'));

        assert.deepEqual(lines.map(l => [l.satisfied, l.members.map(m => m.nodeId + (m.fired ? '*' : ''))]),
            [[false, ['a*', 'b']], [false, ['c']]]);
    });

    it('is empty for a root and satisfied when a line is all fired', () => {
        assert.deepEqual(armingLines('a', nodes, edges, () => 'Armed'), []);
        const lines = armingLines('t', nodes, edges, id => (id === 'c' ? 'Fired' : 'Waiting'));
        assert.deepEqual(lines.map(l => l.satisfied), [false, true]);
    });
});

describe('pickerCandidates', () => {
    const world: StorySimWorldDto = {
        planets: [{name: 'Kuat', owner: 'Empire', revealed: true, corrupted: false, destroyed: false},
            {name: 'Hoth', owner: 'Rebel', revealed: true, corrupted: false, destroyed: false}],
        units: [{type: 'TIE', owner: 'Empire', planet: 'Kuat', count: 2}, {
            type: 'X_Wing',
            owner: 'Rebel',
            planet: 'Hoth',
            count: 1
        }],
        tech: [], credits: [], era: null, counters: [], objectives: [],
    };

    it('lists the event candidates first, else the planets the faction does not hold', () => {
        assert.deepEqual(pickerCandidates('capturePlanet', ['Corellia'], world, 'Rebel').preferred, ['Corellia']);
        const open = pickerCandidates('capturePlanet', [], world, 'Rebel');
        assert.deepEqual(open.preferred, ['Kuat']);
        assert.deepEqual(open.all, ['Kuat', 'Hoth']);
    });

    it('lists someone elses unit types for a destroy and every type otherwise', () => {
        assert.deepEqual(pickerCandidates('destroyUnit', [], world, 'Rebel').preferred, ['TIE']);
        assert.deepEqual(pickerCandidates('buildUnit', [], world, 'Rebel').preferred, ['TIE', 'X_Wing']);
        assert.equal(pickerCandidates('setTech', [], world, 'Rebel').kind, 'none');
    });
});

describe('pace', () => {
    it('parses a stored pace and falls back on garbage', () => {
        assert.deepEqual(parsePace('{"mode":"custom","ticksPerSecond":8}'), {mode: 'custom', ticksPerSecond: 8});
        assert.deepEqual(parsePace('{"mode":"fast"}').mode, 'pulse');
        assert.deepEqual(parsePace('nonsense').mode, 'pulse');
        assert.equal(paceIntervalMs({mode: 'custom', ticksPerSecond: 4}), 250);
        assert.equal(paceIntervalMs({mode: 'pulse', ticksPerSecond: 4}), 1000);
    });
});
