// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The ability command bar: one button per ability, in the game's own arrangement.

import type { Dispatch, SetStateAction } from "react";
import type { Abilities } from "../useAbilities";
import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { safeImageSource } from "../../shared/safeImage";
import { type PreviewMode } from "../previewMode";
import { poolRows } from "../attacker";
import { abilityBarTitle } from "../abilityRows";

export interface AbilityCommandBarProps {
    abilitiesOpen: boolean;
    abilityState: Abilities;
    combat: CombatLens;
    mode: PreviewMode;
    scene: PreviewScene | null;
    setAbilitiesOpen: Dispatch<SetStateAction<boolean>>;
}

export function AbilityCommandBar({
    abilitiesOpen,
    abilityState,
    combat,
    mode,
    scene,
    setAbilitiesOpen,
}: AbilityCommandBarProps): React.JSX.Element {
    return (
        <>
            {/* The ability command bar - the game's own arrangement, and the reason
                            the icons were worth resolving at all. One key per ability, pressed to
                            activate, centred under the model it acts on.

                            A LENS: absent outside Gameplay, which is a statement about which lens
                            you are in rather than about this model, and the one case the
                            disable-don't-hide rule exempts.

                            Every ability gets a key, including the ones that drive nothing - most
                            of the shipped types are ORDERS, and a unit's repertoire is worth
                            reading whole. Those keys are DISABLED and say why in their tooltip
                            rather than being left out. */}
            <span className="command-stack">
                {mode === "gameplay" && abilityState.abilities.length > 0 && (
                    <div
                        className="stage-chrome ability-bar"
                        role="group"
                        aria-label={l10n.t("Abilities")}
                    >
                        {abilityState.abilities.map((ability) => (
                            <button
                                key={ability.type}
                                type="button"
                                className={
                                    "ability-key" +
                                    (abilityState.activeAbilities.has(
                                        ability.type,
                                    )
                                        ? " active"
                                        : "")
                                }
                                aria-pressed={abilityState.activeAbilities.has(
                                    ability.type,
                                )}
                                aria-label={ability.label}
                                disabled={!ability.drivesSomething}
                                title={abilityBarTitle(ability)}
                                onClick={() =>
                                    abilityState.setAbilityActive(
                                        ability.type,
                                        !abilityState.activeAbilities.has(
                                            ability.type,
                                        ),
                                    )
                                }
                            >
                                {/* The icon IS the key. Where none resolved the label
                                            stands in rather than an empty slot: the engine
                                            hardcodes these names, so a miss usually means we
                                            cannot guess the name, not that the game draws
                                            nothing. */}
                                {ability.iconDataUri !== null ? (
                                    <img
                                        className="ability-icon"
                                        src={safeImageSource(
                                            ability.iconDataUri,
                                        )}
                                        alt=""
                                    />
                                ) : (
                                    <span className="ability-key-text">
                                        {ability.label}
                                    </span>
                                )}
                            </button>
                        ))}

                        {/* The reading matter the dock list used to carry. A bar of icons
                                    cannot hold a tooltip's worth of text per ability, and the jump
                                    to a definition needs a control of its own - so both live one
                                    press away instead of being dropped. */}
                        <IconButton
                            icon="details"
                            className={abilitiesOpen ? "active" : undefined}
                            expanded={abilitiesOpen}
                            title={l10n.t("Show abilities")}
                            onClick={() => setAbilitiesOpen((open) => !open)}
                        />
                    </div>
                )}

                {/* What the unit has LEFT, under the keys that act on it - the user's own
                            arrangement. The same three pools the attacker's readout lists, said as
                            bars: shields blue, the hull on the hardpoints' own ramp so a bar and a
                            targeting mark agree, and energy a hue neither of them uses.

                            Energy is absent unless its setting is on - see `poolOptions`. */}
                {mode === "gameplay" &&
                    poolRows(
                        scene?.defence,
                        combat.livePools,
                        combat.hull,
                        combat.poolOptions,
                        combat.hardpointHealthPool,
                    ).length > 0 && (
                        <div
                            className="stage-chrome status-bars"
                            role="group"
                            aria-label={l10n.t("What the unit has left")}
                        >
                            {poolRows(
                                scene?.defence,
                                combat.livePools,
                                combat.hull,
                                combat.poolOptions,
                                combat.hardpointHealthPool,
                            ).map((row) => (
                                <span
                                    key={row.id}
                                    className="status-bar"
                                    title={`${row.label}: ${row.detail}`}
                                >
                                    <span className="status-bar-track">
                                        <span
                                            className="status-bar-fill"
                                            style={{
                                                width: `${Math.round(row.fraction * 100)}%`,
                                                background: row.colour,
                                            }}
                                        />
                                    </span>
                                </span>
                            ))}
                        </div>
                    )}
            </span>
        </>
    );
}
