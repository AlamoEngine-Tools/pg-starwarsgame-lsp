// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { PartCard } from "./PartCard";

const noop = (): void => undefined;

describe("PartCard", () => {
    it("is a head with the name, then the type line, the body and the actions", () => {
        const html = renderToStaticMarkup(
            <PartCard
                name="HP_Laser_01"
                type="HARD_POINT_WEAPON_LASER"
                actions={<button type="button">info</button>}
            >
                <span className="detail">body</span>
            </PartCard>,
        );

        assert.equal(
            html,
            '<li class="hardpoint-card">' +
                '<div class="card-head"><span class="part-name">HP_Laser_01</span></div>' +
                '<span class="detail card-type">HARD_POINT_WEAPON_LASER</span>' +
                '<span class="detail">body</span>' +
                '<span class="view-row card-actions"><button type="button">info</button></span>' +
                "</li>",
        );
    });

    it("leaves out the type line and the actions when there are none", () => {
        assert.equal(
            renderToStaticMarkup(<PartCard name="Spins away" type={null} />),
            '<li class="hardpoint-card"><div class="card-head"><span class="part-name">Spins away</span></div></li>',
        );
    });

    it("marks the variant, the pick and a destroyed part, in that order", () => {
        const html = renderToStaticMarkup(
            <PartCard name="HP" variant="clone-card" selected gone />,
        );

        assert.match(
            html,
            /^<li class="hardpoint-card clone-card selected gone">/,
        );
    });

    it("leads the head with its mark and ends it with the jump to the definition", () => {
        const html = renderToStaticMarkup(
            <PartCard
                name="HP"
                lead={<img className="card-reticle" alt="" />}
                definition={{ title: "Go to where HP is defined", onGo: noop }}
            />,
        );

        assert.match(
            html,
            /<div class="card-head"><img class="card-reticle" alt=""\/><span class="part-name">HP<\/span><button type="button" class="goto-definition" title="Go to where HP is defined">/,
        );
    });

    /** Disable, don't hide: a part that names no definition keeps the jump, disabled. */
    it("keeps the jump disabled where there is nothing to jump to", () => {
        const html = renderToStaticMarkup(
            <PartCard
                name="HP"
                definition={{
                    title: "No definition",
                    disabled: true,
                    onGo: noop,
                }}
            />,
        );

        assert.match(
            html,
            /<button type="button" class="goto-definition" disabled="" title="No definition">/,
        );
    });

    it("carries its data attributes and title onto the card", () => {
        const html = renderToStaticMarkup(
            <PartCard name="HP" data={{ card: "HP_01" }} title="HP_01" />,
        );

        assert.match(
            html,
            /^<li class="hardpoint-card" data-card="HP_01" title="HP_01">/,
        );
    });
});
