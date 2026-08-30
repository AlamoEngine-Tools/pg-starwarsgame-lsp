// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The cameras a model carries in its own skeleton, as entries beside the view presets.
//
// 447 of the 3340 shipped models declare one, and a person aimed each one at its own model - which
// is exactly the framing an icon wants. The other 87% declare none, and say so rather than dropping
// the control: a reader who never sees the entry never learns the feature is there.

import { type PreviewCamera } from '../../protocol/modelPreview';

/** One selectable camera, or the one disabled entry that stands in for none. */
export interface ModelCameraEntry {
    /** Unique within the list, which the name alone is not: nothing stops a file repeating one. */
    id: string;

    /**
     * The BONE's name, verbatim.
     *
     * Not prettified. `Get_Bone_Position` is a shipped game-object method, so this exact string
     * addresses the same point from a script - which is most of why showing it is worth anything.
     */
    label: string;

    title: string;
    disabled: boolean;

    /**
     * Where the camera stands and what it looks at, as the SERVER resolved them.
     *
     * Carried rather than re-found by name. The panel used to look the pair up in the loaded model
     * as `Camera01` and `Camera01.Target` - and the bone in the GLB is `Camera01Target`, because
     * the dot does not survive the export, so the lookup failed and pressing the entry did nothing
     * at all, silently, on every model that has one.
     *
     * Null on the stand-in entry, which names no camera to have a pose.
     */
    position: readonly number[] | null;
    target: readonly number[] | null;
}

/** What the list says when the subject declares no camera of its own. */
const NONE: ModelCameraEntry = {
    id: 'camera:none',
    label: 'Author',
    // 447 of the 3340 shipped models carry one, as a Camera01 / Camera01.Target bone pair. That is
    // the note at the top of this file, which is where it belongs - a reader who wants to know what
    // the entry IS has the disabled button in front of them saying this model has none.
    title: 'This model carries no camera of its own',
    disabled: true,
    position: null,
    target: null,
};

/**
 * The camera entries to offer for a subject.
 *
 * Ordered as the file lists them, because a model with several numbered them for a reason.
 */
export function modelCameraEntries(cameras: readonly PreviewCamera[]): ModelCameraEntry[] {
    if (cameras.length === 0) {
        return [NONE];
    }

    return cameras.map((camera, at) => ({
        id: `camera:${at}`,
        label: camera.name,
        // The Get_Bone_Position tip that used to ride here was the best thing in the tooltip and
        // the reason nobody read the tooltip. It is in the docs for `label` above, which is where
        // someone looking for it will be.
        title: `Look from ${camera.name}, the author's own camera`,
        disabled: false,
        position: camera.position ?? null,
        target: camera.target ?? null,
    }));
}


/** One entry in the camera group on the stage - a preset, or a camera the model carries. */
export interface CameraViewOption {
    id: string;
    label: string;
    title: string;
    disabled: boolean;
}

/**
 * Every camera the reader can be looking from, as ONE list.
 *
 * The stage drew this as two runs of independent toggles, each button carrying `aria-pressed`. That
 * is the markup for switches that hold or release on their own, and these do not: `cameraView` is a
 * single value, so choosing any one of them releases whichever was chosen before. Said as a
 * radiogroup instead, the control matches what it does, and a screen reader stops announcing four
 * switches where there is one choice.
 *
 * The presets come first because they apply to any subject; the model's own cameras follow because
 * they belong to this file. When it carries none, {@link modelCameraEntries} supplies the one
 * disabled entry that says so - dropped, it would teach nobody the feature exists.
 *
 * Takes the presets rather than owning them: their view ids are the viewport's vocabulary, and
 * importing that module here would pull three.js into a test that only compares strings.
 */
export function cameraViewOptions(
    presets: readonly { view: string; label: string; title: string }[],
    cameras: readonly PreviewCamera[],
): CameraViewOption[] {
    return [
        ...presets.map(preset => ({
            id: preset.view,
            label: preset.label,
            title: preset.title,
            disabled: false,
        })),
        ...modelCameraEntries(cameras),
    ];
}

/** A camera pose in the SCENE's axes, ready for `applyCameraPose`. */
export interface CameraPose {
    position: { x: number; y: number; z: number };
    target: { x: number; y: number; z: number };
}

/**
 * A declared camera as the viewport can use it, or null when it is not usable.
 *
 * The conversion is the whole point. `PreviewCamera` carries MODEL space - Alamo, Z up - and the
 * exporter turns the geometry into glTF's Y up with a rotation on the root node, so a pose applied
 * raw lands a quarter turn out. Measured on `RB_CommandCenter`: the eye went to (-375, -372, 213)
 * rather than (-375, 213, 372), which puts it under the floor looking back at the model from the
 * wrong side. On screen that reads exactly like the eye and the target having been swapped, which
 * is what it was first reported as.
 *
 * The same turn the debris and the particle systems make - see `breakoffPose`. Stated here rather
 * than borrowed from there because that module is about wreckage and this one is about cameras;
 * what they share is the axis convention, not a purpose.
 *
 * Null rather than a guess when either end is missing or malformed: a camera aimed at the origin
 * because half its pose was absent is worse than one that politely does nothing.
 */
export function cameraPose(camera: {
    position?: readonly number[] | null;
    target?: readonly number[] | null;
}): CameraPose | null {
    const at = (v: readonly number[] | null | undefined): CameraPose['position'] | null =>
        v === null || v === undefined || v.length < 3 || v.some(n => !Number.isFinite(n))
            ? null
            // Alamo (x, y, z) -> scene (x, z, -y). The `+ 0` normalises the negative zero that
            // -0 produces, which reads as a real value everywhere it surfaces.
            : { x: v[0], y: v[2], z: -v[1] + 0 };

    const position = at(camera.position);
    const target = at(camera.target);

    return position === null || target === null ? null : { position, target };
}
