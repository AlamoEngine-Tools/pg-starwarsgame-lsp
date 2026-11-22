// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type BoundingSphere } from "./framing";
import { CameraRig } from "./cameraRig";
import { ACTIVE_SUBJECT, type LoadedPart } from "./loadedPart";
import { standInContext } from "./viewportContext.testing";

/**
 * A rig over a stand-in viewport: a model root, the hull as one part rooted on it, every mesh drawn, no
 * particle systems unless `reaches` gives some, one system per reach. The grid rebuilds are recorded
 * rather than drawn.
 */
function rigWith(reaches: number[] | null = []): {
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
    // Null stands for a viewport still being put together: the particle rig is built last.
    const particles =
        reaches === null
            ? undefined
            : {
                  particleSystems: new Map(
                      reaches.map((reach, i) => [
                          `system${i}`,
                          { instance: { extent: () => reach } },
                      ]),
                  ),
                  effectReach: () => Math.max(0, ...reaches),
              };
    const hull: LoadedPart = {
        id: "hull",
        root: modelRoot,
        bones: new Map(),
        bonesByIndex: new Map(),
        subjectId: ACTIVE_SUBJECT,
    };
    const context = standInContext({
        camera,
        controls: { target, update: () => undefined },
        levels: { drawn: () => true },
        environment: {
            rebuildGrid: (sphere: BoundingSphere) => grids.push(sphere),
        },
        assembly: { hull: () => hull },
        materials: {},
        modelRoot,
        particles,
        renderer: null,
    });
    return {
        cameraRig: new CameraRig(context),
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

    // A particle file draws no meshes: its particle systems are all there is to frame (#431).
    // The lighting asks for the sphere while the viewport is being built, before any particle rig exists.
    it("measures an empty model as a unit sphere before the particle rig exists", () => {
        const { cameraRig } = rigWith(null);

        assert.deepEqual(cameraRig.boundingSphere(), {
            center: { x: 0, y: 0, z: 0 },
            radius: 1,
        });
    });

    it("measures a subject with no drawn geometry by the reach of its particle systems", () => {
        const { cameraRig } = rigWith([12, 40]);

        assert.deepEqual(cameraRig.boundingSphere(), {
            center: { x: 0, y: 0, z: 0 },
            radius: 40,
        });
    });

    it("frames a subject with no drawn geometry from outside its particle systems, grid and all", () => {
        const { cameraRig, camera, target, grids } = rigWith([40]);

        cameraRig.frameAll();

        assert.ok(
            camera.position.distanceTo(target) > 40,
            `distance ${camera.position.distanceTo(target)}`,
        );
        assert.equal(grids[0].radius, 40);
    });

    it("keeps the near plane where the opening put it when the draw distance is picked again", () => {
        const { cameraRig, camera } = rigWith([40]);
        cameraRig.frameAll();
        const near = camera.near;
        const far = camera.far;

        cameraRig.setDrawDistance(2);
        cameraRig.setDrawDistance(1);

        assert.equal(camera.near, near);
        assert.equal(camera.far, far);
    });

    // The hull is the subject: a plume or a firing effect never pushes it into the distance.
    it("measures a drawn model by its geometry, whatever its particle systems reach", () => {
        const { cameraRig, modelRoot } = rigWith([500]);
        modelRoot.add(box(0));
        modelRoot.updateMatrixWorld(true);

        assert.ok(
            Math.abs(cameraRig.boundingSphere().radius - Math.sqrt(3)) < 1e-6,
        );
        assert.equal(cameraRig.boundingSphere(true).radius, 500);
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

    it("stops turning the billboards of a subtree being disposed, and keeps the rest", () => {
        const { cameraRig } = rigWith();
        const kept = new THREE.Object3D();
        const gone = new THREE.Object3D();
        cameraRig.billboards.push(kept, gone);

        cameraRig.dropBillboards(new Set([gone]));
        assert.deepEqual(cameraRig.billboards, [kept]);

        cameraRig.forgetBillboards();
        assert.deepEqual(cameraRig.billboards, []);
    });

    it("collects the billboarded bones of a part that has just arrived", () => {
        const { cameraRig } = rigWith();
        const root = new THREE.Group();
        const card = new THREE.Object3D();
        card.userData.alamoBillboard = "Face";
        root.add(card, new THREE.Object3D());

        cameraRig.collectBillboards(root);

        assert.deepEqual(cameraRig.billboards, [card]);
    });
});

describe("CameraRig in a batch", () => {
    /** Counts how often the model is measured from here on. */
    function measurings(cameraRig: CameraRig): () => number {
        let count = 0;
        const measure = cameraRig.boundingSphere.bind(cameraRig);
        cameraRig.boundingSphere = (includeEffects?: boolean) => {
            count++;
            return measure(includeEffects);
        };
        return () => count;
    }

    it("puts the far plane past an effect that has arrived", () => {
        const reaches: number[] = [];
        const { cameraRig, camera, modelRoot } = rigWith(reaches);
        modelRoot.add(box(0));
        cameraRig.frameAll();
        const before = camera.far;

        reaches.push(5000);
        cameraRig.refitDepthRange();

        assert.ok(camera.far > before, `${camera.far} against ${before}`);
    });

    // Measured on a mod's capital ship: every explosion put in the scene measured the whole model
    // twice over, a second of the press that destroys every hardpoint.
    it("measures the model once for a batch, however often its steps asked", () => {
        const reaches: number[] = [];
        const { cameraRig, camera, modelRoot } = rigWith(reaches);
        modelRoot.add(box(0));
        cameraRig.frameAll();
        const before = camera.far;
        const measured = measurings(cameraRig);

        cameraRig.batch(() => {
            reaches.push(5000);
            cameraRig.refitDepthRange();
            cameraRig.refitDepthRange();
            cameraRig.refitDepthRange();

            assert.equal(measured(), 0, "measured inside the batch");
            assert.equal(camera.far, before);
        });

        assert.equal(measured(), 1);
        assert.ok(camera.far > before, `${camera.far} against ${before}`);
    });

    it("measures nothing for a batch that asked for nothing", () => {
        const { cameraRig, modelRoot } = rigWith();
        modelRoot.add(box(0));
        const measured = measurings(cameraRig);

        cameraRig.batch(() => undefined);

        assert.equal(measured(), 0);
    });

    it("waits for the outermost batch to end", () => {
        const { cameraRig, modelRoot } = rigWith();
        modelRoot.add(box(0));
        const measured = measurings(cameraRig);

        cameraRig.batch(() => {
            cameraRig.batch(() => cameraRig.refitDepthRange());

            assert.equal(measured(), 0, "measured when the inner batch ended");
        });

        assert.equal(measured(), 1);
    });
});
