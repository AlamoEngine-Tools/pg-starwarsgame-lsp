// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The dock header: the model's identity and counts behind the left button, the lens dial in the
// centre, and the hand-off and the severity tag in the right corner.

import type { Dispatch, SetStateAction } from "react";
import type { Flyouts } from "../useFlyouts";
import type { AnimationTransport } from "../useAnimationTransport";
import type { Abilities } from "../useAbilities";
import * as l10n from "@vscode/l10n";
import {
    type ModelDetail,
    type PreviewProblem,
    type PreviewScene,
} from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { post } from "../previewPost";
import { type ViewportStats } from "../viewport";
import { type PreviewMode, previewModes } from "../previewMode";
import { RotaryModeSwitch } from "../../shared/RotaryModeSwitch";
import { SeverityTag } from "../../shared/SeverityTag";
import { type Handoff } from "../handoff";
import { type ValidationState } from "../../loc/useLocPanel";

export interface DockHeaderProps {
    abilityState: Abilities;
    flyouts: Flyouts;
    handoff: Handoff | null;
    mode: PreviewMode;
    modelDetail: ModelDetail | null;
    notices: PreviewProblem[];
    player: AnimationTransport;
    problemsOpen: boolean;
    problemsTitle: string;
    scene: PreviewScene | null;
    setMode: Dispatch<SetStateAction<PreviewMode>>;
    setProblemsOpen: Dispatch<SetStateAction<boolean>>;
    severity: ValidationState;
    stats: ViewportStats | null;
}

export function DockHeader({
    abilityState,
    flyouts,
    handoff,
    mode,
    modelDetail,
    notices,
    player,
    problemsOpen,
    problemsTitle,
    scene,
    setMode,
    setProblemsOpen,
    severity,
    stats,
}: DockHeaderProps): React.JSX.Element {
    return (
        <>
            {/* The graph's layout exactly: one button anchored left where its save sits,
                            the dial alone in the centred slot, one anchored right. The preview has
                            nothing to save, so the left slot carries what the model IS - its name
                            and its counts - which frees the centre entirely for the dial. */}
            <IconButton
                icon="details"
                className={"header-left" + (flyouts.infoOpen ? " active" : "")}
                expanded={flyouts.infoOpen}
                title={scene?.subject ?? l10n.t("No model")}
                onClick={() => flyouts.setInfoOpen((open) => !open)}
            />

            {/* A flyout, anchored under the button that opens it and pointing back at
                            it: the model's name and its counts are something you check, not
                            something you work with, so they float over the panel rather than
                            taking a block of it. The header is `position: relative` and clips
                            nothing, which is what lets this hang below it. */}
            {flyouts.infoOpen && (
                <div className="model-info" role="dialog" ref={flyouts.infoRef}>
                    <div className="model-info-name" title={scene?.subject}>
                        {scene?.subject ?? l10n.t("No model")}
                    </div>
                    {stats === null ? (
                        <div className="dock-hint">
                            {l10n.t("Nothing loaded yet.")}
                        </div>
                    ) : (
                        <>
                            <div className="stat-row">
                                <span>{l10n.t("Parts")}</span>
                                <span className="value">{stats.parts}</span>
                            </div>
                            <div className="stat-row">
                                <span>{l10n.t("Meshes")}</span>
                                <span className="value">{stats.meshes}</span>
                            </div>
                            <div className="stat-row">
                                <span>{l10n.t("Triangles")}</span>
                                <span className="value">
                                    {stats.triangles.toLocaleString()}
                                </span>
                            </div>
                            <div className="stat-row">
                                <span>{l10n.t("Bones")}</span>
                                <span className="value">{stats.bones}</span>
                            </div>

                            {/* Only when the file carries some, and said plainly: they
                                            are 3ds Max export residue. No shipped effect declares a
                                            point or spot light uniform that could consume one, so
                                            neither the engine nor this preview lights anything with
                                            them - and an author who finds them in their file
                                            deserves to be told that rather than left hunting for a
                                            light that never turns on. */}
                            {(modelDetail?.lightCount ?? 0) > 0 && (
                                <div
                                    className="stat-row"
                                    title={l10n.t(
                                        "The engine lights everything with the three scene directionals instead",
                                    )}
                                >
                                    <span>{l10n.t("Lights (ignored)")}</span>
                                    <span className="value">
                                        {modelDetail?.lightCount}
                                    </span>
                                </div>
                            )}
                        </>
                    )}
                </div>
            )}

            {/* A particle file gets no dial: it has no meshes, no clips and no
                            hardpoints, so every other lens would be empty. */}
            {scene !== null && scene.kind !== "Particle" && (
                <span className="header-modes">
                    <RotaryModeSwitch
                        mode={mode}
                        modes={previewModes({
                            animations: player.animations.length,
                            hardpoints: scene.hardpoints.length,
                            particles: scene.particles.length,
                            weapons: scene.weapons.length,
                            abilities: abilityState.abilities.length,
                        })}
                        onSelect={setMode}
                    />
                </span>
            )}

            {/* The right edge is a column: the hand-off above the severity tag. Outside
                            the dial's span, because a particle file has no dial and is exactly where
                            the Particle Editor belongs. One tool per scene - see handoffFor. */}
            <span className="header-corner">
                {handoff !== null && (
                    <IconButton
                        icon={handoff.icon}
                        className="as-action"
                        title={handoff.title}
                        disabled={handoff.disabledReason !== undefined}
                        disabledReason={handoff.disabledReason ?? ""}
                        onClick={() =>
                            post({
                                type: "openExternal",
                                tool: handoff.tool,
                            })
                        }
                    />
                )}

                <SeverityTag
                    severity={severity}
                    count={notices.length}
                    expanded={problemsOpen}
                    disabled={notices.length === 0}
                    disabledReason={l10n.t(
                        "Nothing to report about this model",
                    )}
                    title={problemsTitle}
                    onClick={() => setProblemsOpen((open) => !open)}
                />
            </span>
        </>
    );
}
