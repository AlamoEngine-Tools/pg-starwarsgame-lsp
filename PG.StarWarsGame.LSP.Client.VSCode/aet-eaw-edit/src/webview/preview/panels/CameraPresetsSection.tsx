// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Camera > Camera presets: saved shots, their names, and the objects that open on them.

import type { Dispatch, SetStateAction, RefObject } from "react";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { InfoBadge } from "../../shared/InfoBadge";
import { Button, IconButton } from "../../shared/Button";
import { DockSection } from "../../shared/DockSection";
import { Field } from "../../shared/Field";
import { ViewRow } from "../../shared/ViewRow";
import { Icon } from "../../shared/Icon";
import { type PreviewViewport } from "../viewport";
import {
    luaFor,
    nextPresetName,
    poseFromPreset,
    presetFromPose,
    renamePreset,
} from "../cameraPresets";
import {
    type CameraBinding,
    type CameraPreset,
} from "../../../shared/viewerSettings";

export interface CameraPresetsSectionProps {
    bindTargets: {
        kind: CameraBinding["kind"];
        value: string;
        label: string;
        what: string;
    }[];
    boundRule: CameraBinding | null;
    cameraPresets: CameraPreset[];
    folded: ReadonlySet<string>;
    renamingPreset: string | null;
    sceneRef: RefObject<PreviewScene | null>;
    setCameraBindings: Dispatch<SetStateAction<CameraBinding[]>>;
    setCameraPresets: Dispatch<SetStateAction<CameraPreset[]>>;
    setCameraView: Dispatch<SetStateAction<string | null>>;
    setRenamingPreset: Dispatch<SetStateAction<string | null>>;
    toggleSection: (id: string) => void;
    viewportRef: RefObject<PreviewViewport | null>;
}

