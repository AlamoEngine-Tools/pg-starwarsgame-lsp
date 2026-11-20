// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a cloak writes to the parts of a unit.
//
// Measured in the game: stealth going on hides every part whose name contains BASEMESH and every model
// a hardpoint attaches through `Model_To_Attach`, and shows every part whose name contains STEALTH.
// Force cloak does the same without hiding the attached models. Either one ending shows the BASEMESH
// parts and the attached models again and hides the STEALTH parts - after a Force cloak too. Every
// other part keeps its state. Each is a write of the part's state switch; the last write is what the
// part has.

/** The cloaking ability types, and whether going on hides the attached hardpoint models. */
const CLOAKS: ReadonlyMap<string, { hidesAttached: boolean }> = new Map([
    ["STEALTH", { hidesAttached: true }],
    ["FORCE_CLOAK", { hidesAttached: false }],
]);

/** Whether this ability type is a cloak. */
export function isCloak(abilityType: string): boolean {
    return CLOAKS.has(abilityType.trim().toUpperCase());
}

/**
 * Whether loading the unit's type hides a part of this name - measured: every part whose name
 * contains STEALTH, `buildup` or `charged`, case ignored. A cloak shows the STEALTH parts again.
 *
 * The other two are the BLAST ability's, and a KNOWN GAP: the game shows `buildup` when the blast is
 * applied, then in its service hides `buildup` and `charged`, then hides `buildup` and shows
 * `charged`. That order is measured and its timing is not, so the preview has no writer for it and
 * those parts stay hidden in a unit preview - four in the shipped data, all on the FoC Krayt
 * destroyer's model.
 */
export function hiddenOnTypeLoad(name: string): boolean {
    const lower = name.toLowerCase();

    return (
        lower.includes("stealth") ||
        lower.includes("buildup") ||
        lower.includes("charged")
    );
}

/**
 * What one cloak going on or off writes to a part of this name, or `written` when it writes
 * nothing there.
 *
 * @param attached Whether the part belongs to a model a hardpoint attaches.
 */
export function cloakWrite(
    abilityType: string,
    on: boolean,
    name: string,
    attached: boolean,
    written: boolean | undefined,
): boolean | undefined {
    const cloak = CLOAKS.get(abilityType.trim().toUpperCase());

    if (cloak === undefined) {
        return written;
    }

    const upper = name.toUpperCase();
    let last = written;

    if (upper.includes("BASEMESH")) {
        last = !on;
    }

    if (attached && (cloak.hidesAttached || !on)) {
        last = !on;
    }

    if (upper.includes("STEALTH")) {
        last = on;
    }

    return last;
}

/**
 * Whether a cloak writes a part of this name at all, going on or off - the rows switching it takes
 * back from the reader.
 */
export function cloakTouches(
    abilityType: string,
    name: string,
    attached: boolean,
): boolean {
    return (
        cloakWrite(abilityType, true, name, attached, undefined) !==
            undefined ||
        cloakWrite(abilityType, false, name, attached, undefined) !== undefined
    );
}
