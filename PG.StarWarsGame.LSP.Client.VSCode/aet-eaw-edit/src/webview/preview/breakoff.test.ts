// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import type { PreviewBreakoffProp, PreviewHardpoint } from '../../protocol/modelPreview';

import {
    ALAMO_FRAME_SECONDS, breakoffFor, breakoffLifetime, breakoffPose, breakoffAnchor,
} from './breakoff';

const PROP: PreviewBreakoffProp = {
    id: 'Hardpoint_Breakoff_Star_Dest_Weapon_FL',
    modelRef: 'EV_StarDestroyer_HP00_L-F.alo',
    resolved: true,
    movementVector: { x: 0, y: -20, z: 0 },
    facingRotateVector: { x: 0, y: 0, z: 90 },
    minLifetimeSeconds: 15,
    maxLifetimeSeconds: 25,
    attachedParticle: 'p_smoke',
    deathExplosions: null,
    removeUponDeath: true,
    animations: [],
    particles: [],
};

function hardpoint(over: Partial<PreviewHardpoint> = {}): PreviewHardpoint {
    return {
        id: 'HP_FL',
        partId: 'hp:HP_FL',
        attachBone: 'HP_F-L_BONE',
        isDestroyable: true,
        isTargetable: true,
        deathBreakoffProp: 'Hardpoint_Breakoff_Star_Dest_Weapon_FL',
        engineDeathHidesEngineParticles: false,
        ...over,
    };
}

describe('breakoffFor', () => {
    it('finds the prop a destroyed mount names', () => {
        // 167 of foc's 355 hardpoints name one, 147 distinct. Until now the name travelled and
        // nothing on the client read it, so destroying a mount dropped nothing at all.
        assert.equal(breakoffFor(hardpoint(), [PROP])?.id, PROP.id);
    });

    it('matches the name however either side cased it', () => {
        assert.equal(
            breakoffFor(hardpoint({ deathBreakoffProp: 'hardpoint_breakoff_star_dest_weapon_fl' }),
                [PROP])?.id, PROP.id);
    });

    it('is nothing for a mount that names none', () => {
        // 188 of the 355 do not, and they simply vanish - which is what the engine does too.
        assert.equal(breakoffFor(hardpoint({ deathBreakoffProp: null }), [PROP]), null);
    });

    it('is nothing when the prop is named but never defined', () => {
        // Still listed on the scene with `resolved: false`, so the author sees the typo - but there
        // is no model to drop.
        assert.equal(breakoffFor(hardpoint(), [{ ...PROP, resolved: false }]), null);
        assert.equal(breakoffFor(hardpoint(), [{ ...PROP, modelRef: null }]), null);
    });
});

describe('breakoffLifetime', () => {
    it('takes the midpoint of the range the prop declares', () => {
        // The engine randomises between the two. A preview picks the middle instead: a debris field
        // that lasted a different time on every run would make two looks at the same mount
        // disagree, and there is nothing to be learned from the dice.
        assert.equal(breakoffLifetime(PROP), 20);
    });

    it('copes with only one end declared', () => {
        assert.equal(breakoffLifetime({ ...PROP, minLifetimeSeconds: null }), 25);
        assert.equal(breakoffLifetime({ ...PROP, maxLifetimeSeconds: null }), 15);
    });

    it('falls back to something watchable when neither is', () => {
        const bare = { ...PROP, minLifetimeSeconds: null, maxLifetimeSeconds: null };

        assert.ok(breakoffLifetime(bare) > 0);
    });
});

