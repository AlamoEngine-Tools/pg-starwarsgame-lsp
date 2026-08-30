// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which meanings the EDITOR already has a glyph for.
//
// The preview draws Tabler because codicons has no 3D vocabulary - no ground plane, no skeleton, no
// mesh, no LOD, no axes - and hand-drawing that set would be more work and worse. That argument
// covers the DOMAIN and stops there.
//
// An editor action is not domain. "Go to where this is defined" is something the extension already
// does in the story graph, drawn as codicon `go-to-file`, and answering the same question with a
// different picture in this panel makes one action look like two. The seam is the right place to
// settle that: a call site still asks for a meaning, and this decides which set answers it.

/** Meanings that keep the editor's glyph rather than taking Tabler's. */
const CODICONS = {
    definition: 'go-to-file',
} as const;

export type CodiconMeaning = keyof typeof CODICONS;

/** Every meaning answered by a codicon, for the test that keeps this list honest. */
export const CODICON_NAMES = Object.keys(CODICONS) as CodiconMeaning[];

/** The codicon name for a meaning, or null when Tabler answers it. */
export function codiconFor(name: string): string | null {
    return CODICONS[name as CodiconMeaning] ?? null;
}