export function CameraPresetsSection({
    bindTargets,
    boundRule,
    cameraPresets,
    folded,
    renamingPreset,
    sceneRef,
    setCameraBindings,
    setCameraPresets,
    setCameraView,
    setRenamingPreset,
    toggleSection,
    viewportRef,
}: CameraPresetsSectionProps): React.JSX.Element {
    return (
        <DockSection
            id="camera.shots"
            title={l10n.t("Camera presets")}
            count={cameraPresets.length}
            end={
                <InfoBadge>
                    {l10n.t(
                        "Kept in radii, not game units, so one preset frames a trooper and a Star Destroyer the same way. Copy as Lua works the distance out for the model on screen.",
                    )}
                </InfoBadge>
            }
            collapsed={folded.has("camera.shots")}
            onToggle={toggleSection}
        >
            <div className="preset-list">
                {cameraPresets.map((saved) => (
                    <ViewRow key={saved.id}>
                        {renamingPreset === saved.id ? (
                            // Enter or leaving the field keeps the name, Escape keeps the old one; an
                            // empty name keeps the old one too (renamePreset).
                            <input
                                type="text"
                                className="preset-name"
                                aria-label={l10n.t("Preset name")}
                                defaultValue={saved.name}
                                autoFocus
                                onFocus={(e) => e.currentTarget.select()}
                                onKeyDown={(e) => {
                                    if (e.key === "Enter") {
                                        e.currentTarget.blur();
                                    } else if (e.key === "Escape") {
                                        e.stopPropagation();
                                        setRenamingPreset(null);
                                    }
                                }}
                                onBlur={(e) => {
                                    const name = e.currentTarget.value;
                                    setCameraPresets((current) =>
                                        renamePreset(current, saved.id, name),
                                    );
                                    setRenamingPreset(null);
                                }}
                            />
                        ) : (
                            <Button
                                className="preset-apply"
                                title={l10n.t(
                                    "Frame this model the way {0} does: {1} radii out, pitch {2}, yaw {3}",
                                    saved.name,
                                    saved.distance.toFixed(1),
                                    Math.round(saved.pitch),
                                    Math.round(saved.yaw),
                                )}
                                onClick={() => {
                                    const viewport = viewportRef.current;
                                    if (viewport === null) {
                                        return;
                                    }

                                    const pose = poseFromPreset(
                                        saved,
                                        viewport.subjectSphere,
                                    );
                                    viewport.applyCameraPose(
                                        pose.position,
                                        pose.target,
                                    );
                                    setCameraView(null);
                                }}
                            >
                                <span className="preset-apply-name">
                                    {saved.name}
                                </span>
                            </Button>
                        )}
                        <IconButton
                            icon="rename"
                            title={l10n.t("Rename {0}", saved.name)}
                            onClick={() => setRenamingPreset(saved.id)}
                        />
                        <IconButton
                            icon="copy"
                            title={l10n.t(
                                "Copy as Lua - Set_Cinematic_Camera_Key, distance for the model on screen",
                            )}
                            onClick={() => {
                                const viewport = viewportRef.current;
                                if (viewport === null) {
                                    return;
                                }

                                void navigator.clipboard.writeText(
                                    luaFor(
                                        saved,
                                        viewport.subjectSphere,
                                        sceneRef.current?.kind === "Object"
                                            ? sceneRef.current.subject
                                            : null,
                                    ),
                                );
                            }}
                        />
                        <IconButton
                            icon="remove"
                            title={l10n.t("Delete {0}", saved.name)}
                            onClick={() =>
                                setCameraPresets((current) =>
                                    current.filter((p) => p.id !== saved.id),
                                )
                            }
                        />
                    </ViewRow>
                ))}

                <Button
                    onClick={() => {
                        const viewport = viewportRef.current;
                        if (viewport === null) {
                            return;
                        }

                        // Named after the first free number rather than prompting; the
                        // row's pencil renames it in place.
                        const saved = presetFromPose(
                            nextPresetName(cameraPresets),
                            viewport.cameraPosition,
                            viewport.subjectSphere,
                        );

                        setCameraPresets((current) => [...current, saved]);
                    }}
                >
                    <Icon name="add" />
                    {l10n.t("New camera preset")}
                </Button>
            </div>

            <Field
                label={l10n.t("Opens with")}
                infoSeverity={bindTargets.length === 0 ? "warning" : "info"}
                info={
                    <>
                        {bindTargets.length === 0
                            ? l10n.t(
                                  "A model opened on its own has no object, type or category to bind to. Open it through a game object to bind a preset to a whole roster.",
                              )
                            : l10n.t(
                                  "A bound preset is applied when the model opens, most specific rule first: This object beats its type, and its type beats a category. Moving the camera afterwards is always allowed.",
                              )}
                    </>
                }
                value={
                    boundRule === null
                        ? "nothing"
                        : (cameraPresets.find(
                              (p) => p.id === boundRule.presetId,
                          )?.name ?? "a deleted preset")
                }
            >
                {/* Bind the LAST camera preset: with no preset there is nothing to bind, and picking
                which one belongs in a list rather than in three buttons. */}
                {bindTargets.map((target) => (
                    <Button
                        key={target.kind}
                        title={l10n.t(
                            "Open every {0} with {1}",
                            target.what,
                            cameraPresets.length === 0
                                ? "a camera preset"
                                : cameraPresets[cameraPresets.length - 1].name,
                        )}
                        disabled={cameraPresets.length === 0}
                        disabledReason={l10n.t("No camera preset to bind")}
                        onClick={() => {
                            const preset =
                                cameraPresets[cameraPresets.length - 1];
                            setCameraBindings((current) => [
                                // Replacing rather than appending: two rules of one kind on the same
                                // value would leave the second permanently unreachable, since the first
                                // match wins.
                                ...current.filter(
                                    (b) =>
                                        !(
                                            b.kind === target.kind &&
                                            b.value.toLowerCase() ===
                                                target.value.toLowerCase()
                                        ),
                                ),
                                {
                                    id: `bind-${Date.now().toString(36)}-${target.kind}`,
                                    kind: target.kind,
                                    value: target.value,
                                    presetId: preset.id,
                                },
                            ]);
                        }}
                    >
                        {target.label}
                    </Button>
                ))}

                {boundRule !== null && (
                    <Button
                        title={l10n.t(
                            "Stop opening {0} with a camera preset",
                            boundRule.kind === "object"
                                ? "this object"
                                : `every ${boundRule.value}`,
                        )}
                        onClick={() =>
                            setCameraBindings((current) =>
                                current.filter((b) => b.id !== boundRule.id),
                            )
                        }
                    >
                        <Icon name="remove" />
                        {l10n.t("Unbind")}
                    </Button>
                )}
            </Field>
        </DockSection>
    );
}
