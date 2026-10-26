// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Whether the "no shader sources are reachable" note has anything to explain.
 *
 * The note is the explanation of a gap: translation is on, the scene asked for effects, and no
 * source came back for any of them. Without the middle condition it fired on every scene that
 * named no effect. A particle system does name its emitters' `Prim*.fx` effects since they are
 * drawn through the engine's own, so under the Game renderer with no sources the note is as right
 * on an asteroid field as on a hull.
 */
export function shaderGapNotice(
    translatedOn: boolean,
    shaderRequested: boolean,
    anyShaderSource: boolean,
): boolean {
    return translatedOn && shaderRequested && !anyShaderSource;
}
