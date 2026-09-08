// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Flattening an effect and the headers it pulls in.
//
// The `.fx` files are thin - annotations, one `#include`, and techniques - so everything worth
// translating lives in the headers. There are 65 includes across the shipped set, and some headers
// include others.

/** Fetches a header's text by bare file name, or null when it cannot be found. */
export type ShaderReader = (name: string) => string | null;

export interface FlattenedShader {
    /** The effect and its headers, in include order, with the `#include` lines removed. */
    text: string;
    /** Headers that were pulled in, in the order they were first seen. */
    included: string[];
    /** Headers that could not be read. Their absence usually makes the result untranslatable. */
    missing: string[];
}

/**
 * Splices every `#include` into one body.
 *
 * Depth-first and in order, so a header's declarations precede the code that uses them - GLSL, like
 * HLSL, requires declaration before use. Each header is spliced ONCE however many times it is
 * included: the shipped headers include each other freely, and repeating one would redeclare every
 * uniform in it.
 */
export function flattenIncludes(source: string, read: ShaderReader): FlattenedShader {
    const included: string[] = [];
    const missing: string[] = [];
    const seen = new Set<string>();

    const expand = (text: string, stack: readonly string[]): string => {
        return text.replace(/^[ \t]*#include\s+"([^"]+)"[ \t]*$/gm, (_, rawName: string) => {
            const name = rawName.trim();
            const key = name.toLowerCase();

            // A cycle would otherwise recurse until the stack gives out. The headers do include each
            // other, so this is a real shape rather than a defensive flourish.
            if (stack.includes(key) || seen.has(key)) {
                return '';
            }

            const body = read(name);
            if (body === null) {
                missing.push(name);
                return '';
            }

            seen.add(key);
            included.push(name);

            return expand(body, [...stack, key]);
        });
    };

    return { text: expand(source, []), included, missing };
}
