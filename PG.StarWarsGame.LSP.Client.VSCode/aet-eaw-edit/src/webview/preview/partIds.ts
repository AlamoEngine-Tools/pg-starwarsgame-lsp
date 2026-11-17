// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The prefixes of the part ids the preview asks GLBs under, when the part is not the subject itself.

/**
 * Marks a GLB request as WRECKAGE rather than a scene part.
 *
 * The reply echoes `partId` and nothing else identifying, so the key rides on it. A breakoff added
 * as a part would be framed as if it were the subject and would never be cleaned up.
 */
export const BREAKOFF_PART = "breakoff:";

/**
 * The part id a death clone's model is loaded under.
 *
 * Prefixed like the breakoff props so the GLB handler can tell it from a scene part - the reply
 * carries only the id it was asked with, and a clone added as an ordinary part would be framed as
 * though it were the subject.
 */
export const DEATH_CLONE_PART = "deathclone:";

/** Prefix for the persistent effect a piece of wreckage trails, so it can be stopped by key. */
export const WRECK_EFFECT = "wreckfire:";
