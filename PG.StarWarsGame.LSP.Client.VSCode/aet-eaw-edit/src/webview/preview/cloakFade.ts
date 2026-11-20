// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A model's whole look - its tint and its alpha - and the cloak that moves it.
//
// Measured in the game: a unit with a stealth or Force cloak ability moves a value on the game's
// spring - the death fade's, see `spring.ts` - from 0 towards 1 as the cloak comes in and from where
// it is towards 0 as it goes, over one transition time both ways: `Stealth_Transition_Time`, or
// `Force_Cloak_Transition_In_Secs`. Its look is white mixed towards the ability block's colour by that
// value on all four channels, multiplied into the model's light scale - so the alpha is
// 1 + (colour alpha - 1) x value - and only while the value is above 0.01. The look reaches the
// meshes and the attached models through the light scale the materials multiply by, and the effects
// through their colour (UNMEASURED: whether the game scales an effect's colour at all).
//
// The shadow volumes are off while the cloak runs. A Force cloak gives them back once its value is
// back within 0.01 of 0; stealth does not give them back after decloaking, until the model is
// refreshed or its detail level changes. That is measured, not a preview fault.

import { SteppedSpring } from "./spring";

/** A colour and an alpha, each 0 to 1. */
export interface Look {
    r: number;
    g: number;
    b: number;
    a: number;
}

/** The look of a model nothing has tinted or faded. */
export const PLAIN_LOOK: Readonly<Look> = { r: 1, g: 1, b: 1, a: 1 };

/** Which cloak: they differ in when the shadow volumes come back. */
export type CloakKind = "stealth" | "forceCloak";

/** Where a cloak takes the look, and how long it takes either way. */
export interface CloakLook {
    colour: Look;
    seconds: number;
}

/** What an ability block writes about one cloak: its colour, 0 to 255, and its transition time. */
export interface CloakTags {
    color?: { r: number; g: number; b: number; a: number } | null;
    transitionSeconds?: number | null;
}

/** The value at or below which a cloak shows nothing at all, and its volumes may come back. */
const CUT_OFF = 0.01;

/**
 * A cloak's look from its block's colour and transition time.
 *
 * Measured: white opaque and a transition of 0 where the block writes nothing - which neither tints
 * nor fades. Inferred: the colour's 0-255 channels read as fractions of 255.
 */
export function cloakLookOf(
    colour: { r: number; g: number; b: number; a: number } | null | undefined,
    seconds: number | null | undefined,
): CloakLook {
    return {
        colour:
            colour === null || colour === undefined
                ? { ...PLAIN_LOOK }
                : {
                      r: colour.r / 255,
                      g: colour.g / 255,
                      b: colour.b / 255,
                      a: colour.a / 255,
                  },
        seconds: seconds ?? 0,
    };
}

/**
 * The look and kind of the cloak that is on, or null when none is: stealth's from its
 * `Stealth_Ability` block, Force cloak's from its `Force_Cloak_Ability` block. The last switched on
 * wins where both are.
 */
export function cloakLookFor(
    active: ReadonlySet<string>,
    cloaks: {
        stealth?: CloakTags | null;
        forceCloak?: CloakTags | null;
    } | null,
): { look: CloakLook; kind: CloakKind } | null {
    const on = [...active]
        .reverse()
        .map((type) => type.trim().toUpperCase())
        .find((type) => type === "STEALTH" || type === "FORCE_CLOAK");

    if (on === undefined) {
        return null;
    }

    const kind: CloakKind = on === "STEALTH" ? "stealth" : "forceCloak";
    const tags = kind === "stealth" ? cloaks?.stealth : cloaks?.forceCloak;

    return { look: cloakLookOf(tags?.color, tags?.transitionSeconds), kind };
}

/** A unit's cloak value, stepped by the viewport, and the look and shadow volumes it decides. */
export class CloakFade {
    private readonly spring = new SteppedSpring(0);
    private hasCloak = false;
    private on = false;
    /** The cloak coming in or last in, kept so the way out has a colour and a time. */
    private look: CloakLook | null = null;
    private kind: CloakKind | null = null;
    /** Whether a stealth has run since the model was last refreshed - see the file comment. */
    private stealthRan = false;

    /** The spring's value: 0 with no cloak, towards 1 while one is on. */
    get value(): number {
        return this.spring.value;
    }

    /** Whether the unit has a stealth or Force cloak ability at all - nothing shows without one. */
    setHasCloak(has: boolean): void {
        this.hasCloak = has;
    }

    /** Brings a cloak in, or - with null - lets the current one go. */
    set(look: CloakLook | null, kind?: CloakKind): void {
        if (look !== null) {
            this.look = look;
            this.kind = kind ?? "stealth";
            this.stealthRan ||= this.kind === "stealth";
        }

        this.on = look !== null;
    }

    /** Moves the spring on by `dt` seconds. */
    step(dt: number): void {
        this.spring.advance(dt, this.on ? 1 : 0, this.look?.seconds ?? 0);
    }

    /** The model's look now. */
    current(): Look {
        const look = this.look;

        // Inferred: a transition of 0 makes the game's value invalid, and it is rejected - so the
        // look never shows. No shipped block writes one.
        if (
            !this.hasCloak ||
            look === null ||
            !(look.seconds > 0) ||
            this.value <= CUT_OFF
        ) {
            return { ...PLAIN_LOOK };
        }

        const mix = (to: number): number => 1 + (to - 1) * this.value;

        return {
            r: mix(look.colour.r),
            g: mix(look.colour.g),
            b: mix(look.colour.b),
            a: mix(look.colour.a),
        };
    }

    /** Whether the model's shadow volumes are off - see the file comment. */
    volumesOff(): boolean {
        if (this.on || this.stealthRan) {
            return true;
        }

        return this.kind === "forceCloak" && this.value > CUT_OFF;
    }

    /** The model refreshed or its detail level changed: a past stealth gives the volumes back. */
    releaseVolumes(): void {
        this.stealthRan = this.on && this.kind === "stealth";
    }

    /** Back to no cloak at once, for a new model. */
    reset(): void {
        this.spring.advance(0, 0, 0);
        this.on = false;
        this.look = null;
        this.kind = null;
        this.stealthRan = false;
    }
}
