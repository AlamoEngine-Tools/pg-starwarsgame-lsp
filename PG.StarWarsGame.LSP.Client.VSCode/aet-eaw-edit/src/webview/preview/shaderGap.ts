// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Whether the "no shader sources are reachable" note has anything to explain.
 *
 * The note is the explanation of a gap: translation is on, the scene asked for effects, and no
 * source came back for any of them. Without the middle condition it fired on every particle
 * system - those ask for no effect at all, their sprites being drawn by the preview's own shaders -
 * and an asteroid field opened with a note about shaders it never needed.
 */
export function shaderGapNotice(
    translatedOn: boolean,
    shaderRequested: boolean,
    anyShaderSource: boolean,
): boolean {
    return translatedOn && shaderRequested && !anyShaderSource;
}
