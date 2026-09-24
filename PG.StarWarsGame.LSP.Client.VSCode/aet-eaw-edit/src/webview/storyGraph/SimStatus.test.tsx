// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {renderToStaticMarkup} from 'react-dom/server';

import type {StorySimStateDto} from '../../protocol/story';
import {SimStatus} from './SimDock';

const noop = (): void => undefined;

function state(overrides: Partial<StorySimStateDto>): StorySimStateDto {
    return {
        running: true, tick: 12, clock: 9, interventions: [], clockPending: 1, battles: [],
        haltedAt: null, pausedFor: null, outcome: null, breakpoints: [], breakOnGates: false,
        ...overrides,
    } as unknown as StorySimStateDto;
}

const render = (s: StorySimStateDto, playing = false): string => renderToStaticMarkup(
    <SimStatus state={s} playing={playing} labelOf={() => undefined} onSelect={noop} onPlayPause={noop}/>);

describe('SimStatus', () => {
    /** The player's wide readout: the state first, the tick and the clock at the far end. */
    it('reads the state and ends with the tick and the clock', () => {
        const html = render(state({}));

        assert.match(html, /class="sim-status paused"/);
        assert.match(html, />Paused</);
        assert.match(html, /class="sim-status-time">t12 - 9s</);
    });

    it('says running while play is on', () => {
        assert.match(render(state({}), true), /class="sim-status running"[\s\S]*>Running</);
    });

    it('offers the retry when a lost battle has left the galaxy nothing to do', () => {
        const lost = state({
            clockPending: 0, battles: [{key: 'm01', label: 'M01', status: 'lost', tick: 0}],
        } as unknown as Partial<StorySimStateDto>);

        const html = renderToStaticMarkup(
            <SimStatus state={lost} playing={false} labelOf={() => undefined} onSelect={noop}
                       onPlayPause={noop} onRetry={noop}/>);

        assert.match(html, /class="sim-status lost"/);
        assert.match(html, />Battle lost - M01</);
        assert.match(html, /title="Retry - back to just before the outcome"/);
    });

    it('says when the story waits on the author', () => {
        const waiting = state({
            clockPending: 0,
            interventions: [{nodeId: 'a#x', kind: 'world', eventName: 'X', eventType: 'STORY_ENTER', options: []}],
        } as unknown as Partial<StorySimStateDto>);

        assert.match(render(waiting), /class="sim-status owed"[\s\S]*>Waiting for input</);
    });
});
