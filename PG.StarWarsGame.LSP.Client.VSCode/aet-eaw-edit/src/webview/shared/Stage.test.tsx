// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { StageFlyout, StageSlots } from "./Stage";

const noop = (): void => undefined;

describe("StageSlots", () => {
    it("lays an edge out as three slots, start, middle and end", () => {
        const html = renderToStaticMarkup(
            <StageSlots
                edge="top"
                start={<i>a</i>}
                middle={<i>b</i>}
                end={<i>c</i>}
            />,
        );

        assert.equal(
            html,
            '<div class="stage-row stage-top">' +
                '<span class="stage-slot"><i>a</i></span>' +
                '<span class="stage-slot stage-slot-mid"><i>b</i></span>' +
                '<span class="stage-slot stage-slot-end"><i>c</i></span>' +
                "</div>",
        );
    });

    /** Two equal side slots are the whole of what centres the middle one, so an empty side stays. */
    it("keeps an empty slot, so the middle stays centred", () => {
        const html = renderToStaticMarkup(
            <StageSlots edge="bottom" middle={<i>b</i>} />,
        );

        assert.equal(
            html,
            '<div class="stage-row stage-bottom">' +
                '<span class="stage-slot"></span>' +
                '<span class="stage-slot stage-slot-mid"><i>b</i></span>' +
                '<span class="stage-slot stage-slot-end"></span>' +
                "</div>",
        );
    });
});

describe("StageFlyout", () => {
    it("is a dialog named by its title, with a head and a body", () => {
        const html = renderToStaticMarkup(
            <StageFlyout title="Camera" at="right" onClose={noop}>
                <p>body</p>
            </StageFlyout>,
        );

        assert.match(
            html,
            /^<div class="stage-flyout on-right" role="dialog" aria-label="Camera">/,
        );
        assert.match(
            html,
            /<div class="stage-flyout-head">Camera<button[^>]*aria-label="Close"[^>]*>/,
        );
        assert.match(
            html,
            /<div class="stage-flyout-body"><p>body<\/p><\/div>/,
        );
    });

    /** Opening upwards from the bottom edge, where its button is - and any variant after that. */
    it("opens from the bottom and carries a variant after its corner", () => {
        const html = renderToStaticMarkup(
            <StageFlyout
                title="Damage log"
                at="right"
                fromBottom
                className="sizeable"
                onClose={noop}
            >
                {null}
            </StageFlyout>,
        );

        assert.match(
            html,
            /^<div class="stage-flyout from-bottom on-right sizeable"/,
        );
    });

    /** The count claims the head's free space, then the tools, then the close at the far end. */
    it("orders the head: title, count, tools, close", () => {
        const html = renderToStaticMarkup(
            <StageFlyout
                title="Abilities"
                at="mid"
                fromBottom
                count={3}
                tools={<button type="button">reset</button>}
                onClose={noop}
            >
                {null}
            </StageFlyout>,
        );

        assert.match(
            html,
            /<div class="stage-flyout-head">Abilities<span class="section-count header-right">3<\/span><button type="button">reset<\/button><button[^>]*aria-label="Close"/,
        );
    });

    /** Resize edges sit on the box itself, before the head, so they span the whole flyout. */
    it("puts its edges before the head", () => {
        const html = renderToStaticMarkup(
            <StageFlyout
                title="Damage log"
                at="right"
                edges={<div className="resize-handle-n" />}
                style={{ height: 200 }}
                onClose={noop}
            >
                {null}
            </StageFlyout>,
        );

        assert.match(
            html,
            /style="height:200px"><div class="resize-handle-n"><\/div><div class="stage-flyout-head">/,
        );
    });
});
