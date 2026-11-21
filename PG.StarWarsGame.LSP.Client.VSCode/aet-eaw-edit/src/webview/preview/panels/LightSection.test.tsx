// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { DEFAULT_LIGHTS } from "../../../shared/viewerSettings";
import { type ShadowTintReach, type StandInBlock } from "../shadowVolumePass";
import { LightSection, type LightSectionProps } from "./LightSection";

/** The Light section with the shadow colour reaching `reach` and the stand-in `blocked`. */
function section(reach: ShadowTintReach, blocked: StandInBlock = null): string {
    const props: LightSectionProps = {
        bearing: {
            from: "from the front",
            height: "above",
            belowGround: false,
        },
        editing: "sun",
        folded: new Set(),
        lights: DEFAULT_LIGHTS,
        setEditing: () => undefined,
        setLight: () => undefined,
        setLights: () => undefined,
        shadowReach: reach,
        standIn: { on: false, set: () => undefined, blocked },
        toggleSection: () => undefined,
    };

    return renderToStaticMarkup(<LightSection {...props} />);
}

/** The shadow colour well, as rendered. */
const shadowWell = (html: string): string => {
    const field = /Shadow colour[\s\S]*?(<input type="color"[^>]*>)/.exec(html);
    assert.ok(field, html);
    return field[1];
};

/** The stand-in toggle, as rendered - the field's button that is not its info badge. */
const standInButton = (html: string): string => {
    const field =
        /Stand-in shadow[\s\S]*?(<button(?![^>]*info-badge)[^>]*>)/.exec(html);
    assert.ok(field, html);
    return field[1];
};

describe("LightSection", () => {
    it("disables the shadow colour with its reason while nothing casts a shadow", () => {
        // #412: the Game renderer on a model without a shadow mesh draws no shadow at all, so the
        // ground has nothing to catch either - the floor being on does not make the tint visible.
        const well = shadowWell(section("uncast"));

        assert.match(well, /disabled=""/);
        assert.match(
            well,
            /title="Nothing casts a shadow: The game draws none without a shadow mesh"/,
        );
    });

    it("keeps the shadow colour live where a shadow is drawn", () => {
        assert.doesNotMatch(shadowWell(section("ground")), /disabled=""/);
    });

    it("disables the stand-in toggle with the reason it cannot act", () => {
        const button = standInButton(section("both", "shadowMesh"));

        assert.match(button, /disabled=""/);
        assert.match(
            button,
            /title="The shadow mesh casts the game&#x27;s own shadow"/,
        );
    });

    it("offers the stand-in toggle, off, where it can act", () => {
        const button = standInButton(section("uncast"));

        assert.doesNotMatch(button, /disabled=""/);
        assert.match(button, /aria-pressed="false"/);
    });
});
