// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The damage state and detail level the subject is shown at, and the reset of its turrets, on the stage
// bottom edge.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { Icon } from "../../shared/Icon";
import {
    type DefinedLevels,
    type LevelCost,
    levelLabel,
    type LevelSteps,
} from "../levels";

export interface SubjectStateControlsProps {
    alt: number;
    altSteps: LevelSteps;
    combat: CombatLens;
    levels: DefinedLevels;
    lod: number;
    /** Set when the reader moves the detail slider: their pick stands from then on. */
    lodPickedRef: RefObject<boolean>;
    lodCost: Map<number, LevelCost>;
    lodSteps: LevelSteps;
    scene: PreviewScene | null;
    setAlt: Dispatch<SetStateAction<number>>;
    setLod: Dispatch<SetStateAction<number>>;
    /** How many turrets the subject has that turn. None leaves the field out. */
    turretCount: number;
    /** How many of them the reader has turned off their rest. */
    turnedTurrets: number;
    onResetTurrets: () => void;
}

export function SubjectStateControls({
    alt,
    altSteps,
    combat,
    levels,
    lod,
    lodPickedRef,
    lodCost,
    lodSteps,
    scene,
    setAlt,
    setLod,
    turretCount,
    turnedTurrets,
    onResetTurrets,
}: SubjectStateControlsProps): React.JSX.Element {
    return (
        <>
            {/* Which damage state and which detail level the subject is shown at.

                            On the stage rather than in the dock because it is the SUBJECT'S state,
                            and the bottom edge is what the subject is doing.

                            SHOWN IN BOTH MODES. It used to be gated to Model on the grounds that in
                            Gameplay the damage state followed from play - which nothing made true,
                            so the stages were simply unreachable there and the control's absence
                            was the only trace. In Gameplay the hull now drives it through the
                            damage table, and the slider is DISABLED rather than removed: the reader
                            needs to see the stage tracking, and to see that the choice exists.

                            Re-authored compactly rather than moved as it stood - the dock's
                            `.field` is a label above a full-width control, which is a shape a 22px
                            stage plate does not have. */}
            {(levels.alt.length > 1 ||
                levels.lod.length > 1 ||
                turretCount > 0) && (
                <div
                    className="stage-chrome subject-state"
                    role="group"
                    aria-label={l10n.t("Model state")}
                >
                    {levels.alt.length > 1 && (
                        <label
                            className="stage-field"
                            title={
                                combat.altDrivenByHull
                                    ? l10n.t(
                                          "Damage state follows the hull in Gameplay",
                                      )
                                    : l10n.t(
                                          "Set damage state - 0 is undamaged",
                                      )
                            }
                        >
                            <Icon name="damage" />
                            <input
                                type="range"
                                min={0}
                                max={altSteps.count - 1}
                                step={1}
                                value={altSteps.positionOf(alt)}
                                disabled={combat.altDrivenByHull}
                                onChange={(e) =>
                                    setAlt(
                                        altSteps.levelAt(
                                            Number(e.target.value),
                                        ),
                                    )
                                }
                            />
                            <span className="stage-value">
                                {levelLabel("alt", alt, levels.alt, {
                                    table: scene?.damageTable ?? [],
                                })}
                            </span>
                        </label>
                    )}

                    {levels.lod.length > 1 && (
                        <label
                            className="stage-field detail"
                            title={l10n.t(
                                "Set LOD - 0 is lowest detail in EaW",
                            )}
                        >
                            <Icon name="detail" />
                            <input
                                type="range"
                                min={0}
                                max={lodSteps.count - 1}
                                step={1}
                                value={lodSteps.positionOf(lod)}
                                onChange={(e) => {
                                    lodPickedRef.current = true;
                                    setLod(
                                        lodSteps.levelAt(
                                            Number(e.target.value),
                                        ),
                                    );
                                }}
                            />
                            <span className="stage-value">
                                {levelLabel("lod", lod, levels.lod, {
                                    cost: lodCost.get(lod),
                                })}
                            </span>
                        </label>
                    )}

                    {/* Every turret back where the model authored it, in every lens. Here rather than
                        beside the per-row resets in the Gameplay dock, because a turret's pose is the
                        subject's own state - the same kind of fact as its damage state and detail level
                        - and the handles that turn it are reachable in every lens. */}
                    {turretCount > 0 && (
                        <span className="stage-field turrets">
                            <Icon name="turret" />
                            <IconButton
                                icon="reset"
                                title={l10n.t(
                                    "Return every turret to the pose the model authored",
                                )}
                                disabled={turnedTurrets === 0}
                                disabledReason={l10n.t("No turret is turned")}
                                badge={
                                    turnedTurrets > 0
                                        ? turnedTurrets
                                        : undefined
                                }
                                onClick={onResetTurrets}
                            />
                        </span>
                    )}
                </div>
            )}
        </>
    );
}
