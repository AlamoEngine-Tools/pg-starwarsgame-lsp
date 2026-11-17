// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { type Flyouts, useFlyouts } from "./useFlyouts";

/** What the flyouts hold on the first render, through a component that only calls it. */
function firstRender(): Flyouts {
    let seen: Flyouts | undefined;
    const Probe = (): null => {
        seen = useFlyouts({
            clearSelection: () => undefined,
            detailsRow: null,
            selected: new Set(),
        });
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useFlyouts", () => {
    it("opens with every flyout and search closed and nothing placed", () => {
        const flyouts = firstRender();

        assert.equal(flyouts.worldOpen, false);
        assert.equal(flyouts.cameraOpen, false);
        assert.equal(flyouts.abilitiesOpen, false);
        assert.equal(flyouts.attackOpen, false);
        assert.equal(flyouts.infoOpen, false);
        assert.equal(flyouts.searchPressed, false);
        assert.equal(flyouts.hardpointSearchPressed, false);
        assert.equal(flyouts.infoCard, null);
        assert.equal(flyouts.cardInfoAt, null);
        assert.equal(flyouts.infoAbility, null);
        assert.equal(flyouts.abilityInfoAt, null);
    });
});
