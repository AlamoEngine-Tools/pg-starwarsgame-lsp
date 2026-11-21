// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model preview's messages (#208): ModelPreviewPanel and modelPreview.tsx.
//
// The asset requests here are answered by NAME rather than through the shared id relay: the host keeps
// state between them - it asks for each texture once per scene, and bakes a GLB with the clips the
// scene reported - and the webview files each answer under the name it asked for.

import type { ProjectSettings } from "../../shared/projectSettings";
import type { SubjectState } from "../../shared/subjectState";
import type { ViewerSettings } from "../../shared/viewerSettings";
import type {
    GetModelDetailResult,
    GetParticleSystemResult,
    GetProjectileResult,
    GetShaderSourceResult,
    PreviewScene,
} from "../modelPreview";

// ── webview to host ─────────────────────────────────────────────────────────

/** The bundle is listening: the room, the features and the scene follow. */
export interface ModelPreviewReady {
    type: "ready";
}

/** The room the reader set up; kept for every preview. */
export interface ModelPreviewSetViewerSettings {
    type: "setViewerSettings";
    settings: ViewerSettings;
}

/** What this project remembers - the attacker, its presets, the faction colours. */
export interface ModelPreviewSetProjectSettings {
    type: "setProjectSettings";
    project: ProjectSettings;
}

/** This subject's own state, kept for the session only. */
export interface ModelPreviewSetSubjectState {
    type: "setSubjectState";
    state: SubjectState;
}

/** A rendered frame for the host to write where the reader chooses. */
export interface ModelPreviewSaveCapture {
    type: "saveCapture";
    /** A `data:image/png;base64,` URL. */
    dataUrl: string;
    fileName: string;
}

/** One part's geometry, as a GLB, answered with {@link ModelPreviewGlb}. */
export interface ModelPreviewRequestGlb {
    type: "requestGlb";
    modelReference: string;
    partId: string;
    attachToPartId?: string;
    attachBone?: string;
    /** The part's own clips; empty or absent bakes the clips the scene reported. */
    animations?: string[];
    /** The version of this model the webview already holds, if it holds one. */
    have?: string;
    /** The webview's number for this request, echoed on the reply. */
    request?: number;
}

/** A model's detail for the tree and the inspector, answered with {@link ModelPreviewModelDetail}. */
export interface ModelPreviewRequestModelDetail {
    type: "requestModelDetail";
    modelReference: string;
}

/**
 * A texture by name, answered with {@link ModelPreviewTexture}; asked for once per scene, with the
 * version the webview already holds, if it holds one.
 */
export interface ModelPreviewRequestTexture {
    type: "requestTexture";
    name: string;
    have?: string;
    /** The webview's number for this request, echoed on the reply. */
    request?: number;
}

/** A particle system by name, answered with {@link ModelPreviewParticleSystem}. */
export interface ModelPreviewRequestParticleSystem {
    type: "requestParticleSystem";
    name: string;
}

/** A shader's source by name, answered with {@link ModelPreviewShader}. */
export interface ModelPreviewRequestShader {
    type: "requestShader";
    name: string;
}

/** A projectile's values by name, answered with {@link ModelPreviewProjectile}. */
export interface ModelPreviewRequestProjectile {
    type: "requestProjectile";
    name: string;
}

/**
 * Opens the inspector on a row. The subject is built and read by the two webviews; the host only
 * passes it on, so it is opaque here and the webview names its type.
 */
export interface ModelPreviewOpenInspector<Subject = unknown> {
    type: "openInspector";
    subject: Subject;
}

/** Retargets an open inspector; opens none. */
export interface ModelPreviewUpdateInspector<Subject = unknown> {
    type: "updateInspector";
    subject: Subject;
}

/** Runs the shader setup. */
export interface ModelPreviewObtainShaders {
    type: "obtainShaders";
}

/** Goes to the definition of a name of a given type, for a row no document stands behind. */
export interface ModelPreviewRevealDefinition {
    type: "revealDefinition";
    value: string;
    referenceType: string;
}

/** Hands the subject's file to an external editor. */
export interface ModelPreviewOpenExternal {
    type: "openExternal";
    tool: "model" | "particles";
}

