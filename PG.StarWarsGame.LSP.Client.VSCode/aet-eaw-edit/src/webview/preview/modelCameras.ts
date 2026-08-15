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
}

/** What the list says when the subject declares no camera of its own. */
const NONE: ModelCameraEntry = {
    id: 'camera:none',
    label: 'Author',
    title: 'This model does not carry a camera of its own. 447 of the shipped models do - they are '
        + 'a bone named Camera01 and one named Camera01.Target, aimed by whoever built the model.',
    disabled: true,
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
        title: `The author's own camera, ${camera.name}, as the model declares it. Readable from `
            + `script with Get_Bone_Position("${camera.name}").`,
        disabled: false,
    }));
}

