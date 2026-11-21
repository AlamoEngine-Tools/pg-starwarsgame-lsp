// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { isValidElement, type ReactElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";

import type { ParticleRun, ParticleTransport } from "../particleTransport";
import {
    ParticlePlaybackSection,
    type ParticlePlaybackSectionProps,
} from "./ParticlePlaybackSection";

/** A transport that records which verb each control reached. */
function transport(run: ParticleRun, calls: string[] = []): ParticleTransport {
    return {
        run,
        togglePlay: () => calls.push("togglePlay"),
        stop: () => calls.push("stop"),
        restart: () => calls.push("restart"),
    };
}

function props(
    overrides: Partial<ParticlePlaybackSectionProps>,
): ParticlePlaybackSectionProps {
    return {
        transport: transport("playing"),
        ready: true,
        speed: 1,
        setSpeed: () => undefined,
        ...overrides,
    };
}

/**
 * Every element under `node`: the ones handed down as props (the bar's `controls`, its `speed`)
 * and the ones a function component renders. An icon button is a leaf - its props are what the
 * wiring is.
 */
function elementsUnder(node: unknown): ReactElement[] {
    if (Array.isArray(node)) {
        return node.flatMap(elementsUnder);
    }
    if (!isValidElement(node)) {
        return node !== null && typeof node === "object"
            ? Object.values(node).flatMap(elementsUnder)
            : [];
    }
    const given = node.props as Record<string, unknown>;
    if (given.icon !== undefined) {
        return [node];
    }
    const rendered =
        typeof node.type === "function"
            ? elementsUnder((node.type as (p: unknown) => unknown)(given))
            : [];
    return [node, ...Object.values(given).flatMap(elementsUnder), ...rendered];
}

/** The icon button titled `title`. */
function button(
    node: unknown,
    title: string,
): { onClick?: () => void; disabled?: boolean } {
    const found = elementsUnder(node).find(
        (element) =>
            (element.props as { icon?: string }).icon !== undefined &&
            (element.props as { title?: string }).title === title,
    );
    assert.ok(found, `no button titled ${title}`);
    return found.props as { onClick?: () => void; disabled?: boolean };
}

describe("ParticlePlaybackSection", () => {
    it("lays out pause, stop and restart in the transport bar while running", () => {
        const html = renderToStaticMarkup(
            <ParticlePlaybackSection {...props({})} />,
        );

        assert.match(html, /class="player particle-transport"/);
        const titles = [...html.matchAll(/<button[^>]*title="([^"]*)"/g)].map(
            (m) => m[1],
        );
        assert.deepEqual(titles, ["Pause", "Stop emitting", "Restart"]);
        assert.match(html, /title="Particle speed"/);
        assert.equal(/<button[^>]*disabled/.test(html), false, html);
    });

    it("offers Play while paused and Play from the start while stopped", () => {
        const paused = renderToStaticMarkup(
            <ParticlePlaybackSection
                {...props({ transport: transport("paused") })}
            />,
        );
        const stopped = renderToStaticMarkup(
            <ParticlePlaybackSection
                {...props({ transport: transport("stopped") })}
            />,
        );

        assert.match(paused, /<button[^>]*title="Play"/);
        assert.match(stopped, /<button[^>]*title="Play from the start"/);
        // Stop is spent once it stands, and says so.
        assert.match(
            stopped,
            /<button[^>]*title="Already stopped"[^>]*disabled/,
        );
    });

    it("is disabled and says why until a system has arrived", () => {
        const html = renderToStaticMarkup(
            <ParticlePlaybackSection {...props({ ready: false })} />,
        );

        const disabled = [...html.matchAll(/<button[^>]*disabled/g)];
        assert.equal(disabled.length, 3, html);
        assert.match(html, /title="No particle system loaded"/);
        assert.match(html, /<input[^>]*type="range"[^>]*disabled/);
    });

    it("sends each control to its verb", () => {
        const calls: string[] = [];
        const section = ParticlePlaybackSection(
            props({ transport: transport("playing", calls) }),
        );

        button(section, "Pause").onClick?.();
        button(section, "Stop emitting").onClick?.();
        button(section, "Restart").onClick?.();

        assert.deepEqual(calls, ["togglePlay", "stop", "restart"]);
    });

    it("reads the speed out and sets it from the slider", () => {
        const set: number[] = [];
        const html = renderToStaticMarkup(
            <ParticlePlaybackSection
                {...props({ speed: 0.5, setSpeed: (s) => set.push(s) })}
            />,
        );
        assert.match(html, /0\.50x/);

        const slider = elementsUnder(
            ParticlePlaybackSection(
                props({ speed: 0.5, setSpeed: (s) => set.push(s) }),
            ),
        ).find((element) => element.type === "input");
        assert.ok(slider);
        (
            slider.props as {
                onChange: (e: { target: { value: string } }) => void;
            }
        ).onChange({ target: { value: "2" } });
        assert.deepEqual(set, [2]);
    });
});
