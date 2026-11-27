// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The game directory settings, checked before the server starts. Pure - no `vscode` import - so the
// check is unit-tested.

import * as l10n from "@vscode/l10n";

/**
 * Why a game directory setting names no game, or undefined when it does or is not set (#393).
 *
 * The setting asks for the folder that holds the game's `Data`. Set to `Data` itself, the server
 * looked for the game's archives in `Data\Data`, found none, and said nothing about it. Either
 * separator, since an install on Linux names its folders with forward slashes.
 */
export function gameDirectoryProblem(
    setting: string,
    directory: string,
    isDirectory: (folder: string) => boolean,
): string | undefined {
    const trimmed = directory.replace(/[\\/]+$/, "");
    if (!trimmed) {
        return undefined;
    }
    const separator = trimmed.includes("\\") ? "\\" : "/";
    if (isDirectory(trimmed + separator + "Data")) {
        return undefined;
    }

    const cut = Math.max(trimmed.lastIndexOf("\\"), trimmed.lastIndexOf("/"));
    const name = trimmed.slice(cut + 1);
    if (cut > 0 && name.toLowerCase() === "data") {
        return l10n.t(
            'EaWEdit: "{0}" points at the Data folder itself - {1}. Set it to the folder above - {2}',
            setting,
            directory,
            trimmed.slice(0, cut),
        );
    }
    return l10n.t(
        'EaWEdit: "{0}" holds no Data folder - {1}. Fix it, or clear it to find the game by itself',
        setting,
        directory,
    );
}
