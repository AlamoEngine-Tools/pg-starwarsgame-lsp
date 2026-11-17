// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type BoundingSphere } from "./framing";
import { type CameraViewport, CameraRig } from "./cameraRig";
import { ACTIVE_SUBJECT, type LoadedPart } from "./loadedPart";
import { type ViewportContext } from "./viewportContext";

/**
 * A rig over a stand-in viewport: a model root, the hull as one part rooted on it, every mesh drawn, no
 * particle systems. The grid rebuilds are recorded rather than drawn.
 */
function rigWith(): {
    cameraRig: CameraRig;
    camera: THREE.PerspectiveCamera;
    target: THREE.Vector3;
    modelRoot: THREE.Group;
    grids: BoundingSphere[];
} {
    const modelRoot = new THREE.Group();
    const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 1000);
    const target = new THREE.Vector3();
    const grids: BoundingSphere[] = [];
    const hull: LoadedPart = {
        id: "hull",
        root: modelRoot,
        bones: new Map(),
        bonesByIndex: new Map(),
        subjectId: ACTIVE_SUBJECT,
    };
    const context = {
        camera,
        controls: { target, update: () => undefined },
        levels: { drawn: () => true },
        environment: {
            rebuildGrid: (sphere: BoundingSphere) => grids.push(sphere),
        },
        assembly: { hull: () => hull },
        materials: {},
        modelRoot,
        particles: { particleSystems: new Map(), effectReach: () => 0 },
        renderer: null,
    } as unknown as ViewportContext;
    const host = {
        updateBillboards: () => undefined,
    } as unknown as CameraViewport;
    return {
        cameraRig: new CameraRig(context, host),
        camera,
        target,
        modelRoot,
        grids,
    };
}

/** A unit cube's worth of mesh centred on a point. */
function box(x: number, size = 2): THREE.Mesh {
    const mesh = new THREE.Mesh(
        new THREE.BoxGeometry(size, size, size),
        new THREE.MeshBasicMaterial(),
    );
    mesh.position.set(x, 0, 0);
    mesh.updateMatrixWorld(true);
    return mesh;
}

describe("CameraRig", () => {
    it("measures the drawn model and leaves an overlay out of it", () => {
        const { cameraRig, modelRoot } = rigWith();
        modelRoot.add(box(0));
        const arc = box(0, 500);
        arc.userData.aetOverlay = true;
        modelRoot.add(arc);
        modelRoot.updateMatrixWorld(true);

        const sphere = cameraRig.boundingSphere();

        assert.ok(
            Math.abs(sphere.radius - Math.sqrt(3)) < 1e-6,
            `radius ${sphere.radius}`,
        );
    });

    it("falls back to a unit sphere for an empty model", () => {
        const { cameraRig } = rigWith();

        assert.deepEqual(cameraRig.boundingSphere(), {
            center: { x: 0, y: 0, z: 0 },
            radius: 1,
        });
    });

    it("frames the model at the fitted distance, aims at its centre and grows the grid with it", () => {
        const { cameraRig, camera, target, modelRoot, grids } = rigWith();
        modelRoot.add(box(10));
        modelRoot.updateMatrixWorld(true);

        cameraRig.frameAll("front");

        assert.ok(Math.abs(target.x - 10) < 1e-6 && Math.abs(target.y) < 1e-6);
        assert.ok(
            Math.abs(
                camera.position.distanceTo(target) - cameraRig.fitDistance,
            ) < 1e-6,
        );
        assert.equal(grids.length, 1);
        assert.ok(Math.abs(grids[0].center.x - 10) < 1e-6);
    });

    it("pushes the far plane out with the draw distance", () => {
        const { cameraRig, camera, modelRoot } = rigWith();
        modelRoot.add(box(0));
        modelRoot.updateMatrixWorld(true);
        cameraRig.frameAll();
        const fitted = camera.far;

        cameraRig.setDrawDistance(2);

        assert.ok(camera.far > fitted, `far ${camera.far} against ${fitted}`);
    });

    it("remembers the model's span until told the model changed", () => {
        const { cameraRig, modelRoot } = rigWith();
        modelRoot.add(box(0));
        modelRoot.updateMatrixWorld(true);
        const first = cameraRig.modelSpan();

        modelRoot.add(box(50));
        modelRoot.updateMatrixWorld(true);
        assert.equal(cameraRig.modelSpan(), first);

        cameraRig.forgetSpan();
        assert.ok(cameraRig.modelSpan() > first);
    });
});
