// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The type's damage smoke - `Damaged_Smoke_Asset_Name` - the way the game adds it.
//
// Measured in the game, on the hull's direct-damage path:
//
// - A hit adds the smoke when it takes the hull from above 75 % of its maximum to 75 % or below, or
//   from above 50 % to 50 % or below, and the unit still lives. The two lines are ONE test, so a hit
//   through both adds one smoke, not two.
// - The smoke is the asset from the game's model folder, added on the model's root bone. An empty
//   name adds nothing.
// - Nothing on that path takes it away again or remembers it was added: a unit repaired above a line
//   gains another when it falls through it again, and a repair removes none.
// - When the unit dies it is destroyed, and the wreck - or the hull spinning away - is a NEW object
//   made from its type, so it carries none of the smokes a hit added.
//
// Inferred, not measured: that the destroyed object's smokes leave the scene with it at once, and
// that nothing else removes them while the unit lives.

/** The fractions of the maximum hull a hit has to fall through. */
const SMOKE_LINES = [0.75, 0.5] as const;

/** Whether a hit taking the hull from `before` to `after` adds the damage smoke. */
export function smokeDue(before: number, after: number, max: number): boolean {
    if (!(max > 0) || !(after > 0)) {
        return false;
    }

    return SMOKE_LINES.some(
        (line) => max * line < before && after <= max * line,
    );
}

/**
 * Follows the hull hit by hit and says when the type's damage smoke is added.
 *
 * The first hull it sees is where it starts, never a hit: a lens opening on a unit is not damage.
 */
export class DamageSmoke {
    private last: number | null = null;
    private added = 0;
    private readonly live: string[] = [];

    constructor(private readonly asset: string | null | undefined) {}

    /** The id of a smoke to add as the hull moves to `hull`, or null for none. */
    observe(hull: number, max: number): string | null {
        const before = this.last;
        this.last = hull;

        if (before === null || (this.asset ?? "") === "") {
            return null;
        }

        if (!smokeDue(before, hull, max)) {
            return null;
        }

        const id = `damagedSmoke:${++this.added}`;
        this.live.push(id);
        return id;
    }

    /** Every smoke added and not yet dropped, handed over once - for the death to take away. */
    drop(): string[] {
        return this.live.splice(0, this.live.length);
    }
}
