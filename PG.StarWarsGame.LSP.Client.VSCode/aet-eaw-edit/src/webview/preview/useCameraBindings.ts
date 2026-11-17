// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The camera: the views this subject offers, the facts a binding is matched on, and the bound
// camera chosen from them and applied when a subject opens.

import * as l10n from "@vscode/l10n";
import {
    useCallback,
    useMemo,
    useRef,
    type Dispatch,
    type SetStateAction,
} from "react";
import { type PreviewScene } from "../../protocol/modelPreview";
import { PRESETS } from "./previewLabels";
import { type PreviewViewport } from "./viewport";
import { lightBearing } from "./lightBearing";
import { cameraViewOptions, modelCameraEntries } from "./modelCameras";
import { poseFromPreset } from "./cameraPresets";
import { bindingFor, type PreviewSubject } from "./cameraBindings";
import {
    type CameraBinding,
    type DirectionalName,
    type LightRig,
    type CameraPreset,
} from "../../shared/viewerSettings";

/** What useCameraBindings reads from the rest of the preview. */
export interface CameraBindingsInputs {
    cameraBindings: CameraBinding[];
    cameraPresets: CameraPreset[];
    editing: DirectionalName;
    lights: LightRig;
    scene: PreviewScene | null;
    setCameraView: Dispatch<SetStateAction<string | null>>;
}

export type CameraBindings = ReturnType<typeof useCameraBindings>;

export function useCameraBindings({
    cameraBindings,
    cameraPresets,
    editing,
    lights,
    scene,
    setCameraView,
}: CameraBindingsInputs) {
    // Two angles do not say where a light is. This puts the same two numbers in the terms the
    // stage's own Front / Side / Top presets already gave the reader, and says out loud when the
    // light has dropped under the ground plane - which the slider marks nowhere.
    const bearing = lightBearing(
        lights[editing].azimuth,
        lights[editing].elevation,
    );

    // The subject's own cameras. Derived, not stored: they belong to the model, so a new scene
    // replaces them and nothing has to be cleared.
    const authorCameras = useMemo(
        () => modelCameraEntries(scene?.cameras ?? []),
        [scene?.cameras],
    );

    /** Presets and the model's own cameras as the one choice they are. */
    const cameraViews = useMemo(
        () => cameraViewOptions(PRESETS, scene?.cameras ?? []),
        [scene?.cameras],
    );

    /** What the subject IS, as far as a binding rule can see. */
    const subjectFacts = useMemo<PreviewSubject>(
        () => ({
            // Only a game object has an id worth matching. A bare model is previewed by filename, which
            // is not a thing anyone binds a roster preset to.
            objectId: scene?.kind === "Object" ? scene.subject : null,
            objectType: scene?.objectType ?? null,
            categories: scene?.categories ?? [],
        }),
        [scene?.kind, scene?.subject, scene?.objectType, scene?.categories],
    );

    /** The rule that decides this subject's opening shot, if any. */
    const boundRule = useMemo(
        () => bindingFor(subjectFacts, cameraBindings),
        [subjectFacts, cameraBindings],
    );

    // Kept in refs as well as in state: `applyBoundCamera` runs from the message handler, which is
    // set up once, and a closure over the state would see whatever the panel opened with.
    const subjectFactsRef = useRef(subjectFacts);
    subjectFactsRef.current = subjectFacts;

    const bindingsRef = useRef(cameraBindings);
    bindingsRef.current = cameraBindings;

    const presetsRef = useRef(cameraPresets);
    presetsRef.current = cameraPresets;

    /**
     * What the subject on screen could be bound by, most specific first.
     *
     * Empty for a bare model, which has no game object at all - and the panel says so rather than
     * offering three buttons that would each bind nothing.
     */
    const bindTargets = useMemo(() => {
        const targets: {
            kind: CameraBinding["kind"];
            value: string;
            label: string;
            what: string;
        }[] = [];

        if (subjectFacts.objectId !== null) {
            targets.push({
                kind: "object",
                value: subjectFacts.objectId,
                label: l10n.t("Bind this object"),
                what: `'${subjectFacts.objectId}'`,
            });
        }

        if (subjectFacts.objectType !== null) {
            targets.push({
                kind: "type",
                value: subjectFacts.objectType,
                label: l10n.t("Bind type {0}", subjectFacts.objectType),
                what: subjectFacts.objectType,
            });
        }

        // The FIRST category only. A subject carries several - `Vehicle | AntiInfantry` - and a row
        // of buttons per category would swamp the panel; the rest can be reached by binding from a
        // subject whose first category is the one wanted.
        const category = subjectFacts.categories[0];
        if (category !== undefined) {
            targets.push({
                kind: "category",
                value: category,
                label: l10n.t("Bind category {0}", category),
                what: category,
            });
        }

        return targets;
    }, [subjectFacts]);

    /**
     * Frames the subject with its bound preset, if it has one.
     *
     * Read through refs rather than closed over: this runs from the message handler, which is set
     * up once and would otherwise capture the bindings as they were when the panel opened.
     */
    const applyBoundCamera = useCallback(
        (viewport: PreviewViewport): boolean => {
            const rule = bindingFor(
                subjectFactsRef.current,
                bindingsRef.current,
            );
            const preset =
                rule === null
                    ? undefined
                    : presetsRef.current.find((p) => p.id === rule.presetId);

            if (preset === undefined) {
                return false;
            }

            const pose = poseFromPreset(preset, viewport.subjectSphere);
            viewport.applyCameraPose(pose.position, pose.target);
            setCameraView(null);

            return true;
        },
        [],
    );

    return {
        bearing,
        authorCameras,
        cameraViews,
        boundRule,
        bindTargets,
        applyBoundCamera,
    };
}
