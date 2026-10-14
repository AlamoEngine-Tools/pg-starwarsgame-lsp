// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { panelKey, projectPanelKey } from "./storyGraphTarget";

/**
 * A story graph tab is filed under its project when the window holds several (#230): two projects
 * may each have a campaign of one name, and those are two campaigns on two servers.
 */
describe("projectPanelKey", () => {
    const target = { campaign: "Rebel_Story", faction: "Rebel" };

    it("is the plain key while the window holds one project", () => {
        assert.equal(projectPanelKey("", target), panelKey(target));
    });

    it("keeps two projects' campaigns of one name apart", () => {
        assert.notEqual(
            projectPanelKey("c:/dev/eawx/rev/rev.pgproj", target),
            projectPanelKey("c:/dev/eawx/fotr/fotr.pgproj", target),
        );
    });
});
