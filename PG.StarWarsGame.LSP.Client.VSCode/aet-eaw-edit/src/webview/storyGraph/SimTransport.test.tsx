// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import type { StorySimStateDto } from "../../protocol/story";
import { type SimActions, SimTransport } from "./SimDock";
import { SPEED_STOPS } from "./simModel";

const noop = (): void => undefined;

function state(overrides: Partial<StorySimStateDto>): StorySimStateDto {
    return {
        running: true,
        tick: 12,
        clock: 9,
        interventions: [],
        clockPending: 1,
        battles: [],
        haltedAt: null,
        pausedFor: null,
        outcome: null,
        breakpoints: [],
        breakOnGates: false,
        assumeMediaCompletes: true,
        ...overrides,
    } as unknown as StorySimStateDto;
}

const render = (s: StorySimStateDto, autoResume: boolean): string =>
    renderToStaticMarkup(
        <SimTransport
            state={s}
            pace={SPEED_STOPS[1]}
            playing={false}
            setPace={noop}
            actions={new Proxy({}, { get: () => noop }) as SimActions}
            labelOf={() => undefined}
            onSelect={noop}
            notice={null}
            autoResume={autoResume}
            setAutoResume={noop}
        />,
    );

/** The button whose accessible name is `name`, as rendered markup. */
function button(html: string, name: string): string {
    const match = html.match(
        new RegExp(`<button[^>]*aria-label="${name}"[^>]*>`),
    );
    assert.ok(match, `no button named ${name}`);
    return match[0];
}

describe("SimTransport", () => {
    /** The run's behaviour switches sit together at the row's far end: gates, resume, media. */
    it("ends the transport row with the three run toggles in order", () => {
        const html = render(state({}), true);
        const gates = html.indexOf('aria-label="Break on gates"');
        const resume = html.indexOf('aria-label="Resume after an answer"');
        const media = html.indexOf(
            'aria-label="Speech and movies finish on their own"',
        );

        assert.ok(gates >= 0 && gates < resume && resume < media);
    });

    it("shows the resume toggle as the viewer set it", () => {
        assert.match(
            button(render(state({}), true), "Resume after an answer"),
            /aria-pressed="true"/,
        );
        assert.match(
            button(render(state({}), false), "Resume after an answer"),
            /aria-pressed="false"/,
        );
    });

    it("shows the media toggle as the session holds it", () => {
        const name = "Speech and movies finish on their own";
        assert.match(
            button(render(state({}), true), name),
            /aria-pressed="true"/,
        );
        assert.match(
            button(render(state({ assumeMediaCompletes: false }), true), name),
            /aria-pressed="false"/,
        );
    });
});
