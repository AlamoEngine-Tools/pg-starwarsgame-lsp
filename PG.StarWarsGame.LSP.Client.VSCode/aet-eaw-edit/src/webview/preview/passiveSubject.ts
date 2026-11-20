// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a PASSIVE subject brings with it, and when its effects run.
//
// A death clone and a piece of wreckage are their own models, not further pieces of the one being
// previewed. The server describes each as a MODEL - it cannot know what the client will call the
// instance it loads, and the same prop can be dropped by two mirrored hardpoints at once - so the
// descriptor is re-homed onto the part here.
//
// Kept free of three.js so the rules can be read against the XML they come from.

import type { PreviewParticle } from "../../protocol/modelPreview";

/**
 * One passive subject's effects, re-homed onto the part that was actually loaded.
 *
 * Both halves of the identity are rewritten. `partId` is where the effect hangs, which only the
 * client knows; `id` has to be unique across the whole scene, and a wreck's descriptor ids are the
 * MODEL's - two hardpoints shedding the same prop would collide on every one of them.
 */
export function passiveEffects(
    particles: readonly PreviewParticle[],
    partId: string,
): PreviewParticle[] {
    return particles.map((particle, at) => ({
        ...particle,
        partId,
        id: `${partId}#${at}`,
    }));
}
