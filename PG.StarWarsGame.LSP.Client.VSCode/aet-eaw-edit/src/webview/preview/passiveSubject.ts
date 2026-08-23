// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a PASSIVE subject brings with it, and when its effects run.
//
// A death clone and a piece of wreckage are their own models, not further pieces of the one being
// previewed. The server describes each as a MODEL - it cannot know what the client will call the
// instance it loads, and the same prop can be dropped by two mirrored mounts at once - so the
// descriptor is re-homed onto the part here.
//
// Kept free of three.js so the rules can be read against the XML they come from.

import type { PreviewParticle } from '../../protocol/modelPreview';

/**
 * One passive subject's effects, re-homed onto the part that was actually loaded.
 *
 * Both halves of the identity are rewritten. `partId` is where the effect hangs, which only the
 * client knows; `id` has to be unique across the whole scene, and a wreck's descriptor ids are the
 * MODEL's - two mounts shedding the same prop would collide on every one of them.
 */
export function passiveEffects(
    particles: readonly PreviewParticle[], partId: string,
): PreviewParticle[] {
    return particles.map((particle, at) => ({ ...particle, partId, id: `${partId}#${at}` }));
}

/**
 * Whether one of a passive subject's effects is running.
 *
 * Deliberately NOT the subject's rule. The opening rules hold a hull's ungated fires and dust back
 * until the reader asks for them, and they are about the FIRST MOMENT of a preview - see
 * `playsOnOpen`. A passive subject has no first moment: a death clone exists because the ship died
 * and its explosions ARE the death, and a burning piece of debris is on fire from the instant it
 * breaks off.
 *
 * Nor is it the damage rule. The gate joins a proxy to the hardpoint whose `Damage_Particles` bone
 * is its parent, which is a fact about the SUBJECT's skeleton; a passive subject has its own, so
 * the server sends it no owning hardpoints and everything it carries comes back ungated.
 *
 * What is left is the model's own switch, which has the last word everywhere else too. The ALT/LOD
 * gate still applies on top, in the viewport, exactly as it does for the subject.
 */
export function passiveEffectPlaysNow(effect: PreviewParticle): boolean {
    return effect.startsVisible;
}
