// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The dock header: the model's identity and counts behind the left button, the lens dial in the
// centre, and the hand-off, the stand-in shadow's mark and the severity tag in the right corner.

import type { Dispatch, SetStateAction } from "react";
import type { Flyouts } from "../useFlyouts";
import type { AnimationTransport } from "../useAnimationTransport";
import type { Abilities } from "../useAbilities";
import * as l10n from "@vscode/l10n";
import {
    type ModelDetail,
    type PreviewScene,
} from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { Icon } from "../../shared/Icon";
import { type ViewportStats } from "../viewport";
import { type PreviewMode, previewModes } from "../previewMode";
import {
    type DialAction,
    RotaryModeSwitch,
} from "../../shared/RotaryModeSwitch";

export interface DockHeaderProps {
    abilityState: Abilities;
    /** What the lens does to everything it shows: the scene reset, and Gameplay's destroy and repair. */
    dialActions: readonly DialAction[];
    flyouts: Flyouts;
    mode: PreviewMode;
    modelDetail: ModelDetail | null;
    player: AnimationTransport;
    scene: PreviewScene | null;
    setMode: Dispatch<SetStateAction<PreviewMode>>;
    /** Game rendering is casting the stand-in shadow, which the game does not draw. */
    standInDrawn: boolean;
    stats: ViewportStats | null;
}

export function DockHeader({
    abilityState,
    dialActions,
    flyouts,
    mode,
    modelDetail,
    player,
    scene,
    setMode,
    standInDrawn,
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
                        actions={dialActions}
                        onSelect={setMode}
                    />
                </span>
            )}

            {/* The right edge: passive state only. The hand-off and the verdict are the page's
                            and sit in its toolbar. */}
            <span className="header-corner">
                {/* Passive state beside the warnings: a shadow on screen the game does not draw.
                Not a control - the switch is in Scene settings - so a mark, not a button. */}
                <span className="header-corner-row">
                    {standInDrawn && (
                        <span
                            className="stand-in-mark"
                            role="img"
                            title={l10n.t(
                                "Stand-in shadow: The game draws none for this model",
                            )}
                            aria-label={l10n.t(
                                "Stand-in shadow: The game draws none for this model",
                            )}
                        >
                            <Icon name="standInShadow" />
                        </span>
                    )}
                </span>
            </span>
        </>
    );
}