export type ModelPreviewToHost<Subject = unknown> =
    | ModelPreviewReady
    | ModelPreviewSetViewerSettings
    | ModelPreviewSetProjectSettings
    | ModelPreviewSetSubjectState
    | ModelPreviewSaveCapture
    | ModelPreviewRequestGlb
    | ModelPreviewRequestModelDetail
    | ModelPreviewRequestTexture
    | ModelPreviewRequestParticleSystem
    | ModelPreviewRequestShader
    | ModelPreviewRequestProjectile
    | ModelPreviewOpenInspector<Subject>
    | ModelPreviewUpdateInspector<Subject>
    | ModelPreviewObtainShaders
    | ModelPreviewRevealDefinition
    | ModelPreviewOpenExternal;

// ── host to webview ─────────────────────────────────────────────────────────

/**
 * The scene. `refresh` marks a re-read of the same subject after the tree behind it changed, as
 * opposed to a subject being opened: the reader's camera stays.
 */
export interface ModelPreviewScene {
    type: "scene";
    scene: PreviewScene;
    refresh?: boolean;
}

/**
 * One model's geometry, as bytes.
 *
 * `data` is the GLB itself: the host collected it from the server's transfer pipe and posts it as a
 * typed array, so nothing on the way to the webview is base64 text. Absent when the model did not
 * resolve, would not parse, or did not arrive - `error` then says which, in words to show.
 */
export interface ModelPreviewGlb {
    type: "glb";
    partId: string;
    attachToPartId?: string | null;
    attachBone?: string | null;
    /** The clips baked into the GLB. */
    animations: string[];
    data?: Uint8Array | null;
    error?: string | null;
    /** The version of the model, to ask with next time. */
    version?: string | null;
    /** The server says the copy the webview asked with still stands; no `data` is sent. */
    unchanged?: boolean;
    /** The number of the request this answers. */
    request?: number;
}

/**
 * One texture's file, as bytes and undecoded.
 *
 * `format` is the extension the server FOUND - `dds` or `tga` - which is frequently not the one the
 * model named. `data` is absent when the texture did not resolve or did not arrive; `error` then
 * says which.
 */
export interface ModelPreviewTexture {
    type: "texture";
    name: string;
    format?: string | null;
    data?: Uint8Array | null;
    error?: string | null;
    /** The version of the texture, to ask with next time. */
    version?: string | null;
    /** The server says the copy the webview asked with still stands; no `data` is sent. */
    unchanged?: boolean;
    /** The number of the request this answers. */
    request?: number;
}

export interface ModelPreviewModelDetail {
    type: "modelDetail";
    modelReference: string;
    result: GetModelDetailResult;
}

/** The reader closed the inspector tab the preview opened. */
export interface ModelPreviewInspectorClosed {
    type: "inspectorClosed";
}

export interface ModelPreviewParticleSystem {
    type: "particleSystem";
    name: string;
    result: GetParticleSystemResult;
}

export interface ModelPreviewShader {
    type: "shader";
    name: string;
    result: GetShaderSourceResult;
}

/**
 * The room, this project's settings and this subject's state from earlier in the session. They arrive
 * before the scene, so nothing snaps into place a frame after the model appears.
 */
export interface ModelPreviewViewerSettings {
    type: "viewerSettings";
    settings: ViewerSettings;
    project: ProjectSettings;
    subject: SubjectState | null;
}

export interface ModelPreviewProjectile {
    type: "projectile";
    name: string;
    result: GetProjectileResult;
}

/** The optional mechanics this preview shows; pushed on open and whenever the setting changes. */
export interface ModelPreviewFeatures {
    type: "previewFeatures";
    features: { energyPool: boolean };
}

/**
 * Where effects are read from changed and the server restarted on it (#395): every effect and header
 * the preview holds, and every answer that one was not there, is asked for again.
 */
export interface ModelPreviewRefetchEffects {
    type: "refetchEffects";
}

export type ModelPreviewToWebview =
    | ModelPreviewScene
    | ModelPreviewGlb
    | ModelPreviewTexture
    | ModelPreviewModelDetail
    | ModelPreviewInspectorClosed
    | ModelPreviewParticleSystem
    | ModelPreviewShader
    | ModelPreviewViewerSettings
    | ModelPreviewProjectile
    | ModelPreviewFeatures
    | ModelPreviewRefetchEffects;
