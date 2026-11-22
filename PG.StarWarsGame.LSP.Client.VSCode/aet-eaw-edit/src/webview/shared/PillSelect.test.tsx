// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { type Enablement } from "./Button";
import { PillSelect } from "./PillSelect";

const LEVELS = [
    { value: 0, label: "0 (12 meshes, 340 tris)" },
    { value: 2, label: "2 (12 meshes, 1,882 tris)" },
];

function pill(props: {
    value?: number;
    disabled?: boolean;
    className?: string;
}): string {
    const enablement: Enablement =
        props.disabled === undefined
            ? {}
            : { disabled: props.disabled, disabledReason: "Follows the hull" };

    return renderToStaticMarkup(
        <PillSelect
            icon="detail"
            value={props.value ?? 2}
            options={LEVELS}
            title="LOD 2 (12 meshes, 1,882 tris)"
            className={props.className}
            {...enablement}
            onPick={() => undefined}
        />,
    );
}

describe("PillSelect", () => {
    it("shows the value alone in the pill and the long form in the list", () => {
        const html = pill({});

        assert.match(html, /<span class="pill-value">2<\/span>/);
        assert.match(
            html,
            /<option value="0">0 \(12 meshes, 340 tris\)<\/option>/,
        );
        assert.match(
            html,
            /<option value="2" selected="">2 \(12 meshes, 1,882 tris\)<\/option>/,
        );
    });

    it("says what it holds as a tooltip, and names the list by it", () => {
        const html = pill({});

        assert.match(
            html,
            /<label class="pill-select" title="LOD 2 \(12 meshes, 1,882 tris\)"/,
        );
        assert.match(
            html,
            /<select[^>]*aria-label="LOD 2 \(12 meshes, 1,882 tris\)"/,
        );
    });

    it("draws the glyph for what it picks and the mark of a list", () => {
        const html = pill({});

        assert.equal(html.match(/<svg/g)?.length, 2, html);
        assert.match(html, /<span class="pill-mark" aria-hidden="true"><svg/);
    });

    it("takes a class for the host to place it by", () => {
        assert.match(
            pill({ className: "detail" }),
            /class="pill-select detail"/,
        );
    });

    it("stays on screen when it cannot be used, and says why", () => {
        const html = pill({ disabled: true });

        assert.match(
            html,
            /<label class="pill-select disabled" title="Follows the hull"/,
        );
        assert.match(html, /<select[^>]* disabled=""/);
        assert.match(html, /<span class="pill-value">2<\/span>/);
    });

    it("is usable when the reason to disable it does not hold", () => {
        const html = pill({ disabled: false });

        assert.equal(/ disabled=""/.test(html), false, html);
        assert.match(html, /title="LOD 2 \(12 meshes, 1,882 tris\)"/);
    });

    it("shows a value the list does not hold as it is", () => {
        assert.match(pill({ value: 5 }), /<span class="pill-value">5<\/span>/);
    });
});
