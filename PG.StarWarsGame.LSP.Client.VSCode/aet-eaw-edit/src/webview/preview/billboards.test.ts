// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import {
    billboardLocalRotation, billboardTypeOf, billboardRotation, BILLBOARD_FACING, BILLBOARD_UP,
} from './billboards';

/** Where the quad's face points once `rotation` has been applied to it. */
function facing(rotation: THREE.Quaternion): THREE.Vector3 {
    return BILLBOARD_FACING.clone().applyQuaternion(rotation);
}

const CAMERA_AT = (x: number, y: number, z: number): THREE.Camera => {
    const camera = new THREE.PerspectiveCamera();
    camera.position.set(x, y, z);
    camera.lookAt(0, 0, 0);
    camera.updateMatrixWorld(true);
    return camera;
};

describe('billboardTypeOf', () => {
    it('reads the mode the exporter wrote', () => {
        const node = new THREE.Object3D();
        node.userData.alamoBillboard = 'ZAxisView';

        assert.equal(billboardTypeOf(node), 'ZAxisView');
    });

    /** 22598 of the corpus's 22866 bones write nothing at all, and must cost nothing. */
    it('gives nothing back for an ordinary bone', () => {
        assert.equal(billboardTypeOf(new THREE.Object3D()), null);
    });

    it('refuses a mode this build does not know rather than guessing one', () => {
        const node = new THREE.Object3D();
        node.userData.alamoBillboard = 'SomethingElse';

        assert.equal(billboardTypeOf(node), null);
    });
});

describe('billboardRotation', () => {
    const light = new THREE.Vector3(1, 0, 0);

    /**
     * `BBT_FACE` and `BBT_PARALLEL` both take `m_billboardView` - the camera's own orientation - so
     * the quad's face ends up pointing straight back down the view direction.
     */
    it('turns a facing billboard square-on to the camera', () => {
        for (const type of ['Face', 'Parallel'] as const) {
            const camera = CAMERA_AT(0, 0, 10);

            const towards = facing(billboardRotation(type, camera, light, new THREE.Vector3()));

            assert.ok(towards.dot(new THREE.Vector3(0, 0, 1)) > 0.999, `${type}: ${towards.z}`);
        }
    });

    it('follows the camera round', () => {
        const camera = CAMERA_AT(10, 4, 0);

        const towards = facing(billboardRotation('Face', camera, light, new THREE.Vector3()));

        // Straight at the camera, wherever it has got to.
        assert.ok(towards.dot(camera.position.clone().normalize()) > 0.999, `${towards.x}`);
    });

    /**
     * The `ZAxis*` family yaws about the model's own up axis and nothing else - that is what keeps
     * a tree standing upright while it turns to face you, rather than tipping over to point its
     * face at a camera looking down.
     */
    it('keeps a Z-axis billboard upright however high the camera is', () => {
        const camera = CAMERA_AT(0, 50, 10);

        const towards = facing(billboardRotation('ZAxisView', camera, light, new THREE.Vector3()));

        assert.ok(Math.abs(towards.dot(BILLBOARD_UP)) < 1e-6, `tipped: ${towards.y}`);
    });

    it('yaws a Z-axis billboard towards the camera', () => {
        const camera = CAMERA_AT(10, 0, 0);

        const towards = facing(billboardRotation('ZAxisView', camera, light, new THREE.Vector3()));

        assert.ok(towards.dot(new THREE.Vector3(1, 0, 0)) > 0.999, `${towards.x},${towards.z}`);
    });

    /** Measured from the billboard, not the origin: a card off to one side still turns to face you. */
    it('measures the direction from the billboard`s own position', () => {
        const camera = CAMERA_AT(0, 0, 10);
        const at = new THREE.Vector3(0, 0, 20);

        const towards = facing(billboardRotation('ZAxisView', camera, light, at));

        // The camera is BEHIND this one, so it turns the other way.
        assert.ok(towards.dot(new THREE.Vector3(0, 0, -1)) > 0.999, `${towards.z}`);
    });

    /**
     * `BBT_ZAXIS_LIGHT` turns towards the light instead. Every tree in the shipped models uses it -
     * for the SHADOW volume, which has to face the light to cast anything.
     */
    it('turns a light-facing billboard towards the light', () => {
        const camera = CAMERA_AT(0, 0, 10);

        const towards = facing(
            billboardRotation('ZAxisLight', camera, new THREE.Vector3(0, -1, -1), new THREE.Vector3()));

        // The light travels along -Z here, so the face turns to meet it at +Z, and stays upright.
        assert.ok(towards.dot(new THREE.Vector3(0, 0, 1)) > 0.999, `${towards.x},${towards.z}`);
        assert.ok(Math.abs(towards.y) < 1e-6, `tipped: ${towards.y}`);
    });

    /** No wind is modelled, so these hold still rather than drifting to some arbitrary heading. */
    it('leaves a wind billboard alone', () => {
        const camera = CAMERA_AT(10, 0, 0);

        const rotation = billboardRotation('ZAxisWind', camera, light, new THREE.Vector3());

        assert.ok(rotation.angleTo(new THREE.Quaternion()) < 1e-6);
    });
});

