// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The home directory in a line of log text, written as `~`. The output channels are read on screen
// and pasted into public issues, and a path under the home directory carries the user's name: the
// bundled server, the log folder, a project kept in the user's documents. Kept free of `vscode` so
// the unit harness can run it.

/**
 * `text` with every path under `home` starting at `~` instead.
 *
 * A path is taken with either separator, with the drive's colon as a file URI encodes it, and
 * without regard to case where the file system has none. A folder that only starts like the home
 * directory (`someone-else` beside `someone`) is another folder and stays.
 *
 * @param home The home directory; an empty one changes nothing.
 * @param ignoreCase Whether the file system matches names without regard to case.
 */
export function homeAsTilde(
    text: string,
    home: string,
    ignoreCase: boolean,
): string {
    const parts = home
        .replace(/[\\/]+$/, "")
        .split(/[\\/]/)
        .map((part) =>
            part
                .replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
                .replace(/:/g, "(?::|%3A)"),
        );
    if (parts.every((part) => part === "")) {
        return text;
    }
    const path = new RegExp(
        `${parts.join("[\\\\/]")}(?=[\\\\/]|[^\\w.-]|$)`,
        ignoreCase ? "gi" : "g",
    );
    return text.replace(path, "~");
}
