// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import type {
    PreviewBreakoffProp,
    PreviewHardpoint,
    PreviewVector3,
} from "../../protocol/modelPreview";

import {
    ALAMO_FRAME_SECONDS,
    type BreakoffOrientation,
    breakoffAnchor,
    breakoffFor,
    breakoffLifetime,
    breakoffPose,
} from "./breakoff";

const PROP: PreviewBreakoffProp = {
    id: "Hardpoint_Breakoff_Star_Dest_Weapon_FL",
    modelRef: "EV_StarDestroyer_HP00_L-F.alo",
    resolved: true,
    movementVector: { x: 0, y: -20, z: 0 },
    facingRotateVector: { x: 0, y: 0, z: 90 },
    minLifetimeSeconds: 15,
    maxLifetimeSeconds: 25,
    attachedParticle: "p_smoke",
    deathExplosions: null,
    removeUponDeath: true,
    animations: [],
    particles: [],
};

function hardpoint(over: Partial<PreviewHardpoint> = {}): PreviewHardpoint {
    return {
        id: "HP_FL",
        partId: "hp:HP_FL",
        attachBone: "HP_F-L_BONE",
        isDestroyable: true,
        isTargetable: true,
        deathBreakoffProp: "Hardpoint_Breakoff_Star_Dest_Weapon_FL",
        engineDeathHidesEngineParticles: false,
        ...over,
    };
}

describe("breakoffFor", () => {
    it("finds the prop a destroyed hardpoint names", () => {
        // 167 of foc's 355 hardpoints name one, 147 distinct. Until now the name travelled and
        // nothing on the client read it, so destroying a hardpoint dropped nothing at all.
        assert.equal(breakoffFor(hardpoint(), [PROP])?.id, PROP.id);
    });

    it("matches the name however either side cased it", () => {
        assert.equal(
            breakoffFor(
                hardpoint({
                    deathBreakoffProp: "hardpoint_breakoff_star_dest_weapon_fl",
                }),
                [PROP],
            )?.id,
            PROP.id,
        );
    });

    it("is nothing for a hardpoint that names none", () => {
        // 188 of the 355 do not, and they simply vanish - which is what the engine does too.
        assert.equal(
            breakoffFor(hardpoint({ deathBreakoffProp: null }), [PROP]),
            null,
        );
    });

    it("is nothing when the prop is named but never defined", () => {
        // Still listed on the scene with `resolved: false`, so the author sees the typo - but there
        // is no model to drop.
        assert.equal(
            breakoffFor(hardpoint(), [{ ...PROP, resolved: false }]),
            null,
        );
        assert.equal(
            breakoffFor(hardpoint(), [{ ...PROP, modelRef: null }]),
            null,
        );
    });
});

describe("breakoffLifetime", () => {
    it("takes the midpoint of the range the prop declares", () => {
        // The engine randomises between the two. A preview picks the middle instead: a debris field
        // that lasted a different time on every run would make two looks at the same hardpoint
        // disagree, and there is nothing to be learned from the dice.
        assert.equal(breakoffLifetime(PROP), 20);
    });

    it("copes with only one end declared", () => {
        assert.equal(
            breakoffLifetime({ ...PROP, minLifetimeSeconds: null }),
            25,
        );
        assert.equal(
            breakoffLifetime({ ...PROP, maxLifetimeSeconds: null }),
            15,
        );
    });

    it("falls back to something watchable when neither is", () => {
        const bare = {
            ...PROP,
            minLifetimeSeconds: null,
            maxLifetimeSeconds: null,
        };

        assert.ok(breakoffLifetime(bare) > 0);
    });
});

