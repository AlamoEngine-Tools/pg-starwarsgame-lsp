// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which settings a running server cannot take without a restart. Kept free of `vscode` so it can be
// unit tested.
//
// The server reads its configuration once, from the initialization options, and never again: its
// configuration provider is loaded by the initialize handler alone and has no change handler. So
// every setting that travels there takes effect only when the server starts.

/** Answers whether a configuration change touches a section, as `affectsConfiguration` does. */
export type Affects = (section: string) => boolean;

/** Why the servers restart, for the log line. */
export type RestartReason = "folders" | "features" | "startup";

/**
 * The experimental flags, which travel in the initialization options, and the two executables,
 * picked when the process starts.
 */
const FEATURE_SETTINGS = [
    "aet-eaw-edit.experimental",
    "aet-eaw-edit.lsp.executable",
    "aet-eaw-edit.lua.analyzerPath",
];

/**
 * The two game folders the server resolves models, textures and every other asset through.
 *
 * #395: a change reached neither the running server nor an open preview until the window was
 * reloaded.
 */
const GAME_FOLDER_SETTINGS = [
    "aet-eaw-edit.lsp.source.baseGameDirectory",
    "aet-eaw-edit.lsp.source.expansionDirectory",
];

/** Where an open preview's effects are read from: the base shader sources, and the game folders. */
const EFFECT_SETTINGS = [
    "aet-eaw-edit.shaders.directory",
    ...GAME_FOLDER_SETTINGS,
];

/**
 * The rest of what travels in the initialization options only (#395): the string table's
 * language, where the schema and the baseline come from, and the startup diagnostics sweep.
 */
const STARTUP_SETTINGS = [
    "aet-eaw-edit.lsp.localisation.language",
    "aet-eaw-edit.lsp.schema.source",
    "aet-eaw-edit.lsp.schema.url",
    "aet-eaw-edit.lsp.schema.localPath",
    "aet-eaw-edit.lsp.source.baseline.type",
    "aet-eaw-edit.lsp.source.baseline.url",
    "aet-eaw-edit.lsp.source.baseline.localPath",
    "aet-eaw-edit.diagnostics.workspaceOnStartup",
];

function touches(settings: readonly string[], affects: Affects): boolean {
    return settings.some((setting) => affects(setting));
}

/**
 * Why a configuration change needs the servers started again, or null when it does not.
 *
 * Folders first, because they also send open previews to read again; then the features, whose log
 * line UI specs match.
 */
export function restartReason(affects: Affects): RestartReason | null {
    if (touches(EFFECT_SETTINGS, affects)) {
        return "folders";
    }

    if (touches(FEATURE_SETTINGS, affects)) {
        return "features";
    }

    return touches(STARTUP_SETTINGS, affects) ? "startup" : null;
}

/** Whether a configuration change needs the servers started again to take effect. */
export function restartRequired(affects: Affects): boolean {
    return restartReason(affects) !== null;
}

/** Whether a configuration change moves where an open preview's effects are read from. */
export function previewEffectsChanged(affects: Affects): boolean {
    return touches(EFFECT_SETTINGS, affects);
}

/**
 * Whether a configuration change moves where an open preview's models and textures are read from,
 * so its subject has to be read again in full.
 */
export function previewAssetsChanged(affects: Affects): boolean {
    return touches(GAME_FOLDER_SETTINGS, affects);
}
