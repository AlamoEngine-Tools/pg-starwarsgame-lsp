// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The setting that says which of a story type's two names the story graph's palette shows.
//
// Shared because both sides read it: the extension host reads the setting, which only it can, and
// the webview is told the mode. Free of `vscode` so both can import it.

/** The setting's key. */
export const TYPE_NAMES_SETTING = "aet-eaw-edit.editors.storyTypeNames";

/**
 * Which name a tile shows: the schema's display name for the type - Time elapsed - or the name XML
 * writes - STORY_ELAPSED.
 */
export type TypeNameMode = "display" | "technical";

/** The setting's value as a mode. Anything that is not the technical choice is the default. */
export function typeNameMode(setting: unknown): TypeNameMode {
    return setting === "technical" ? "technical" : "display";
}
