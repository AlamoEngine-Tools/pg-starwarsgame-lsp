// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { type Abilities, useAbilities } from "./useAbilities";

/** What the abilities hold on the first render, through a component that only calls it. */
function firstRender(): Abilities {
    let seen: Abilities | undefined;
    const Probe = (): null => {
        seen = useAbilities({
            scene: null,
            setAnimation: () => undefined,
            viewportRef: { current: null },
        });
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useAbilities", () => {
    it("has no abilities and none switched on without a scene", () => {
        const abilities = firstRender();

        assert.deepEqual(abilities.abilities, []);
        assert.equal(abilities.activeAbilities.size, 0);
        assert.equal(abilities.abilityProxyIds.size, 0);
        assert.equal(
            abilities.abilityProxyIdsRef.current,
            abilities.abilityProxyIds,
        );
    });
});
