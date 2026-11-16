// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model inspector's messages (#208): ModelInspectorPanel and modelInspector.tsx.
//
// The subject is built by the model preview's webview and only passed along by the host, which never
// reads it, so its type is a parameter: the host's side is `unknown`, the inspector's names it.

import type { GetSubMeshGeometryResult } from "../modelPreview";

/** The bundle is listening; the subject held back can go now. */
export interface ModelInspectorReady {
    type: "ready";
}

/** One page of a sub-mesh's geometry table. */
export interface RequestSubMeshGeometry {
    type: "requestSubMeshGeometry";
    modelReference: string;
    meshIndex: number;
    subMeshIndex: number;
    table: string;
    offset: number;
    count: number;
}

export type ModelInspectorToHost = ModelInspectorReady | RequestSubMeshGeometry;

/** What the inspector shows: the preview's pick, or null for nothing picked. */
export interface SetInspectorSubject<S = unknown> {
    type: "setSubject";
    subject: S | null;
}

/** A requested page, or why there is none. */
export interface SubMeshGeometry {
    type: "subMeshGeometry";
    result: GetSubMeshGeometryResult;
}

export type ModelInspectorToWebview<S = unknown> =
    SetInspectorSubject<S> | SubMeshGeometry;
