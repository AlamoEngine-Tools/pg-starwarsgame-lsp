// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { type HardpointList, useHardpointList } from "./useHardpointList";

/** What the hardpoint list holds on the first render, through a component that only calls it. */
function firstRender(searchPressed: boolean): HardpointList {
    let seen: HardpointList | undefined;
    const Probe = (): null => {
        seen = useHardpointList({
            cards: [],
            fireTarget: "hull",
            hardpointSearchPressed: searchPressed,
            infoCard: null,
            looseWeapons: [],
            setInfoCard: () => undefined,
        });
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useHardpointList", () => {
    it("opens unfiltered, with nothing folded and no cards to group", () => {
        const list = firstRender(false);

        assert.equal(list.hardpointFilter, "");
        assert.equal(list.foldedGroups.size, 0);
        assert.equal(list.hardpointSearchOpen, false);
        assert.deepEqual(list.hardpointGroups, []);
    });

    it("keeps the search open while it is pressed", () => {
        assert.equal(firstRender(true).hardpointSearchOpen, true);
    });
});
