// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import {
    SubjectStateControls,
    type SubjectStateControlsProps,
} from "./SubjectStateControls";

/** A subject with one damage state and one detail level, so only the turret field can show. */
function controls(turrets: {
    turretCount: number;
    turnedTurrets: number;
}): string {
    const props = {
        alt: 0,
        altSteps: null,
        combat: { altDrivenByHull: false },
        levels: { alt: [0], lod: [0] },
        lod: 0,
        lodChosenRef: { current: false },
        lodCost: new Map(),
        lodSteps: null,
        scene: null,
        setAlt: () => undefined,
        setLod: () => undefined,
        onResetTurrets: () => undefined,
        ...turrets,
    } as unknown as SubjectStateControlsProps;

    return renderToStaticMarkup(<SubjectStateControls {...props} />);
}

describe("SubjectStateControls", () => {
    it("offers to put every turned turret back, with the count inside the button", () => {
        const html = controls({ turretCount: 3, turnedTurrets: 2 });

        assert.match(
            html,
            /title="Return every turret to the pose the model authored"/,
        );
        assert.equal(/<button[^>]*disabled/.test(html), false, html);
        assert.match(html, /<button[^>]*>[\s\S]*2[\s\S]*<\/button>/);
    });

    it("is disabled and says why while no turret is turned", () => {
        const html = controls({ turretCount: 3, turnedTurrets: 0 });

        assert.match(html, /<button[^>]*disabled/);
        assert.match(html, /title="No turret is turned"/);
    });

    it("leaves the field out for a subject with no turret that turns", () => {
        const html = controls({ turretCount: 0, turnedTurrets: 0 });

        assert.equal(html.includes("<button"), false, html);
    });
});