describe("breakoffPose", () => {
    // The declared vector is per LOGIC FRAME, so a second of drift is 30 of them. Reading it as
    // units per second is what made every wreck crawl: the shipped props declare magnitudes from
    // 0.42 to 0.71 across all 90 of them, which at one unit per second moves a piece of a
    // 600-unit hull ten units over its whole 20-second life - visually stationary.
    const perSecond = 1 / ALAMO_FRAME_SECONDS;

    /** A vector turned by a quaternion: what the wreck's orientation does to one of its own axes. */
    function turned(q: BreakoffOrientation, v: PreviewVector3): PreviewVector3 {
        const tx = 2 * (q.y * v.z - q.z * v.y);
        const ty = 2 * (q.z * v.x - q.x * v.z);
        const tz = 2 * (q.x * v.y - q.y * v.x);

        return {
            x: v.x + q.w * tx + (q.y * tz - q.z * ty),
            y: v.y + q.w * ty + (q.z * tx - q.x * tz),
            z: v.z + q.w * tz + (q.x * ty - q.y * tx),
        };
    }

    function near(actual: PreviewVector3, expected: PreviewVector3): void {
        const off = Math.max(
            Math.abs(actual.x - expected.x),
            Math.abs(actual.y - expected.y),
            Math.abs(actual.z - expected.z),
        );
        assert.ok(
            off < 1e-6,
            `expected ${JSON.stringify(expected)}, was ${JSON.stringify(actual)}`,
        );
    }

    /** A prop that drifts or turns by one vector a frame and does nothing else. */
    function prop(
        movementVector: PreviewVector3 | null,
        facingRotateVector: PreviewVector3 | null = null,
    ): PreviewBreakoffProp {
        return { ...PROP, movementVector, facingRotateVector };
    }

    // The scene's axes on a ship at facing 0: X is its left, Y is up, Z is its bow.
    const LEFT = { x: 1, y: 0, z: 0 };
    const UP = { x: 0, y: 1, z: 0 };
    const BOW = { x: 0, y: 0, z: 1 };
    // The wreck's own axes, as its file has them: Alamo's, with the bow down -Y and up along Z.
    const FILE_LEFT = { x: 1, y: 0, z: 0 };
    const FILE_BOW = { x: 0, y: -1, z: 0 };
    const FILE_UP = { x: 0, y: 0, z: 1 };

    it("drifts along the movement vector, in units per logic frame", () => {
        const pose = breakoffPose(PROP, 2);

        // The prop declares -20 on world Y, which is the right of a ship at facing 0.
        near(pose.offset, { x: -20 * 2 * perSecond, y: 0, z: 0 });
    });

    /**
     * The game adds the vector to the wreck's WORLD position, whatever way the ship heads, and a
     * ship at facing 0 heads along world +X. Applied in the model's own axes instead - which is the
     * game at a heading of 270 - the Star Destroyer's four weapon wrecks all slid across the hull
     * towards its centreline.
     */
    it("drifts along world X towards the bow of a ship at facing 0", () => {
        near(
            breakoffPose(prop({ x: 1, y: 0, z: 0 }), ALAMO_FRAME_SECONDS)
                .offset,
            BOW,
        );
    });

    it("drifts along world Y towards the left of a ship at facing 0", () => {
        near(
            breakoffPose(prop({ x: 0, y: 1, z: 0 }), ALAMO_FRAME_SECONDS)
                .offset,
            LEFT,
        );
    });

    it("drifts along world Z upwards", () => {
        near(
            breakoffPose(prop({ x: 0, y: 0, z: 1 }), ALAMO_FRAME_SECONDS)
                .offset,
            UP,
        );
    });

    it("sends the Star Destroyer's front left wreck astern and down, not across the hull", () => {
        const pose = breakoffPose(
            prop({ x: -0.2, y: 0, z: -0.5 }),
            ALAMO_FRAME_SECONDS,
        );

        near(pose.offset, { x: 0, y: -0.5, z: -0.2 });
    });

    /**
     * The game creates the wreck with its PARENT's facing, not with the orientation of the bone
     * the hardpoint hung on, so it starts standing exactly as the ship does.
     */
    it("stands as the ship does at the moment it breaks off", () => {
        const pose = breakoffPose(PROP, 0);

        near(pose.offset, { x: 0, y: 0, z: 0 });
        near(turned(pose.orientation, FILE_LEFT), LEFT);
        near(turned(pose.orientation, FILE_BOW), BOW);
        near(turned(pose.orientation, FILE_UP), UP);
    });

    it("turns on the facing vector, in degrees per logic frame", () => {
        // 90 a frame about Z: half a frame is 45 degrees, flat.
        const pose = breakoffPose(PROP, ALAMO_FRAME_SECONDS / 2);
        const half = Math.SQRT1_2;

        near(turned(pose.orientation, FILE_UP), UP);
        near(turned(pose.orientation, FILE_BOW), { x: half, y: 0, z: half });
    });

    /**
     * The three numbers are added to the wreck's three facing angles. The Z angle is a turn about
     * the up axis, the Y angle a pitch about the left axis, the X angle a roll about the heading.
     * Fed to the wreck's own axes after being turned into the scene's, the Y and Z rates changed
     * places: a wreck told to yaw flat tumbled end over end instead.
     */
    it("yaws flat on the Z angle, from the bow towards the left", () => {
        const pose = breakoffPose(
            prop(null, { x: 0, y: 0, z: 90 }),
            ALAMO_FRAME_SECONDS,
        );

        near(turned(pose.orientation, FILE_UP), UP);
        near(turned(pose.orientation, FILE_BOW), LEFT);
    });

    it("pitches on the Y angle, about the left axis", () => {
        const pose = breakoffPose(
            prop(null, { x: 0, y: 90, z: 0 }),
            ALAMO_FRAME_SECONDS,
        );

        near(turned(pose.orientation, FILE_LEFT), LEFT);
        near(turned(pose.orientation, FILE_UP), BOW);
    });

    it("rolls on the X angle, about the heading", () => {
        const pose = breakoffPose(
            prop(null, { x: 90, y: 0, z: 0 }),
            ALAMO_FRAME_SECONDS,
        );

        near(turned(pose.orientation, FILE_BOW), BOW);
        near(turned(pose.orientation, FILE_UP), { x: -1, y: 0, z: 0 });
    });

    it("applies the roll first, the pitch second and the yaw last", () => {
        const pose = breakoffPose(
            prop(null, { x: 0, y: 90, z: 90 }),
            ALAMO_FRAME_SECONDS,
        );

        // Pitched, the wreck's up points at the bow; yawed after that, at the left. The other way
        // round the yaw would leave its up alone and the pitch would put it on the bow.
        near(turned(pose.orientation, FILE_UP), LEFT);
    });

    it("reads the frame length off the engine, not off a guess", () => {
        // `CONST_FRAME_TIME` in the shipped debug PDB, to the digits it stores.
        assert.equal(ALAMO_FRAME_SECONDS, 0.03333);
    });

    it("sits still where the prop declares no motion", () => {
        // A prop with no vectors is debris that simply stays where the hardpoint was, which is a
        // perfectly ordinary thing to author.
        const pose = breakoffPose(prop(null), 5);

        assert.deepEqual(pose.offset, { x: 0, y: 0, z: 0 });
        near(turned(pose.orientation, FILE_BOW), BOW);
        near(turned(pose.orientation, FILE_UP), UP);
    });
});

