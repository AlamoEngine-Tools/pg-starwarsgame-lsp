// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How a search pattern is read: as plain text, with wildcards, or as a regular expression.
//
// It was the localisation grid's, and is one module since every search plate offers the same three
// modes inside its field. The match semantics are the grid's, unchanged: users have them in their
// fingers.

import { type SearchMode } from "../../shared/searchMode";

export { type SearchMode };

/** A pattern compiled once, plus why it could not be compiled. */
export interface CompiledMatcher {
    test: (text: string) => boolean;
    /** The engine's message for a regex that would not compile; undefined when the pattern is fine. */
    error?: string;
}

/**
 * Compiles a pattern into a predicate, reporting a regex that would not compile.
 *
 * An empty pattern matches everything. An invalid regex yields a matcher that matches nothing
 * rather than throwing: the box is half-typed most of the time it is being used, and an exception
 * would take the webview down. It reports the reason as well, because "matches nothing" and "you
 * have a typo" look identical on screen - an empty list - and the user has no other way to tell
 * them apart.
 */
export function compileMatcher(
    pattern: string,
    mode: SearchMode,
): CompiledMatcher {
    if (!pattern) {
        return { test: () => true };
    }

    if (mode === "regex") {
        try {
            const re = new RegExp(pattern, "i");
            return { test: (text) => re.test(text) };
        } catch (e) {
            return {
                test: () => false,
                error: e instanceof Error ? e.message : String(e),
            };
        }
    }

    if (mode === "wildcard") {
        // Metacharacters are escaped first so only * and ? are special - otherwise a '.' in a key
        // would match any character and quietly widen the filter. Nothing here can fail to compile.
        const escaped = pattern
            .replace(/[.+^${}()|[\]\\]/g, "\\$&")
            .replace(/\*/g, ".*")
            .replace(/\?/g, ".");
        const re = new RegExp(escaped, "i");
        return { test: (text) => re.test(text) };
    }

    const lower = pattern.toLowerCase();
    return { test: (text) => text.toLowerCase().includes(lower) };
}
