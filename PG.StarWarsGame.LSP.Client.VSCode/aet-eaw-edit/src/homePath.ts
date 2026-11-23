// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The home directory in log text, written as `~`. The output channels are read on screen and pasted
// into public issues, and a path under the home directory carries the user's name: the bundled
// server, the log folder, a temporary folder, a project kept in the user's documents. Kept free of
// `vscode` so the unit harness can run it.

/**
 * A function that writes every path under `home` as starting at `~`.
 *
 * A path is taken with either separator, with its backslashes doubled as JSON writes them, with the
 * drive's colon as a file URI encodes it, and without regard to case where the file system has
 * none. A folder that only starts like the home directory (`someone-else` beside `someone`) is
 * another folder and stays.
 *
 * @param home The home directory; an empty one changes nothing.
 * @param ignoreCase Whether the file system matches names without regard to case.
 */
export function homeScrubber(
    home: string,
    ignoreCase: boolean,
): (text: string) => string {
    const parts = home
        .replace(/[\\/]+$/, "")
        .split(/[\\/]/)
        .map((part) =>
            part
                .replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
                .replace(/:/g, "(?::|%3A)"),
        );
    if (parts.every((part) => part === "")) {
        return (text) => text;
    }
    const path = new RegExp(
        `${parts.join("(?:\\\\{1,2}|/)")}(?=[\\\\/]|[^\\w.-]|$)`,
        ignoreCase ? "gi" : "g",
    );
    return (text) => text.replace(path, "~");
}

/** `text` with every path under `home` starting at `~` instead; see {@link homeScrubber}. */
export function homeAsTilde(
    text: string,
    home: string,
    ignoreCase: boolean,
): string {
    return homeScrubber(home, ignoreCase)(text);
}

/**
 * `channel`, with every text written to it passed through `scrub` first.
 *
 * Whatever method takes the text - a line, a log entry at any level, a replaced content - gets it
 * scrubbed, so a writer that is not ours (the language client writes the server's log messages and
 * its error stream) cannot write around it. An error is written as its scrubbed text; anything
 * else that is not text passes as it is. Properties are the channel's own, unchanged.
 */
export function scrubbed<T extends object>(
    channel: T,
    scrub: (text: string) => string,
): T {
    return new Proxy(channel, {
        get(target, key) {
            const value: unknown = Reflect.get(target, key, target);
            if (typeof value !== "function") {
                return value;
            }
            return (...args: unknown[]): unknown =>
                (value as (...given: unknown[]) => unknown).apply(
                    target,
                    args.map((arg) =>
                        typeof arg === "string"
                            ? scrub(arg)
                            : arg instanceof Error
                              ? scrub(arg.stack ?? String(arg))
                              : arg,
                    ),
                );
        },
    });
}
