// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { CloakFade, PLAIN_LOOK, cloakLookFor, cloakLookOf } from "./cloakFade";

function near(
    actual: number,
    expected: number,
    what: string,
    within = 0.005,
): void {
    assert.ok(
        Math.abs(actual - expected) <= within,
        `${what}: ${actual}, expected ${expected}`,
    );
}

/** Twi'lek stealth, as FoC writes it: 255, 64, 64, 120 over 0.6 s. */
const TWILEK = cloakLookOf({ r: 255, g: 64, b: 64, a: 120 }, 0.6);

/** A fade for a unit that has a cloak, the given one coming in. */
function cloaking(
    look = TWILEK,
    kind: "stealth" | "forceCloak" = "stealth",
): CloakFade {
    const fade = new CloakFade();
    fade.setHasCloak(true);
    fade.set(look, kind);
    return fade;
}

describe("cloakLookOf", () => {
    it("reads the colour off 0-255 channels", () => {
        near(TWILEK.colour.g, 64 / 255, "g", 1e-9);
        near(TWILEK.colour.a, 120 / 255, "a", 1e-9);
        assert.equal(TWILEK.seconds, 0.6);
    });

    /** Measured: white opaque and a transition of 0 where the block writes nothing. */
    it("takes white opaque and no transition where the tags are not written", () => {
        const unwritten = cloakLookOf(null, null);

        assert.deepEqual(unwritten.colour, PLAIN_LOOK);
        assert.equal(unwritten.seconds, 0);
    });
});

describe("CloakFade", () => {
    /**
     * Measured: the cloak's value rises on the game's spring - the death fade's - from 0 towards 1,
     * and the look is white mixed towards the cloak's colour by it, all four channels.
     */
    it("brings the look in along the spring, alpha and tint together", () => {
        const fade = cloaking();

        fade.step(0.3);

        near(fade.value, 0.264, "value at T/2");
        near(fade.current().a, 1 + (120 / 255 - 1) * fade.value, "alpha", 1e-9);
        near(fade.current().g, 1 + (64 / 255 - 1) * fade.value, "green", 1e-9);
        near(fade.current().r, 1, "red", 1e-9);
    });

    it("goes out from where it is, keeping its velocity", () => {
        const fade = cloaking();
        fade.step(0.3);
        const atFlip = fade.value;

        fade.set(null);
        fade.step(1 / 30);
        assert.ok(
            fade.value > atFlip,
            "the way out did not carry the velocity",
        );

        fade.step(5);
        assert.deepEqual(fade.current(), PLAIN_LOOK);
    });

    /** Measured: nothing at all at or below 0.01. */
    it("shows nothing while the value is at or below 0.01", () => {
        const fade = cloaking(cloakLookOf({ r: 255, g: 0, b: 0, a: 0 }, 5));

        fade.step(1 / 30);
        assert.ok(fade.value > 0 && fade.value <= 0.01, `value ${fade.value}`);
        assert.deepEqual(fade.current(), PLAIN_LOOK);

        fade.step(1);
        assert.ok(fade.current().a < 1);
    });

    it("shows nothing for a unit that has no cloak ability", () => {
        const fade = new CloakFade();
        fade.set(TWILEK, "stealth");
        fade.step(1);

        assert.deepEqual(fade.current(), PLAIN_LOOK);
    });

    /** Inferred: a transition of 0 makes the game's value invalid, and it never shows the look. */
    it("shows nothing for a transition of 0", () => {
        const fade = cloaking(cloakLookOf({ r: 255, g: 64, b: 64, a: 120 }, 0));
        fade.step(1);

        assert.deepEqual(fade.current(), PLAIN_LOOK);
    });

    /**
     * Measured: the shadow volumes are off while the cloak runs. A Force cloak gives them back once
     * its fade is within 0.01 of 0; stealth does not give them back at all until the model is
     * refreshed or its detail level changes.
     */
    it("keeps a stealthed unit's shadow volumes off after it decloaks, until it is refreshed", () => {
        const fade = cloaking();
        assert.equal(fade.volumesOff(), true);

        fade.step(1);
        fade.set(null);
        fade.step(10);
        assert.equal(fade.volumesOff(), true, "back after decloaking");

        fade.releaseVolumes();
        assert.equal(fade.volumesOff(), false);
    });

    it("gives a Force cloak's shadow volumes back once its fade is within 0.01 of 0", () => {
        const fade = cloaking(
            cloakLookOf({ r: 255, g: 255, b: 255, a: 255 }, 0.4),
            "forceCloak",
        );
        fade.step(1);
        fade.set(null);
        fade.step(1 / 30);
        assert.equal(fade.volumesOff(), true, "back while still fading");

        fade.step(5);
        assert.equal(fade.volumesOff(), false);
    });

    it("has its shadow volumes on while no cloak has run", () => {
        assert.equal(new CloakFade().volumesOff(), false);
    });
});

describe("cloakLookFor", () => {
    const cloaks = {
        stealth: {
            color: { r: 255, g: 64, b: 64, a: 120 },
            transitionSeconds: 0.6,
        },
        forceCloak: {
            color: { r: 255, g: 255, b: 255, a: 255 },
            transitionSeconds: 0.4,
        },
    };

    it("takes the look and kind of the cloak that is on, and none when no cloak is", () => {
        assert.equal(cloakLookFor(new Set(["TURBO"]), cloaks), null);
        assert.equal(
            cloakLookFor(new Set(["STEALTH"]), cloaks)?.look.seconds,
            0.6,
        );
        assert.equal(
            cloakLookFor(new Set(["STEALTH"]), cloaks)?.kind,
            "stealth",
        );
        assert.equal(
            cloakLookFor(new Set(["FORCE_CLOAK"]), cloaks)?.kind,
            "forceCloak",
        );
    });

    it("takes the unwritten values for a cloak its type writes nothing for", () => {
        assert.deepEqual(
            cloakLookFor(new Set(["STEALTH"]), {})?.look.colour,
            PLAIN_LOOK,
        );
        assert.equal(cloakLookFor(new Set(["STEALTH"]), null)?.look.seconds, 0);
    });
});
