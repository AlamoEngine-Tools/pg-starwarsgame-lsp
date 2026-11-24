// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The one rule for comparing two paths (#324).
//
// Windows hands the same file back under several spellings: either separator, any case, and a
// directory with or without its trailing separator. Every map keyed on a path, and every "is this
// file under that directory", has to fold all three, or two spellings of one file become two
// entries - two panels staging edits against one file, two watchers on one directory, a file no
// project owns.
//
// Pure, so it runs under the node test harness and compiles into the webviews as well.

/**
 * A path as it is compared: forward slashes, no trailing separator, lower case.
 *
 * Only ever a key. It is not a path to open - a drive's root loses its separator - so the spelling
 * that came in is the one handed on.
 */
export function pathKey(path: string): string {
    return path.replace(/\\/g, "/").replace(/\/+$/, "").toLowerCase();
}

/** Whether two spellings name one file or directory. */
export function samePath(a: string, b: string): boolean {
    return pathKey(a) === pathKey(b);
}

/**
 * Whether `path` is `directory` or lies under it. The match ends on a segment boundary, which is
 * what keeps `Rev2` out of a directory named `Rev`.
 */
export function isInside(path: string, directory: string): boolean {
    const target = pathKey(path);
    const d = pathKey(directory);
    return target === d || target.startsWith(d + "/");
}
