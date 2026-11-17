// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The stage's top edge: HOW YOU ARE LOOKING at the subject - the scene and the overlays on the left, the
// faction tint in the middle, where the camera looks from on the right.

import type { Dispatch, SetStateAction, RefObject } from "react";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { StageSlots } from "../../shared/Stage";
import { NEUTRAL_COLOUR } from "../previewLabels";
import { Icon, type IconName } from "../../shared/Icon";
import { type PresetView, type PreviewViewport } from "../viewport";
import { toHex } from "../colour";
import {
    cameraPose,
    type CameraViewOption,
    type ModelCameraEntry,
} from "../modelCameras";
import { ModeSelector } from "../../shared/ModeSelector";

export interface StageTopEdgeProps {
    authorCameras: ModelCameraEntry[];
    cameraOpen: boolean;
    cameraView: string | null;
    cameraViews: CameraViewOption[];
    chooseTint: (name: string, colour: string | null) => void;
    customColour: string | null;
    faction: string;
    overlays: {
        id: string;
        label: string;
        icon: IconName;
        title: string;
        on: boolean;
        set: (on: boolean) => void;
        disabled?: boolean;
    }[];
    scene: PreviewScene | null;
    setCameraOpen: Dispatch<SetStateAction<boolean>>;
    setCameraView: Dispatch<SetStateAction<string | null>>;
    setWorldOpen: Dispatch<SetStateAction<boolean>>;
    viewportRef: RefObject<PreviewViewport | null>;
    worldOpen: boolean;
}

