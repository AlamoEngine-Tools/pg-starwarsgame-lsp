// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Camera > Capture: a picture of the stage, its size and what goes into it.

import type { Dispatch, SetStateAction, RefObject } from "react";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { Button } from "../../shared/Button";
import { DockSection } from "../../shared/DockSection";
import { Field } from "../../shared/Field";
import { ViewRow } from "../../shared/ViewRow";
import { post } from "../previewPost";
import { Icon } from "../../shared/Icon";
import { type PreviewViewport, VIEWPORT_BACKGROUND } from "../viewport";
import {
    CAPTURE_SIZES,
    captureFileName,
    captureSize,
    iconFileName,
    MAX_SIDE,
} from "../capture";
import { IntegerField } from "../../shared/IntegerField";
import { ModeSelector } from "../../shared/ModeSelector";

export interface CaptureSectionProps {
    captureDims: { width: number; height: number } | null;
    captureScenery: boolean;
    captureSizeId: string;
    captureTransparent: boolean;
    customCapture: { width: string; height: string };
    floor: boolean;
    folded: ReadonlySet<string>;
    grid: boolean;
    particlesOn: boolean;
    scene: PreviewScene | null;
    setCaptureScenery: Dispatch<SetStateAction<boolean>>;
    setCaptureSizeId: Dispatch<SetStateAction<string>>;
    setCaptureTransparent: Dispatch<SetStateAction<boolean>>;
    setCustomCapture: Dispatch<
        SetStateAction<{ width: string; height: string }>
    >;
    toggleSection: (id: string) => void;
    viewportRef: RefObject<PreviewViewport | null>;
}

export function CaptureSection({
    captureDims,
    captureScenery,
    captureSizeId,
    captureTransparent,
    customCapture,
    floor,
    folded,
    grid,
    particlesOn,
    scene,
    setCaptureScenery,
    setCaptureSizeId,
    setCaptureTransparent,
    setCustomCapture,
    toggleSection,
    viewportRef,
}: CaptureSectionProps): React.JSX.Element {
    return (
        <DockSection
            id="camera.capture"
            title={l10n.t("Capture")}
            collapsed={folded.has("camera.capture")}
            onToggle={toggleSection}
        >
            <Field
                label={l10n.t("Capture")}
                value={
                    captureDims
                        ? `${captureDims.width} x ${captureDims.height}`
                        : "-"
                }
                info={
                    <>
                        {l10n.t(
                            "Writes the framed area at the chosen size. Grid, floor, effects and annotations stay out unless kept.",
                        )}
                    </>
                }
            >
                <ModeSelector
                    label={l10n.t("Capture size")}
                    value={captureSizeId}
                    options={[
                        ...CAPTURE_SIZES.map((size) => ({
                            id: size.id,
                            label: size.label,
                            title: size.title,
                        })),
                        {
                            id: "custom",
                            label: l10n.t("Custom"),
                            title: l10n.t("Custom - up to {0}", MAX_SIDE),
                        },
                    ]}
                    onSelect={setCaptureSizeId}
                />

                <ViewRow className="capture-custom">
                    <IntegerField
                        value={customCapture.width}
                        ariaLabel={l10n.t("Width")}
                        disabled={captureSizeId !== "custom"}
                        disabledReason={l10n.t("Custom size only")}
                        onChange={(width) =>
                            setCustomCapture((c) => ({
                                ...c,
                                width,
                            }))
                        }
                    />
                    {l10n.t("x")}
                    <IntegerField
                        value={customCapture.height}
                        ariaLabel={l10n.t("Height")}
                        disabled={captureSizeId !== "custom"}
                        disabledReason={l10n.t("Custom size only")}
                        onChange={(height) =>
                            setCustomCapture((c) => ({
                                ...c,
                                height,
                            }))
                        }
                    />
                </ViewRow>

                <label className="check-row">
                    <input
                        type="checkbox"
                        checked={captureTransparent}
                        onChange={(e) =>
                            setCaptureTransparent(e.target.checked)
                        }
                    />
                    {l10n.t("Transparent background")}
                </label>

                <label className="check-row">
                    <input
                        type="checkbox"
                        checked={captureScenery}
                        onChange={(e) => setCaptureScenery(e.target.checked)}
                    />
                    {l10n.t("Keep the grid, floor, effects and markers")}
                </label>

                <Button
                    disabled={captureDims === null}
                    disabledReason={l10n.t(
                        "Width and height must be whole numbers from 1 to {0}",
                        MAX_SIDE,
                    )}
                    onClick={() => {
                        const viewport = viewportRef.current;
                        if (viewport === null) {
                            return;
                        }

                        if (captureDims === null) {
                            return;
                        }
                        const size = captureSize(
                            captureDims.width,
                            captureDims.height,
                        );
                        const dataUrl = viewport.capture({
                            ...size,
                            // The reader's own backdrop when they asked to keep the room, so what they
                            // see is what they get; otherwise nothing at all, which is what an icon
                            // going onto a mega texture wants.
                            // The viewport's own backdrop when a flat one is wanted, so an opaque
                            // capture matches what is on screen rather than inventing a colour.
                            background: captureTransparent
                                ? null
                                : `#${VIEWPORT_BACKGROUND.toString(16).padStart(6, "0")}`,
                            grid: captureScenery && grid,
                            floor: captureScenery && floor,
                            particles: captureScenery && particlesOn,
                            annotations: captureScenery,
                        });

                        post({
                            type: "saveCapture",
                            dataUrl,
                            // The unit's own icons are named the way the engine looks them up.
                            fileName:
                                iconFileName(
                                    CAPTURE_SIZES.find(
                                        (s) => s.id === captureSizeId,
                                    ) ?? null,
                                    scene?.iconName,
                                ) ??
                                captureFileName(
                                    scene?.subject ?? "capture",
                                    size.width,
                                    size.height,
                                ),
                        });
                    }}
                >
                    <Icon name="capture" />
                    {l10n.t("Save a picture")}
                </Button>
            </Field>
        </DockSection>
    );
}
