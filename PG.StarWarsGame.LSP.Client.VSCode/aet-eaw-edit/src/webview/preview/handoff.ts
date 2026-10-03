// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which external editor a preview hands its file to, and whether it can.
//
// One tool per scene, chosen by what the file IS: AloViewer for a model, the Particle Editor for a
// particle system. Both share the .alo extension, so the scene kind - which the server decides by
// reading the root chunk - is the only thing that tells them apart. An assembled object is built
// out of several files and has no single one to hand over, so it gets no hand-off at all.

import {PREVIEW_SCENE_KIND, type PreviewSourceFile} from '../../protocol/modelPreview';
import {type IconName} from '../shared/Icon';

/** The hand-off a scene offers. */
export interface Handoff {
    /** The tool, in the host's terms: `model` is AloViewer, `particles` the Particle Editor. */
    tool: 'model' | 'particles';
    icon: IconName;
    title: string;
    /** Why it cannot be pressed, or undefined when it can. */
    disabledReason?: string;
}

/** The hand-off for a scene of `kind` whose file resolved to `source`, or null for none. */
export function handoffFor(kind: string, source: PreviewSourceFile | null | undefined): Handoff | null {
    const base: Handoff | null =
        kind === PREVIEW_SCENE_KIND.model
            ? {tool: 'model', icon: 'open', title: 'Open in AloViewer'}
            : kind === PREVIEW_SCENE_KIND.particle
                ? {tool: 'particles', icon: 'bloom', title: 'Open in the Particle Editor'}
                : null;

    if (base === null) {
        return null;
    }

    // Disabled rather than extracted. A copy written out of the MEG would let the tool open it,
    // but an edit made there goes nowhere the game reads - worth building only together with a
    // way to put the file into the mod. Left for a later release.
    if (source?.packed === true) {
        return {...base, disabledReason: 'Packed in a MEG archive'};
    }

    if (source === null || source === undefined || source.path === null) {
        return {...base, disabledReason: 'No file to open'};
    }

    return base;
}
