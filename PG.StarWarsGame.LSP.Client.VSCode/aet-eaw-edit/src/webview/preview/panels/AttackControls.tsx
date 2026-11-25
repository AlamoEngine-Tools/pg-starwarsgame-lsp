// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Fire, Repair, the weapon settings and the damage log, on the stage bottom edge in Gameplay.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { type PreviewMode } from "../previewMode";

export interface AttackControlsProps {
    attackOpen: boolean;
    /** The pill that opens the weapon settings, for the flyout to open at. */
    attackPillRef: RefObject<HTMLButtonElement | null>;
    combat: CombatLens;
    mode: PreviewMode;
    scene: PreviewScene | null;
    setAttackOpen: Dispatch<SetStateAction<boolean>>;
}

export function AttackControls({
    attackOpen,
    attackPillRef,
    combat,
    mode,
    scene,
    setAttackOpen,
}: AttackControlsProps): React.JSX.Element {
    return (
        <>
            {/* The attack tool. A LENS, like the command bar beside it: absent
                            outside Gameplay rather than disabled, because its absence says which
                            lens you are in and not something about this model.

                            A button rather than the form itself - ten fields laid along the bottom
                            edge would take a third of the viewport permanently, to show a tool that
                            is used in bursts. */}
            {mode === "gameplay" &&
                scene?.defence !== null &&
                scene?.defence !== undefined && (
                    <div className="stage-chrome">
                        {/* The two ACTIONS, on the edge rather than inside the flyout -
                                    the same arrangement the scene controls and the camera presets
                                    use. `as-action`, not the plain toolbar glyph: neither ever
                                    latches, and a bare icon beside controls that do reads as a
                                    switch that happens to be off. Firing is something you do repeatedly while watching the
                                    model; having to open a panel first put a lid on the one
                                    control that gets pressed most. The flyout keeps the SETTINGS,
                                    and lives in a band of its own beside this one.

                                    A unit WITH hardpoints cannot be targeted itself, so with none
                                    picked there is nothing for a shot to land on: disabled with
                                    the reason, rather than firing into nothing. The title also
                                    names the target, which the flyout's own readout used to be
                                    the only place to see. */}
                        <IconButton
                            icon="fire"
                            className={
                                "as-action" + (combat.justFired ? " fired" : "")
                            }
                            title={l10n.t(
                                "Fire at {0}",
                                combat.fireTarget === "hull"
                                    ? "the whole unit"
                                    : combat.fireTarget,
                            )}
                            disabled={
                                combat.fireTarget === "hull" &&
                                !combat.shipTargetable
                            }
                            disabledReason={l10n.t(
                                "Fire - pick a hardpoint to fire at",
                            )}
                            onClick={combat.fire}
                        />
                        <IconButton
                            icon="repair"
                            className="as-action"
                            title={l10n.t("Repair the target")}
                            onClick={() => {
                                combat.repairTarget();
                                combat.destroyAll(false, false);
                            }}
                        />
                    </div>
                )}

            {/* Its own band, the way the camera corner does it: Fire and Repair ACT on
                            the model, this OPENS something, and the two are not the same kind of
                            control. Last on the right, so the stage reads outwards from the model
                            in both directions. */}
            {mode === "gameplay" &&
                scene?.defence !== null &&
                scene?.defence !== undefined && (
                    <div className="stage-chrome">
                        <IconButton
                            icon="settings"
                            className={attackOpen ? "active" : undefined}
                            expanded={attackOpen}
                            title={l10n.t("Weapon settings")}
                            ref={attackPillRef}
                            onClick={() => setAttackOpen((open) => !open)}
                        />

                        {/* Beside the settings, because it is the same kind of control -
                                    it OPENS something. What each shot actually did, so a reader
                                    can see WHY a number came out the way it did rather than
                                    watching a bar move and guessing. */}
                        <IconButton
                            icon="log"
                            className={combat.logOpen ? "active" : undefined}
                            expanded={combat.logOpen}
                            title={l10n.t("Damage log")}
                            ref={combat.logPillRef}
                            disabled={combat.damageLog.length === 0}
                            // Names the control even when it is disabled: a tooltip that
                            // gives only the reason leaves a reader who has never opened it
                            // no idea what it is.
                            disabledReason={l10n.t(
                                "Damage log - nothing fired yet",
                            )}
                            onClick={() => combat.setLogOpen((open) => !open)}
                        />
                    </div>
                )}
        </>
    );
}
