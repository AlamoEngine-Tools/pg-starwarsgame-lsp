// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import {
    type DialAction,
    type RotaryMode,
    RotaryModeSwitch,
} from "./RotaryModeSwitch";

const noop = (): void => undefined;

const MODES: RotaryMode<"view" | "edit">[] = [
    { id: "view", label: "View", icon: "viewMode", angle: 240 },
    { id: "edit", label: "Edit", icon: "editMode", angle: 300 },
];

const reset: DialAction = {
    id: "reset",
    icon: "sceneReset",
    title: "Reset the scene",
    slot: "start",
    onPress: noop,
};

function render(actions?: readonly DialAction[]): string {
    return renderToStaticMarkup(
        <RotaryModeSwitch
            mode="view"
            modes={MODES}
            actions={actions}
            onSelect={noop}
        />,
    );
}

describe("RotaryModeSwitch", () => {
    it("draws a position per mode and lights the one in use", () => {
        const html = render();

        assert.equal(html.match(/class="rotary-pos/g)?.length, 2);
        assert.match(html, /class="rotary-pos active"[^>]*title="View"/);
        assert.match(html, /class="rotary-pos"[^>]*title="Edit"/);
    });

    it("draws no action where the page names none", () => {
        assert.equal(render().includes("rotary-action"), false);
        assert.equal(render([]).includes("rotary-action"), false);
    });

    it("draws a mode-wide action beside the centre, named by its tooltip", () => {
        const html = render([reset]);

        assert.match(
            html,
            /<button type="button" class="rotary-action at-start" title="Reset the scene" aria-label="Reset the scene">/,
        );
    });

    /**
     * An action keeps its slot whatever else is on the dial, so it is in the same spot in every
     * mode that has it.
     */
    it("puts an action in the slot it names, whatever order the page lists them in", () => {
        const html = render([
            { ...reset, id: "b", title: "B", slot: "end-2" },
            { ...reset, id: "a", title: "A", slot: "end" },
            reset,
        ]);

        assert.match(
            html,
            /class="rotary-action at-start" title="Reset the scene"/,
        );
        assert.match(html, /class="rotary-action at-end" title="A"/);
        assert.match(html, /class="rotary-action at-end-2" title="B"/);
    });

    it("says why an action cannot be pressed while it cannot", () => {
        const html = render([
            {
                ...reset,
                disabled: true,
                disabledReason: "Nothing is destroyed",
            },
        ]);

        assert.match(
            html,
            /class="rotary-action at-start" title="Nothing is destroyed" aria-label="Nothing is destroyed" disabled=""/,
        );
    });
});
