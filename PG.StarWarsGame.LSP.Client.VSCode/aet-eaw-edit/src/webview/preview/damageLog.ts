// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What each shot actually did, in the order it happened.
//
// The user asked for it in the shape an old-school RPG reports a hit: "X did Y amount of damage to Z
// (Armour_Type)". That last bracket is the point of it - the armour is what SCALED the number, and a
// panel that shows a pool going down without saying which factor applied leaves the one question a
// modder is actually asking unanswered.
//
// The numbers here are MEASURED, not recomputed: the panel logs the difference a shot made to a pool
// or a hardpoint's health. Recomputing them would be a second implementation of the damage rules
// able to disagree with the one that did the work.

import * as l10n from "@vscode/l10n";

/** How a pool is named in a line. The hull is the default and goes unsaid. */
export type DamagePool = "hull" | "shield" | "energy";

/** One thing one shot did to one target. */
export interface DamageLogEntry {
    /** What hit it - the damage type, or the projectile where one was picked. */
    source: string;
    /** How much actually landed, after the armour factor. */
    amount: number;
    /** The hardpoint's id, or the unit itself. */
    target: string;
    /** The armour column that scaled it, or null where the target declares none. */
    armor: string | null;
    pool: DamagePool;
    /** This shot finished it off. */
    destroyed: boolean;
    /**
     * What this line records. A shot unless it says otherwise.
     *
     * Repairs belong here for the same reason destroying a hardpoint by hand does: the log is a
     * record of what happened to the unit, not of shots. A reader who repairs mid-session and then
     * reads the numbers back has to be able to see where the pools were refilled.
     */
    kind?: "damage" | "repair";
    /**
     * The arithmetic behind {@link amount}, one line each, or absent where there is none to show.
     *
     * The summary line says what happened; these say WHY, which is the question a reader actually
     * brings to a damage log: which armour factor applied, what the shield took, how a blast was
     * divided, and where each pool stood before and after.
     */
    workings?: readonly string[];
}

/** How many lines the log keeps. */
export const LOG_LIMIT = 200;

/**
 * What a hardpoint destroyed from its own card was killed BY.
 *
 * The switch on a card is not a weapon - it simply decides the hardpoint is gone - so there is no
 * damage type to name and no armour factor that applied. An easter egg the user asked for by name,
 * and one constant to change if it wears thin.
 */
export const BY_HAND = l10n.t("The Force");

/** One line, as the user asked for it. */
export function damageLine(entry: DamageLogEntry): string {
    if (entry.kind === "repair") {
        // No armour column and no pool: nothing was scaled and nothing was defended against. The
        // numbers, where there are any, ride on the workings under the line.
        return Number.isFinite(entry.amount) && entry.amount > 0
            ? l10n.t(
                  "{0} repaired {1} - {2} restored",
                  entry.source,
                  entry.target,
                  round(entry.amount),
              )
            : l10n.t("{0} repaired {1}", entry.source, entry.target);
    }

    // A shot that got through nothing still gets a line. Silence there reads as a broken button:
    // the reader pressed Fire and the panel said nothing at all.
    //
    // INFINITE is said in words. `Infinity` is what the number formats to and reads as a bug, and
    // the symbol is not ASCII - the panel's strings are.
    // The hull is the ordinary case and goes unsaid; the shield stopping a bolt is a different
    // event and worth naming. The clause is one whole message per amount and pool; the armour and
    // the kill wrap the whole line, so no part of a sentence is slotted into another.
    const line = didLine(entry);
    const armored =
        entry.armor === null || entry.armor === ""
            ? line
            : l10n.t("{0} ({1})", line, entry.armor);
    return entry.destroyed ? l10n.t("{0} - destroyed", armored) : armored;
}

function didLine(entry: DamageLogEntry): string {
    const { source, target } = entry;
    if (!Number.isFinite(entry.amount)) {
        switch (entry.pool) {
            case "shield":
                return l10n.t(
                    "{0} did infinite damage to the shield to {1}",
                    source,
                    target,
                );
            case "energy":
                return l10n.t(
                    "{0} did infinite damage to the energy to {1}",
                    source,
                    target,
                );
            default:
                return l10n.t("{0} did infinite damage to {1}", source, target);
        }
    }
    if (entry.amount <= 0) {
        switch (entry.pool) {
            case "shield":
                return l10n.t(
                    "{0} did no damage to the shield to {1}",
                    source,
                    target,
                );
            case "energy":
                return l10n.t(
                    "{0} did no damage to the energy to {1}",
                    source,
                    target,
                );
            default:
                return l10n.t("{0} did no damage to {1}", source, target);
        }
    }
    const amount = round(entry.amount);
    switch (entry.pool) {
        case "shield":
            return l10n.t(
                "{0} did {1} damage to the shield to {2}",
                source,
                amount,
                target,
            );
        case "energy":
            return l10n.t(
                "{0} did {1} damage to the energy to {2}",
                source,
                amount,
                target,
            );
        default:
            return l10n.t("{0} did {1} damage to {2}", source, amount, target);
    }
}

/**
 * The log with one shot's victims added, newest shot first.
 *
 * Within a shot the victims keep the order the blast reached them - nearest out - because that is
 * the order the damage was applied in and reading it backwards would misdescribe a falloff.
 */
export function appendShot(
    log: readonly DamageLogEntry[],
    shot: readonly DamageLogEntry[],
): DamageLogEntry[] {
    return [...shot, ...log].slice(0, LOG_LIMIT);
}

/** The same rounding the pool readout uses: an armor factor of 0.35 leaves a tail nobody wants. */
function round(value: number): string {
    return Number.parseFloat(value.toFixed(1)).toString();
}