export function StageTopEdge({
    authorCameras,
    cameraOpen,
    cameraView,
    cameraViews,
    chooseTint,
    customColour,
    faction,
    overlays,
    scene,
    setCameraOpen,
    setCameraView,
    setWorldOpen,
    viewportRef,
    worldOpen,
}: StageTopEdgeProps): React.JSX.Element {
    return (
        <StageSlots
            edge="top"
            start={
                <>
                    {/* Its own plate. The scene settings OPEN something, while the toggles beside
                            them switch something - putting all of it on one bar said they were one
                            group of related switches, which the flyout button is not. */}
                    <div className="stage-chrome">
                        <IconButton
                            icon="scene"
                            className={
                                "world-globe" + (worldOpen ? " active" : "")
                            }
                            expanded={worldOpen}
                            title={l10n.t("Scene settings")}
                            onClick={() => setWorldOpen((open) => !open)}
                        />
                    </div>

                    {/* The grid, the ground and the effects master describe the ROOM the model
                            is standing in - so they belong on the stage rather than in a dock that
                            describes the subject. Their persistence is unchanged: every one was
                            already a Tier 1 viewer setting, and moving a control does not move
                            where its value lives.

                            Icon only. These are the same switch repeated, so the icon is what tells
                            them apart and the words only made the bar long enough to reach the
                            camera. The name leads the tooltip. */}
                    <div className="stage-chrome icon-only">
                        {overlays.map((overlay) => (
                            <IconButton
                                key={overlay.id}
                                icon={overlay.icon}
                                className={overlay.on ? "active" : undefined}
                                pressed={overlay.on}
                                label={overlay.label}
                                title={overlay.title}
                                disabled={overlay.disabled ?? false}
                                // The overlay's own title already says why when it is off:
                                // each one phrases its state, so there is nothing to add here.
                                disabledReason={overlay.title}
                                onClick={() => overlay.set(!overlay.on)}
                            />
                        ))}
                    </div>
                </>
            }
            middle={
                <>
                    {/* The faction palette, centred over the model.

                            It sits with the room and the camera because all three answer HOW YOU
                            ARE LOOKING at the subject rather than what the subject is - a tint is
                            a way of viewing a hull, not a property of it. The bottom edge is for
                            what the model is DOING, which is why it moved.

                            The colour IS the button - a swatch says what it will do far better
                            than its faction's name does, and the name is one hover away. The
                            custom well is always the RIGHTMOST, so the one control that is not a
                            faction never moves as the roster changes length.

                            Not gated on the lens. It is stage chrome, and a corner that empties
                            when you change lens makes the stage feel like it is coming apart. */}
                    {(scene?.factions.length ?? 0) > 0 && (
                        <div
                            className="stage-chrome faction-palette"
                            role="group"
                            aria-label={l10n.t("Faction colour")}
                        >
                            <button
                                type="button"
                                className={
                                    "swatch swatch-plain" +
                                    (faction === "" && customColour === null
                                        ? " active"
                                        : "")
                                }
                                title={l10n.t("No team colour")}
                                onClick={() => chooseTint("", null)}
                            >
                                <Icon name="none" />
                            </button>

                            {scene?.factions.map((entry) => (
                                <button
                                    key={entry.name}
                                    type="button"
                                    className={
                                        "swatch" +
                                        (faction === entry.name &&
                                        customColour === null
                                            ? " active"
                                            : "")
                                    }
                                    style={{
                                        background: toHex(
                                            entry.color ?? NEUTRAL_COLOUR,
                                        ),
                                    }}
                                    title={entry.name}
                                    // Picking a faction drops a custom colour, or the strip
                                    // would highlight one and the hull show another.
                                    onClick={() => chooseTint(entry.name, null)}
                                />
                            ))}

                            <input
                                type="color"
                                className={
                                    "swatch swatch-custom" +
                                    (customColour !== null ? " active" : "")
                                }
                                value={
                                    customColour ??
                                    toHex(
                                        scene?.factions.find(
                                            (f) => f.name === faction,
                                        )?.color ?? NEUTRAL_COLOUR,
                                    )
                                }
                                title={l10n.t("Pick a custom colour")}
                                onChange={(e) =>
                                    chooseTint(faction, e.target.value)
                                }
                            />
                        </div>
                    )}
                </>
            }
            end={
                <>
                    {/* Where you are looking FROM is a property of the view, not of the model,
                            so it sits on the stage like the scene controls opposite.

                            The presets read as words rather than pictograms, because no icon says
                            "three-quarter view". They carried a small camera glyph to say what the
                            row was for; the camera BUTTON beside them now says it, and two camera
                            icons in a row said it twice.

                            ONE radiogroup, presets and the model's own cameras together. They were
                            two runs of `aria-pressed` buttons, which is the markup for switches
                            that hold and release independently - and these cannot, because the
                            camera is in one place at a time. Choosing any of them releases
                            whichever was chosen before, which is what a radiogroup says and a row
                            of toggles denies. */}
                    <div className="stage-chrome">
                        <ModeSelector
                            label={l10n.t("Where the camera looks from")}
                            value={cameraView}
                            options={cameraViews}
                            onSelect={(id) => {
                                // The model's own cameras are the ones this panel did not
                                // invent, and only they can refuse: a bone can be missing from
                                // the skeleton that was loaded, and a view that did not take
                                // must not be marked as the one you are looking from.
                                const authored = authorCameras.find(
                                    (entry) => entry.id === id,
                                );

                                if (authored === undefined) {
                                    setCameraView(id as PresetView);
                                    viewportRef.current?.frameAll(
                                        id as PresetView,
                                    );
                                    return;
                                }

                                // The pose the SERVER resolved, turned into the scene's axes.
                                //
                                // It used to ask the viewport to find the pair by name -
                                // Camera01 and Camera01.Target - and the bone in the GLB is
                                // Camera01Target, the dot having not survived the export. So
                                // the lookup failed, applyModelCamera returned false, and the
                                // entry did nothing at all, silently, on every model with one.
                                //
                                // The turn matters as much as the numbers: those are Alamo
                                // Z-up and the scene is glTF Y-up. See `cameraPose`.
                                const pose = cameraPose(authored);

                                if (pose === null) {
                                    return;
                                }

                                viewportRef.current?.applyCameraPose(
                                    pose.position,
                                    pose.target,
                                );
                                setCameraView(id);
                            }}
                        />
                    </div>

                    {/* The mirror of the scene button opposite: the presets are the switches,
                            this OPENS something, and the two are not the same kind of control. Last
                            on the right as the scene is first on the left, so the stage reads
                            outwards from the model in both directions. */}
                    <div className="stage-chrome">
                        <IconButton
                            icon="camera"
                            className={cameraOpen ? "active" : undefined}
                            expanded={cameraOpen}
                            title={l10n.t("Camera settings")}
                            onClick={() => setCameraOpen((open) => !open)}
                        />
                    </div>
                </>
            }
        />
    );
}
