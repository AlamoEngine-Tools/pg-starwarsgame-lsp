// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The dock foot: what the other lenses are up to, the skeleton control in Model, the player in
// Animation, and a particle file's own player.

import type { AnimationTransport } from "../useAnimationTransport";
import type { ParticleTransport } from "../particleTransport";
import type { Dispatch, SetStateAction, RefObject } from "react";
import type { CombatLens } from "../useCombatLens";
import type { Abilities } from "../useAbilities";
import * as l10n from "@vscode/l10n";
import {
    PREVIEW_SCENE_KIND,
    type PreviewScene,
} from "../../../protocol/modelPreview";
import { Button } from "../../shared/Button";
import { PlaybackSection } from "./PlaybackSection";
import { ParticlePlaybackSection } from "./ParticlePlaybackSection";
import { type PreviewViewport } from "../viewport";
import { otherModeChips, type PreviewMode } from "../previewMode";
import { ModeSelector } from "../../shared/ModeSelector";

export interface DockFootProps {
    abilityState: Abilities;
    /** The subject's bones - what the skeleton control draws. */
    boneCount: number;
    combat: CombatLens;
    mode: PreviewMode;
    /** Whether a particle file's system has arrived for its player to drive. */
    particleReady: boolean;
    particleSpeed: number;
    particleTransport: ParticleTransport;
    player: AnimationTransport;
    scene: PreviewScene | null;
    setMode: Dispatch<SetStateAction<PreviewMode>>;
    setParticleSpeed: (speed: number) => void;
    setSkeletonMode: (next: "off" | "selected" | "always") => void;
    skeletonMode: "selected" | "off" | "always";
    viewportRef: RefObject<PreviewViewport | null>;
}

export function DockFoot({
    abilityState,
    boneCount,
    combat,
    mode,
    particleReady,
    particleSpeed,
    particleTransport,
    player,
    scene,
    setMode,
    setParticleSpeed,
    setSkeletonMode,
    skeletonMode,
    viewportRef,
}: DockFootProps): React.JSX.Element {
    return (
        <>
            {/* The soft switch's safety net. Leaving a lens does not stop what it
                            started, so each one says in a line what the others are up to - and a
                            chip implies a click, so it takes you there. */}
            {otherModeChips(mode, {
                playing: player.animation,
                destroyed: combat.destroyed.size,
                particleSystems: scene?.particles.length ?? 0,
                abilities: abilityState.activeAbilities.size,
            }).map((chip) => (
                <Button
                    key={chip.mode}
                    compact
                    title={l10n.t("Switch to {0} mode", chip.mode)}
                    onClick={() => setMode(chip.mode)}
                >
                    {chip.text}
                </Button>
            ))}

            {/* HOW YOU LOOK at the model, which is what the foot is for - the dock's
                            three levels each answer a different question, and "draw the skeleton"
                            is not a fact about the model the way the tree above it is. It sat
                            under the tree because it is the thing the tree is a list OF; that is
                            an argument about subject matter, and the level is decided by what a
                            control DOES.

                            One control rather than two, because "draw the skeleton" and "label its
                            bones" were never independent - nobody wants labels floating with no
                            skeleton under them, and the old pair let you ask for exactly that. */}
            {mode === "model" && (
                <div className="field skeleton-field">
                    <span className="field-label">{l10n.t("Skeleton")}</span>
                    <ModeSelector
                        label={l10n.t("How the skeleton is drawn")}
                        value={skeletonMode}
                        // Disabled rather than removed, and on the BONES rather than on the scene
                        // kind: a particle file has none (measured - no particle .alo in eaw or foc
                        // carries a skeleton), and a model without any gets the same answer.
                        disabled={boneCount === 0}
                        disabledReason={l10n.t("No skeleton loaded")}
                        options={[
                            {
                                id: "off",
                                label: l10n.t("Off"),
                                title: l10n.t("No skeleton"),
                            },
                            {
                                id: "selected",
                                label: l10n.t("Selected"),
                                title: l10n.t("Name the picked bone only"),
                            },
                            {
                                id: "always",
                                label: l10n.t("Always"),
                                // The dropping rule is real and worth knowing, but it
                                // is a thing the reader SEES the moment they press
                                // this on a large hull. The tooltip's job is to get
                                // them there.
                                title: l10n.t("Name every bone"),
                            },
                        ]}
                        onSelect={setSkeletonMode}
                    />
                </div>
            )}

            {/* Fixed above the viewport controls, and DISABLED rather than removed when no
                            clip is selected. A control group that appears and disappears as you pick
                            things shifts every row under it, so the button you were reaching for is no
                            longer where you looked. It is gated only on whether the subject has clips at
                            all - a fact about the file - so nothing the reader does inside
                            Animation mode ever moves this. Changing LENS is the one thing that
                            does, which is what a lens is for. */}
            {mode === "animation" && player.animations.length > 0 && (
                <PlaybackSection player={player} viewportRef={viewportRef} />
            )}

            {/* A particle file's player, in the animation player's place. Shown for the whole of
                            the file's life, disabled until its system arrives, so nothing moves under
                            the reader when it does. An assembled unit's effects get none: its foot
                            already holds the animation player, and one dock with two transports
                            would leave the reader asking which one plays what. */}
            {scene?.kind === PREVIEW_SCENE_KIND.particle && (
                <ParticlePlaybackSection
                    transport={particleTransport}
                    ready={particleReady}
                    speed={particleSpeed}
                    setSpeed={setParticleSpeed}
                />
            )}
        </>
    );
}
