// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The extension's own settings, as the host and its commands read them.

import * as vscode from "vscode";

/** One section of the extension's settings, by its name under `aet-eaw-edit`. */
export function cfg(section: string): vscode.WorkspaceConfiguration {
    return vscode.workspace.getConfiguration(`aet-eaw-edit.${section}`);
}

/**
 * The experimental features, sent to the server via initializationOptions; everything else is always
 * on. The server's FeatureFlags record (Core\Configuration\FeatureFlags.cs) defaults each to true, so
 * the fallbacks here carry the user-facing default - off - and must mirror package.json. Restart-based:
 * the config listener in activate() restarts the server when an `aet-eaw-edit.experimental` value
 * changes.
 */
export function resolveFeatureFlags(): {
    storyEditing: boolean;
    storySimulator: boolean;
} {
    const experimental = cfg("experimental");
    return {
        storyEditing: experimental.get<boolean>("storyEditing", false),
        storySimulator: experimental.get<boolean>("storySimulator", false),
    };
}