describe('breakoffPose', () => {
    // The declared vector is per LOGIC FRAME, so a second of drift is 30 of them. Reading it as
    // units per second is what made every wreck crawl: the shipped props declare magnitudes from
    // 0.42 to 0.71 across all 90 of them, which at one unit per second moves a piece of a
    // 600-unit hull ten units over its whole 20-second life - visually stationary.
    const perSecond = 1 / ALAMO_FRAME_SECONDS;

    it('drifts along the movement vector, in units per logic frame', () => {
        const pose = breakoffPose(PROP, 2);

        // The prop declares -20 on ALAMO Y, which is glTF -Z: `(x, y, z) -> (x, z, -y)`.
        assert.equal(pose.offset.x, 0);
        assert.equal(pose.offset.y, 0);
        assert.ok(Math.abs(pose.offset.z - (20 * 2 * perSecond)) < 1e-6);
    });

    it('turns on the facing vector, in degrees per logic frame', () => {
        const pose = breakoffPose(PROP, 2);

        // 90 about ALAMO Z - the up axis - is 90 about glTF Y.
        assert.ok(Math.abs(pose.rotation.y - (90 * 2 * perSecond)) < 1e-6);
    });

    /**
     * The declared vectors are in ALAMO axes, where Z is up; the scene runs in the model's glTF
     * space, where Y is. Applied raw, a piece told to fall DOWNWARD slid sideways instead - which
     * is the same axis confusion that `accelerationIn` exists for on the particle side.
     */
    it('turns the Alamo up axis into the glTF one', () => {
        const up = { ...PROP, movementVector: { x: 0, y: 0, z: 1 }, facingRotateVector: null };

        const pose = breakoffPose(up, ALAMO_FRAME_SECONDS);

        assert.ok(Math.abs(pose.offset.y - 1) < 1e-6, `y was ${pose.offset.y}`);
        assert.equal(pose.offset.x, 0);
        assert.equal(pose.offset.z, 0);
    });

    it('flips the Alamo depth axis, which is what sent debris the wrong way', () => {
        const forward = { ...PROP, movementVector: { x: 0, y: 1, z: 0 }, facingRotateVector: null };

        const pose = breakoffPose(forward, ALAMO_FRAME_SECONDS);

        assert.ok(Math.abs(pose.offset.z - -1) < 1e-6, `z was ${pose.offset.z}`);
        assert.equal(pose.offset.y, 0);
    });

    it('reads the frame length off the engine, not off a guess', () => {
        // `CONST_FRAME_TIME` in the shipped debug PDB, to the digits it stores.
        assert.equal(ALAMO_FRAME_SECONDS, 0.03333);
    });

    it('sits still where the prop declares no motion', () => {
        // A prop with no vectors is debris that simply stays where the mount was, which is a
        // perfectly ordinary thing to author.
        const still = { ...PROP, movementVector: null, facingRotateVector: null };
        const pose = breakoffPose(still, 5);

        assert.deepEqual(pose.offset, { x: 0, y: 0, z: 0 });
        assert.deepEqual(pose.rotation, { x: 0, y: 0, z: 0 });
    });

    it('is at the mount at the moment it breaks off', () => {
        const pose = breakoffPose(PROP, 0);

        assert.deepEqual(pose.offset, { x: 0, y: 0, z: 0 });
        assert.deepEqual(pose.rotation, { x: 0, y: 0, z: 0 });
    });
});

describe('breakoffAnchor, where the wreck is dropped', () => {
    /**
     * The mount is already standing on the hull bone this hardpoint names, so its ROOT is that
     * place by construction - and asking for the bone by NAME inside the mount is what went wrong.
     *
     * An Executor hardpoint model carries a copy of the WHOLE hull skeleton: 228 nodes, including
     * `HP_L_Trb00_Bone` itself. So the name resolved a second time, inside a part already standing
     * on that bone, and the offset was applied twice - `HP_EXECUTOR_LEFT_TURBO_00` dropped its
     * wreck 2093 units from the mount it came off. A Star Destroyer's mount carries 22 nodes and no
     * such bone, so the lookup missed and the silent fallback happened to give the right answer:
     * the error is invisible on every ship whose parts do not carry the hull's bones, and grows
     * with the bone's distance from the origin.
     */
    it('drops it on the MOUNT itself, never on a bone name inside the mount', () => {
        assert.deepEqual(breakoffAnchor('hull', hardpoint()), { partId: 'hp:HP_FL' });
    });

    /**
     * With no mount there is nothing standing there, so the hull's own bone is the place. 32 of
     * foc's hardpoints are like this - a tractor beam or a fighter bay names a bone and no model.
     *
     * By NAME only, because `PreviewHardpoint` carries no bone index: repeated bone names would
     * still take the first. That is a gap in the DESCRIPTOR rather than here, and it is narrower
     * than it was - a hardpoint WITH a mount no longer resolves a name at all.
     */
    it('falls back to the hull bone for a hardpoint with no model', () => {
        assert.deepEqual(
            breakoffAnchor('hull', hardpoint({ partId: null })),
            { partId: 'hull', bone: 'HP_F-L_BONE' });
    });

    /** A hardpoint naming neither a mount nor a bone leaves the hull itself, which is recoverable. */
    it('leaves the hull for a hardpoint naming no bone either', () => {
        assert.deepEqual(
            breakoffAnchor('hull', hardpoint({ partId: null, attachBone: undefined })),
            { partId: 'hull' });
    });
});