describe("breakoffAnchor, where the wreck is dropped", () => {
    /**
     * The hardpoint is already standing on the hull bone this hardpoint names, so its ROOT is that
     * place by construction - and asking for the bone by NAME inside the hardpoint is what went wrong.
     *
     * An Executor hardpoint model carries a copy of the WHOLE hull skeleton: 228 nodes, including
     * `HP_L_Trb00_Bone` itself. So the name resolved a second time, inside a part already standing
     * on that bone, and the offset was applied twice - `HP_EXECUTOR_LEFT_TURBO_00` dropped its
     * wreck 2093 units from the hardpoint it came off. A Star Destroyer's hardpoint carries 22 nodes and no
     * such bone, so the lookup missed and the silent fallback happened to give the right answer:
     * the error is invisible on every ship whose parts do not carry the hull's bones, and grows
     * with the bone's distance from the origin.
     */
    it("drops it on the HARDPOINT itself, never on a bone name inside the hardpoint", () => {
        assert.deepEqual(breakoffAnchor("hull", hardpoint()), {
            partId: "hp:HP_FL",
        });
    });

    /**
     * With no hardpoint there is nothing standing there, so the hull's own bone is the place. 32 of
     * foc's hardpoints are like this - a tractor beam or a fighter bay names a bone and no model.
     *
     * By NAME only, because `PreviewHardpoint` carries no bone index: repeated bone names would
     * still take the first. That is a gap in the DESCRIPTOR rather than here, and it is narrower
     * than it was - a hardpoint WITH a hardpoint no longer resolves a name at all.
     */
    it("falls back to the hull bone for a hardpoint with no model", () => {
        assert.deepEqual(breakoffAnchor("hull", hardpoint({ partId: null })), {
            partId: "hull",
            bone: "HP_F-L_BONE",
        });
    });

    /** A hardpoint naming neither a hardpoint nor a bone leaves the hull itself, which is recoverable. */
    it("leaves the hull for a hardpoint naming no bone either", () => {
        assert.deepEqual(
            breakoffAnchor(
                "hull",
                hardpoint({ partId: null, attachBone: undefined }),
            ),
            { partId: "hull" },
        );
    });
});
