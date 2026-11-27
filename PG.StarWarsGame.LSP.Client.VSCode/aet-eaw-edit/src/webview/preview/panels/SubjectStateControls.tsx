// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The damage state, detail level and map environment the subject is shown at, and the reset of its
// turrets, on the stage bottom edge.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import {
    PREVIEW_SCENE_KIND,
    type PreviewScene,
} from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { Icon } from "../../shared/Icon";
import { PillSelect } from "../../shared/PillSelect";
import { type DefinedLevels, type LevelCost, levelLabel } from "../levels";

export interface SubjectStateControlsProps {
    alt: number;
    combat: CombatLens;
    levels: DefinedLevels;
    lod: number;
    /** Set when the reader picks a detail level: their pick stands from then on. */
    lodPickedRef: RefObject<boolean>;
    lodCost: Map<number, LevelCost>;
    scene: PreviewScene | null;
    setAlt: Dispatch<SetStateAction<number>>;
    setLod: Dispatch<SetStateAction<number>>;
    /** How many turrets the subject has that turn. None leaves the field out. */
    turretCount: number;
    /** How many of them the reader has turned off their rest. */
    turnedTurrets: number;
    onResetTurrets: () => void;
    /** Asks for the scene on another map environment; null is the unit's default model. */
    onPickEnvironment: (environment: string | null) => void;
}

export function SubjectStateControls({
    alt,
    combat,
    levels,
    lod,
    lodPickedRef,
    lodCost,
    scene,
    setAlt,
    setLod,
    turretCount,
    turnedTurrets,
    onResetTurrets,
    onPickEnvironment,
}: SubjectStateControlsProps): React.JSX.Element {
    const damageTable = scene?.damageTable ?? [];
    // A unit's, never a bare model's: only an object has a Land_Terrain_Model_Mapping to read.
    const isUnit = scene?.kind === PREVIEW_SCENE_KIND.object;
    const terrainModels = scene?.terrainModels ?? [];
    const environment = scene?.environment ?? null;
    const environmentModel = terrainModels.find(
        (terrain) => terrain.environment === environment,
    )?.model;

    return (
        <>
            {/* Which damage state and which detail level the subject is shown at.

                            On the stage rather than in the dock because it is the SUBJECT'S state,
                            and the bottom edge is what the subject is doing.

                            SHOWN IN BOTH MODES. It used to be gated to Model on the grounds that in
                            Gameplay the damage state followed from play - which nothing made true,
                            so the stages were simply unreachable there and the control's absence
                            was the only trace. In Gameplay the hull now drives it through the
                            damage table, and the pick is DISABLED rather than removed: the reader
                            needs to see the stage tracking, and to see that the choice exists.

                            A PILL WITH A LIST, not a slider. Both were sliders with the value
                            written beside them, and the pair filled the slot: 261px for a detail
                            level between 0 and 3. The pill shows the level alone; what the level
                            IS - the health band a stage covers, the meshes and triangles a detail
                            level draws - is in the list's rows and in the tooltip. */}
            {(levels.alt.length > 1 ||
                levels.lod.length > 1 ||
                isUnit ||
                turretCount > 0) && (
                <div
                    className="stage-chrome subject-state"
                    role="group"
                    aria-label={l10n.t("Model state")}
                >
                    {levels.alt.length > 1 && (
                        <PillSelect
                            icon="damage"
                            className="damage"
                            value={alt}
                            options={levels.alt.map((level) => ({
                                value: level,
                                label: levelLabel("alt", level, levels.alt, {
                                    table: damageTable,
                                }),
                            }))}
                            title={l10n.t(
                                "Damage state {0}",
                                levelLabel("alt", alt, levels.alt, {
                                    table: damageTable,
                                }),
                            )}
                            disabled={combat.altDrivenByHull}
                            disabledReason={l10n.t(
                                "Damage state {0} - follows the hull in Gameplay",
                                alt,
                            )}
                            onPick={setAlt}
                        />
                    )}

                    {levels.lod.length > 1 && (
                        <PillSelect
                            icon="detail"
                            className="detail"
                            value={lod}
                            options={levels.lod.map((level) => ({
                                value: level,
                                label: levelLabel("lod", level, levels.lod, {
                                    cost: lodCost.get(level),
                                }),
                            }))}
                            title={l10n.t(
                                "LOD {0} - 0 is lowest detail in EaW",
                                levelLabel("lod", lod, levels.lod, {
                                    cost: lodCost.get(lod),
                                }),
                            )}
                            onPick={(level) => {
                                lodPickedRef.current = true;
                                setLod(level);
                            }}
                        />
                    )}

                    {/* The land model the unit wears on a map of one environment (#294). The server
                        assembles the scene for it, so a pick asks for the scene again. Disabled rather
                        than left out on a unit that maps none, so the reader sees the choice exists. */}
                    {isUnit && (
                        <PillSelect
                            icon="mapEnvironment"
                            className="environment"
                            value={environment ?? ""}
                            shown={environment ?? l10n.t("Default")}
                            options={[
                                { value: "", label: l10n.t("Default model") },
                                ...terrainModels.map((terrain) => ({
                                    value: terrain.environment,
                                    label: `${terrain.environment} - ${terrain.model}`,
                                })),
                            ]}
                            title={
                                environment === null
                                    ? l10n.t("Map environment - default model")
                                    : l10n.t(
                                          "Map environment {0}",
                                          `${environment} - ${environmentModel ?? ""}`,
                                      )
                            }
                            disabled={terrainModels.length === 0}
                            disabledReason={l10n.t(
                                "No Land_Terrain_Model_Mapping",
                            )}
                            onPick={(picked) =>
                                onPickEnvironment(picked === "" ? null : picked)
                            }
                        />
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