// The engine PRE-MULTIPLIES the billboard onto whatever world matrix the node already had -
// `world = m_billboardZLight * world` in `ObjectTemplate::DoBillboard`. The preview replaced that
// world rotation outright, which silently threw away the exporter's Z-up-to-Y-up root correction.
//
// It cost every tree its shadow. `W_tree_alien_00_hi`'s SHADOW card is 50 units of local Z, which
// the root correction stands up into world Y; forcing the world rotation to a bare yaw laid it flat
// instead, so the extruded volume ran from world Y 0 down to -145.5 - entirely at or below the
// floor. Nothing to darken above ground, and a shadow below it.
describe('billboardLocalRotation', () => {
    /** The Z-up to Y-up correction the exporter puts on the model root: -90 degrees about X. */
    const rootCorrection = (): THREE.Quaternion => new THREE.Quaternion()
        .setFromAxisAngle(new THREE.Vector3(1, 0, 0), -Math.PI / 2);

    const yaw = (radians: number): THREE.Quaternion => new THREE.Quaternion()
        .setFromAxisAngle(BILLBOARD_UP, radians);

    /** Where a local vector ends up once the bone and the computed local rotation are composed. */
    const world = (bone: THREE.Quaternion, local: THREE.Quaternion, v: THREE.Vector3): THREE.Vector3 =>
        v.clone().applyQuaternion(local).applyQuaternion(bone);

    it('keeps the standing card standing', () => {
        // The card's height is its local +Z. Through the root correction alone that is world +Y,
        // and the billboard must not take it out of the vertical.
        const bone = rootCorrection();
        const local = billboardLocalRotation(bone, yaw(Math.PI / 2), new THREE.Quaternion());

        const up = world(bone, local, new THREE.Vector3(0, 0, 1));

        assert.ok(Math.abs(up.y - 1) < 1e-6, `local +Z should stand up, got ${JSON.stringify(up)}`);
    });

    it('still turns the card about the vertical', () => {
        // A quarter turn has to actually move it, or the billboard is doing nothing.
        const bone = rootCorrection();
        const at0 = billboardLocalRotation(bone, yaw(0), new THREE.Quaternion());
        const at90 = billboardLocalRotation(bone, yaw(Math.PI / 2), new THREE.Quaternion());

        const a = world(bone, at0, new THREE.Vector3(1, 0, 0));
        const b = world(bone, at90, new THREE.Vector3(1, 0, 0));

        assert.ok(a.distanceTo(b) > 1, `${JSON.stringify(a)} vs ${JSON.stringify(b)}`);
    });

    it('is the billboard itself when the node has no rotation of its own', () => {
        const local = billboardLocalRotation(
            new THREE.Quaternion(), yaw(Math.PI / 3), new THREE.Quaternion());

        assert.ok(local.angleTo(yaw(Math.PI / 3)) < 1e-6);
    });
});
