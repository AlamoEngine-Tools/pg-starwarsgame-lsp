// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the preview hands the inspector tab, and what the bulk tables are keyed by.
//
// The inspector is its own editor tab rather than a flyout, so the facts have to cross a
// postMessage to get there. They are the RAW sources, not the rendered panels: `inspector.ts` is
// deliberately free of three.js, so the tab imports the same `inspectPanels` and lays them out
// itself. Sending the rendered panels instead would make the layout two implementations that
// drift, which is the mistake `WebviewPanelHost` was written to undo.
//
// Everything here is plain JSON by construction - the viewport reads mesh facts off the loaded
// glTF and the material extras arrive as parsed JSON - so the whole subject survives the structured
// clone without a conversion step.

import { type Inspection, type MeshInspection } from './inspector';
import { type ModelDetail } from '../../protocol/modelPreview';

/** One tree row, as everything that describes it. */
export interface InspectorSubject {
    /** The row's stable id from `stampTreeKeys` - `part:bone:ordinal`, not a three.js uuid. */
    rowId: string;
    /** The row's facts. A merged bone/mesh row carries both, and a vanished row carries none. */
    sources: Inspection[];
    /** What the server knows about the file, which several sections read from. */
    modelDetail: ModelDetail | null;
    /** The model the row belongs to - the other half of the geometry key. */
    modelReference: string | null;
}

/** Everything `aet/getSubMeshGeometry` needs to name one sub-mesh. */
export interface GeometryKey {
    modelReference: string;
    meshIndex: number;
    subMeshIndex: number;
}

/**
 * Which sub-mesh's bulk tables this row can fetch, or null when it has none.
 *
 * A bone or a particle system has no geometry of its own, and a mesh the exporter could not index
 * cannot be joined back to the file - so all three answer null rather than fetching the wrong
 * rows under the right name.
 */
export function geometryKeyOf(subject: InspectorSubject): GeometryKey | null {
    const mesh = subject.sources
        .find((source): source is MeshInspection => source.kind === 'mesh');

    // Explicitly against undefined: sub-mesh 0 is the first sub-mesh and the commonest case, so a
    // truthiness check here drops the majority of rows.
    if (mesh?.meshIndex === undefined || mesh.subMeshIndex === undefined
        || subject.modelReference === null) {
        return null;
    }

    return {
        modelReference: subject.modelReference,
        meshIndex: mesh.meshIndex,
        subMeshIndex: mesh.subMeshIndex,
    };
}

/**
 * Whether two keys name the same sub-mesh.
 *
 * The page a reader is on belongs to the KEY, not to the object the subject arrived in. Every row
 * has its facts rebuilt whenever the tree does - a visibility checkbox is enough - so the subject
 * turns up as a new object saying exactly the same thing, and comparing by identity threw away the
 * table the reader was three pages into.
 */
export function sameGeometryKey(a: GeometryKey | null, b: GeometryKey | null): boolean {
    if (a === null || b === null) {
        return a === b;
    }

    return a.modelReference === b.modelReference
        && a.meshIndex === b.meshIndex
        && a.subMeshIndex === b.subMeshIndex;
}
