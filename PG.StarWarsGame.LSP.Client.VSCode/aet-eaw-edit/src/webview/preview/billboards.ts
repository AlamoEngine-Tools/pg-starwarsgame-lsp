// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Geometry that turns to face something.
//
// A bone can declare that whatever hangs off it always points at the camera, the light or the wind -
// `AlamoBillboardType`, from the `0x206` bone chunk. It is rare: 22598 of the 22866 bones in the
// shipped models declare nothing, and the 268 that do are mostly sky domes and the planet cards on
// the galactic map. Trees are NOT among them - their foliage is real geometry, and the one
// billboarded bone a tree has is its SHADOW volume, which turns to face the light so it has
// something to cast.
//
// The engine's rule is `ObjectTemplate::DoBillboard`: a rotation composed onto the sub-mesh's world
// matrix ahead of the bone's own placement, so the bone supplies the position and the billboard
// supplies the orientation. That is what is reproduced here - as a WORLD rotation the caller puts on
// the geometry, never on the bone, so a billboarded bone's children are left where the file put them.

import * as THREE from 'three';

/**
 * The modes, exactly as `AlamoBillboardType` names them.
 *
 * `Disable` is not here: a bone that does not billboard carries no extras at all, so the absence is
 * the answer.
 */
export type BillboardType =
    'Parallel' | 'Face' | 'ZAxisView' | 'ZAxisLight' | 'ZAxisWind' | 'SunlightGlow' | 'Sun';

const TYPES = new Set<string>(
    ['Parallel', 'Face', 'ZAxisView', 'ZAxisLight', 'ZAxisWind', 'SunlightGlow', 'Sun']);

/**
 * The axis a billboarded quad's face points along before anything turns it.
 *
 * +Z, because the exporter's single Z-up-to-Y-up root correction is undone for us by working in
 * world space: a card modelled facing the viewer in Alamo's XZ plane comes out facing +Z here. The
 * engine's own `BillboardCorrection` exists for the same reason on its side of the convention.
 */
export const BILLBOARD_FACING = new THREE.Vector3(0, 0, 1);

/** The axis the `ZAxis*` family turns about - Alamo's Z, which the root correction makes Y here. */
export const BILLBOARD_UP = new THREE.Vector3(0, 1, 0);

/** Reused so a frame of billboarding allocates nothing. */
const TO_TARGET = new THREE.Vector3();
const FLATTENED = new THREE.Vector3();
const BASIS = new THREE.Matrix4();

/** The billboard mode a node declares, or null for the overwhelming majority that declare none. */
export function billboardTypeOf(node: THREE.Object3D): BillboardType | null {
    const declared = node.userData.alamoBillboard;

    return typeof declared === 'string' && TYPES.has(declared)
        ? declared as BillboardType
        : null;
}

/**
 * The WORLD rotation a billboard should be drawn with.
 *
 * `at` is where the billboard stands, because the direction to the camera is measured from the
 * billboard rather than from the model's origin - a row of cards along a hull each turn by a
 * different amount, and measuring from the origin turns them all the same way.
 *
 * `light` is the direction the light TRAVELS, matching the viewport's own key-light vector.
 */
export function billboardRotation(
    type: BillboardType,
    camera: THREE.Camera,
    light: THREE.Vector3,
    at: THREE.Vector3,
): THREE.Quaternion {
    switch (type) {
        case 'Face':
        case 'Parallel':
        case 'Sun':
        case 'SunlightGlow':
            // The camera's own orientation, which is what `m_billboardView` is - the inverse view
            // rotation. `Sun` and `SunlightGlow` also displace the card towards the sun in the
            // engine; the preview models no sun position, so they keep the facing and stay put.
            return camera.quaternion.clone();

        case 'ZAxisWind':
            // Towards the wind, and the preview has no wind. Holding still is better than turning
            // to some arbitrary heading that would read as a bug.
            return new THREE.Quaternion();

        case 'ZAxisView':
            return yawTowards(TO_TARGET.subVectors(camera.position, at));

        case 'ZAxisLight':
            // Towards where the light comes FROM, which is the direction it travels, reversed.
            return yawTowards(TO_TARGET.copy(light).negate());
    }
}

/**
 * A rotation about the up axis alone, turning the facing axis towards `direction`.
 *
 * Yaw only - that is the whole point of the `ZAxis` family. A full look-at would tip a tree's
 * shadow card over whenever the camera rose above it.
 */
function yawTowards(direction: THREE.Vector3): THREE.Quaternion {
    FLATTENED.copy(direction).projectOnPlane(BILLBOARD_UP);

    if (FLATTENED.lengthSq() < 1e-12) {
        // Looking straight down the up axis: every yaw is equally right, so keep the current one.
        return new THREE.Quaternion();
    }

    FLATTENED.normalize();

    // Built from the basis rather than `setFromUnitVectors`, which picks the SHORTEST arc between
    // the two vectors and so tips the card out of upright the moment the target is off the plane.
    const right = new THREE.Vector3().crossVectors(BILLBOARD_UP, FLATTENED).normalize();
    BASIS.makeBasis(right, BILLBOARD_UP, FLATTENED);

    return new THREE.Quaternion().setFromRotationMatrix(BASIS);
}
