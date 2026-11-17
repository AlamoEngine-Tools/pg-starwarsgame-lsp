// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Scene > Room: the backdrop, the ground and its level. Viewer-level settings; see useRoomSettings.

import type { Dispatch, SetStateAction, RefObject } from "react";
import * as l10n from "@vscode/l10n";
import { snapToZero } from "../groundRange";
import { DockSection } from "../../shared/DockSection";
import { Field } from "../../shared/Field";
import { type ChoiceOption } from "../../shared/choice";
import { ModeSelector } from "../../shared/ModeSelector";
import { type BackgroundKind } from "../../../shared/viewerSettings";

export interface RoomSectionProps {
    background: BackgroundKind;
    floor: boolean;
    floorLevel: number;
    folded: ReadonlySet<string>;
    ground: { limit: number; step: number };
    groundDragging: RefObject<boolean>;
    setBackground: Dispatch<SetStateAction<BackgroundKind>>;
    setFloorLevel: Dispatch<SetStateAction<number>>;
    toggleSection: (id: string) => void;
}

export function RoomSection({
    background,
    floor,
    floorLevel,
    folded,
    ground,
    groundDragging,
    setBackground,
    setFloorLevel,
    toggleSection,
}: RoomSectionProps): React.JSX.Element {
    return (
        <DockSection
            id="scene.room"
            title={l10n.t("Room")}
            collapsed={folded.has("scene.room")}
            onToggle={toggleSection}
        >
            <Field
                label={l10n.t("Backdrop")}
                info={
                    <>
                        {l10n.t(
                            "Never lit and never in the depth buffer, so a backdrop cannot be mistaken for part of the model",
                        )}
                    </>
                }
            >
                <ModeSelector
                    label={l10n.t("Backdrop")}
                    value={background}
                    options={
                        [
                            {
                                id: "flat",
                                label: l10n.t("Flat"),
                                title: l10n.t("A plain, neutral field"),
                            },
                            {
                                id: "starfield",
                                label: l10n.t("Stars"),
                                title: l10n.t("A starfield"),
                            },
                            {
                                id: "sky",
                                label: l10n.t("Sky"),
                                title: l10n.t("A sky with a horizon"),
                            },
                        ] satisfies ChoiceOption<BackgroundKind>[]
                    }
                    onSelect={setBackground}
                />
            </Field>

            <Field
                label={l10n.t("Ground height")}
                value={floorLevel}
                info={
                    <>
                        {l10n.t(
                            "Zero is the middle of the track - the plane the model itself stands on. The ends are one model radius either way, so the control means the same thing on a trooper and on a Star Destroyer. The grid moves with it: It is the ruler for the ground, not for the origin.",
                        )}
                    </>
                }
            >
                {/* The tick marks where the model stands. Zero is always the exact middle, because
                the track is symmetrical by construction - see `groundRange`. */}
                <span className="detent-track">
                    <input
                        type="range"
                        min={-ground.limit}
                        max={ground.limit}
                        step={ground.step}
                        value={floorLevel}
                        disabled={!floor}
                        title={
                            floor
                                ? l10n.t(
                                      "Where the ground and its grid sit, in model units. The track runs {0} either side of the origin, which is the middle, and catches there when dragged.",
                                      ground.limit,
                                  )
                                : l10n.t("Switch the ground on to place it")
                        }
                        onPointerDown={() => {
                            groundDragging.current = true;
                        }}
                        onPointerUp={() => {
                            groundDragging.current = false;
                        }}
                        onPointerCancel={() => {
                            groundDragging.current = false;
                        }}
                        onKeyDown={() => {
                            groundDragging.current = false;
                        }}
                        onChange={(e) =>
                            setFloorLevel(
                                snapToZero(
                                    Number(e.target.value),
                                    ground.step,
                                    groundDragging.current ? 1 : 0,
                                ),
                            )
                        }
                    />
                </span>
            </Field>
        </DockSection>
    );
}
