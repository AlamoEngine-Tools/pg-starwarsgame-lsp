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
        combat: { altDrivenByHull: false },
        levels: { alt: [0], lod: [0] },
        lod: 0,
        lodPickedRef: { current: false },
        lodCost: new Map(),
        scene: null,
        setAlt: () => undefined,
        setLod: () => undefined,
        onResetTurrets: () => undefined,
        ...turrets,
    } as unknown as SubjectStateControlsProps;

    return renderToStaticMarkup(<SubjectStateControls {...props} />);
}

/** A subject with three detail levels and three damage stages, shown at the given ones. */
function levels(shown: {
    alt?: number;
    lod?: number;
    altDrivenByHull?: boolean;
}): string {
    const props = {
        alt: shown.alt ?? 0,
        combat: { altDrivenByHull: shown.altDrivenByHull ?? false },
        levels: { alt: [0, 1, 2], lod: [0, 2, 5] },
        lod: shown.lod ?? 5,
        lodPickedRef: { current: false },
        lodCost: new Map([
            [0, { meshes: 12, triangles: 340 }],
            [2, { meshes: 12, triangles: 1507 }],
            [5, { meshes: 14, triangles: 1882 }],
        ]),
        scene: null,
        setAlt: () => undefined,
        setLod: () => undefined,
        onResetTurrets: () => undefined,
        turretCount: 0,
        turnedTurrets: 0,
    } as unknown as SubjectStateControlsProps;

    return renderToStaticMarkup(<SubjectStateControls {...props} />);
}

/** The pill of one axis: everything from its label to the label's end. */
function pillOf(html: string, axis: "damage" | "detail"): string {
    const pill = html.match(
        new RegExp(`<label class="pill-select ${axis}[\\s\\S]*?</label>`),
    )?.[0];
    assert.ok(pill, `no ${axis} pill in ${html}`);

    return pill;
}

describe("SubjectStateControls, the damage state and the detail level", () => {
    it("picks each from a list, not along a slider", () => {
        const html = levels({});

        assert.equal(html.includes('type="range"'), false, html);
        pillOf(html, "damage");
        pillOf(html, "detail");
    });

    it("shows the level alone in the pill", () => {
        const html = levels({ alt: 1, lod: 2 });

        assert.match(
            pillOf(html, "damage"),
            /<span class="pill-value">1<\/span>/,
        );
        assert.match(
            pillOf(html, "detail"),
            /<span class="pill-value">2<\/span>/,
        );
    });

    it("offers the levels the model defines, each with what it draws", () => {
        const detail = pillOf(levels({}), "detail");

        assert.deepEqual(
            [...detail.matchAll(/<option value="(\d+)"[^>]*>([^<]*)</g)].map(
                (option) => [option[1], option[2]],
            ),
            [
                ["0", "0 (12 meshes, 340 tris)"],
                ["2", "2 (12 meshes, 1,507 tris)"],
                ["5", "5 (14 meshes, 1,882 tris)"],
            ],
        );
        assert.match(detail, /<option value="5" selected="">/);
    });

    it("says what the detail level draws in its tooltip, and which way the axis runs", () => {
        assert.match(
            pillOf(levels({ lod: 2 }), "detail"),
            /title="LOD 2 \(12 meshes, 1,507 tris\) - 0 is lowest detail in EaW"/,
        );
    });

    it("names the damage state in its tooltip", () => {
        assert.match(
            pillOf(levels({ alt: 0 }), "damage"),
            /title="Damage state 0 \(undamaged\)"/,
        );
    });

    it("keeps the damage state on screen while the hull drives it, disabled, and says why", () => {
        const damage = pillOf(
            levels({ alt: 1, altDrivenByHull: true }),
            "damage",
        );

        assert.match(
            damage,
            /title="Damage state 1 - follows the hull in Gameplay"/,
        );
        assert.match(damage, /<select[^>]* disabled=""/);
        assert.match(damage, /<span class="pill-value">1<\/span>/);
    });

    it("leaves an axis with one level out", () => {
        const html = controls({ turretCount: 1, turnedTurrets: 0 });

        assert.equal(html.includes("pill-select"), false, html);
    });
});

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
