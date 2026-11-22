// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import {
    UnitStateControls,
    type UnitStateControlsProps,
} from "./UnitStateControls";

/** A unit as the stage shows it, by which of the game's states its behaviours let it be put in. */
function unit(
    combat: Partial<UnitStateControlsProps["combat"]>,
    kind = "Object",
): string {
    const props = {
        combat: {
            movedByLocomotor: true,
            stunnable: true,
            speed: 1,
            ionStunned: false,
            enginesDisabled: false,
            setSpeed: () => undefined,
            setIonStunned: () => undefined,
            setEnginesDisabled: () => undefined,
            ...combat,
        },
        scene: { kind },
    } as unknown as UnitStateControlsProps;

    return renderToStaticMarkup(<UnitStateControls {...props} />);
}

/** The opening tag of the button with this title. */
function button(html: string, title: string): string {
    const tag = html
        .match(/<button[^>]*>/g)
        ?.find((candidate) => candidate.includes(`title="${title}"`));
    assert.ok(tag, `no button titled ${title} in ${html}`);

    return tag;
}

describe("UnitStateControls", () => {
    it("offers the speed and both states for a ship", () => {
        const html = unit({ speed: 0.5 });

        assert.match(html, /aria-label="Unit state"/);
        assert.match(
            html,
            /title="Set speed - the engine effects dim with it"/,
        );
        assert.match(html, /<input[^>]*type="range"[^>]*value="50"/);
        assert.match(html, /50 %/);
        button(html, "Ion stun");
        button(html, "Engines disabled");
        assert.equal(
            /<(button|input)[^>]* disabled=""/.test(html),
            false,
            html,
        );
    });

    it("opens at full speed", () => {
        const html = unit({});

        assert.match(html, /<input[^>]*type="range"[^>]*value="100"/);
        assert.match(html, /100 %/);
    });

    it("shows a state that is on as pressed", () => {
        const html = unit({ ionStunned: true });

        assert.match(button(html, "Ion stun"), /aria-pressed="true"/);
        assert.match(button(html, "Ion stun"), /class="[^"]*\bactive\b/);
        assert.match(button(html, "Engines disabled"), /aria-pressed="false"/);
    });

    it("disables the speed and the engines for a unit the space locomotor does not move, and says why", () => {
        const html = unit({ movedByLocomotor: false });

        assert.match(html, /<input[^>]*type="range"[^>]* disabled=""/);
        assert.match(
            html,
            /<label[^>]*title="No SIMPLE_SPACE_LOCOMOTOR behaviour"/,
        );
        assert.match(
            button(html, "No SIMPLE_SPACE_LOCOMOTOR behaviour"),
            / disabled=""/,
        );
        assert.equal(/ disabled=""/.test(button(html, "Ion stun")), false);
    });

    it("disables the stun for a unit without the behaviour, and says why", () => {
        const html = unit({ stunnable: false });

        assert.match(
            button(html, "No ION_STUN_EFFECT behaviour"),
            / disabled=""/,
        );
        assert.equal(
            / disabled=""/.test(button(html, "Engines disabled")),
            false,
        );
    });

    it("leaves all three out for a model opened as a file", () => {
        assert.equal(unit({}, "Model"), "");
    });

    it("leaves all three out while no scene is loaded", () => {
        const props = {
            combat: {},
            scene: null,
        } as unknown as UnitStateControlsProps;

        assert.equal(
            renderToStaticMarkup(<UnitStateControls {...props} />),
            "",
        );
    });
});
