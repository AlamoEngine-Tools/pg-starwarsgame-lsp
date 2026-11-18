// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type LoadedPart } from "./loadedPart";
import { axisRange, TURRET_AT_REST, type TurretHandle } from "./turretHandles";
import { type TurretHost, TurretRig } from "./turretRig";
import { standInContext } from "./viewportContext.testing";

/** A part whose one turret bone hangs under a mount, the way a hull carries one. */
function turretPart(): {
    part: LoadedPart;
    mount: THREE.Object3D;
    turret: THREE.Object3D;
} {
    const root = new THREE.Group();
    const mount = new THREE.Object3D();
    const turret = new THREE.Object3D();
    root.add(mount);
    mount.add(turret);
    return {
        part: {
            id: "p",
            root,
            bones: new Map([["turret", turret]]),
            bonesByIndex: new Map(),
            subjectId: "subject",
        },
        mount,
        turret,
    };
}

/** A rig over a stand-in viewport holding the parts given. Nothing here projects or draws an arc. */
function rigWith(parts: ReadonlyMap<string, LoadedPart>): TurretRig {
    const context = standInContext({
        camera: new THREE.PerspectiveCamera(),
        canvas: null,
        modelRoot: new THREE.Group(),
        parts,
        cameraRig: { modelSpan: () => 10 },
        assembly: { fireBoneAttachment: () => null },
    });
    const host = {
        modelScale: 1,
        onTurretAimed: null,
    } as unknown as TurretHost;
    return new TurretRig(context, host);
}

const handle: TurretHandle = {
    id: "weapon:turret",
    partId: "p",
    turretBone: "Turret",
    barrelBone: null,
    yaw: axisRange("yaw", 90),
    pitch: axisRange("pitch", 30),
};

const overlaysUnder = (node: THREE.Object3D): number =>
    node.children.filter((child) => child.userData.aetOverlay === true).length;

describe("TurretRig", () => {
    it("aims a turret at rest until it is turned by hand", () => {
        const rig = rigWith(new Map());

        assert.deepEqual(rig.turretAim("weapon:turret"), TURRET_AT_REST);
    });

    it("turns a turret bone to the rest angle the XML declares", () => {
        const { part, turret } = turretPart();
        const rig = rigWith(new Map([["p", part]]));

        rig.setTurretRestAngles([
            {
                partId: "p",
                turretBone: "Turret",
                barrelBone: null,
                rest: { yaw: 90, pitch: 0 },
            },
        ]);

        // A quarter turn, whichever way round the traverse axis is signed.
        const angle = 2 * Math.acos(Math.min(1, Math.abs(turret.quaternion.w)));
        assert.ok(Math.abs(angle - Math.PI / 2) < 1e-6, `turned by ${angle}`);
    });

    it("hangs a yaw and a pitch track beside a handled turret and takes them off with the handle", () => {
        const { part, mount } = turretPart();
        const rig = rigWith(new Map([["p", part]]));

        rig.setTurretHandles([handle]);
        assert.equal(overlaysUnder(mount), 2);

        rig.setTurretHandles([]);
        assert.equal(overlaysUnder(mount), 0);
    });

    it("builds no handle for a turret bone the part does not have", () => {
        const { part, mount } = turretPart();
        const rig = rigWith(new Map([["p", part]]));

        rig.setTurretHandles([{ ...handle, turretBone: "Missing" }]);
        assert.equal(overlaysUnder(mount), 0);
    });

    it("says whether the arcs are shown", () => {
        const rig = rigWith(new Map());

        rig.setFireArcsVisible(true);
        assert.equal(rig.arcsShown, true);
        rig.setFireArcsVisible(false);
        assert.equal(rig.arcsShown, false);
    });
});
