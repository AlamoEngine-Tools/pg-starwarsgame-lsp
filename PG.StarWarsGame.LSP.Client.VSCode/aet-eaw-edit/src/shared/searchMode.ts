// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How a search pattern is read. Here rather than with the matcher, because a stored search keeps
// its mode beside its text and the host reads that state back.

export type SearchMode = "text" | "wildcard" | "regex";

/** A stored mode read back. Anything unreadable is plain text, which is what a search starts as. */
export function searchModeFrom(stored: unknown): SearchMode {
    return stored === "wildcard" || stored === "regex" ? stored : "text";
}
