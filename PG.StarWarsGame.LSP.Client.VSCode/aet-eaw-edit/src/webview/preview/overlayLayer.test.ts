// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type OverlayHost, OverlayLayer } from "./overlayLayer";
import { type LoadedPart } from "./loadedPart";
import { standInContext } from "./viewportContext.testing";

/** A part holding one 2-unit cube. */
function cubePart(id: string): LoadedPart {
    const root = new THREE.Group();
    root.add(new THREE.Mesh(new THREE.BoxGeometry(2, 2, 2)));
    return {
        id,
        root,
        bones: new Map(),
        bonesByIndex: new Map(),
        subjectId: "subject",
    };
}

/**
 * A layer over a stand-in viewport: a scene of its own and the parts given. Nothing here draws a
 * label or projects a mark, so the canvas and the label layer are never reached.
 */
function layerWith(parts: ReadonlyMap<string, LoadedPart> = new Map()): {
    layer: OverlayLayer;
    scene: THREE.Scene;
} {
    const scene = new THREE.Scene();
    const context = standInContext({
        scene,
        camera: new THREE.PerspectiveCamera(),
        canvas: null,
        labelLayer: null,
        parts,
        rows: { rowParents: new Map(), rowTargets: new Map() },
        assembly: { hull: () => null },
        queries: { skeleton: () => [], bonePosition: () => null },
        cameraRig: {
            modelSpan: () => 1,
            boundingSphere: () => ({ center: { x: 0, y: 0, z: 0 }, radius: 1 }),
        },
    });
    const host = {
        onReticleClicked: null,
    } as unknown as OverlayHost;
    return { layer: new OverlayLayer(context, host), scene };
}

describe("OverlayLayer", () => {
    it("puts four overlays in the scene, hidden, unculled and drawn over the model", () => {
        const { scene } = layerWith();

        assert.equal(scene.children.length, 4);
        for (const overlay of scene.children) {
            assert.equal(overlay.visible, false);
            assert.equal(overlay.frustumCulled, false);
            assert.ok(overlay.renderOrder >= 999);
        }
    });

    it("shows the joints and the lines between them only while the skeleton is on", () => {
        const { layer } = layerWith();

        layer.setSkeletonVisible(true);
        assert.equal(layer.skeletonShown, true);
        assert.equal(layer.joints.visible, true);
        assert.equal(layer.jointLines.visible, true);

        layer.setSkeletonVisible(false);
        assert.equal(layer.skeletonShown, false);
        assert.equal(layer.joints.visible, false);
        assert.equal(layer.jointLines.visible, false);
    });

    it("outlines a hardpoint's own part whole, and draws no box once nothing is selected", () => {
        const { layer } = layerWith(new Map([["hp", cubePart("hp")]]));

        layer.setSelectedHardpoint("hp", "row:hp");
        assert.equal(layer.selectionBoxes.visible, true);
        // One box is twelve edges, two points each.
        assert.equal(
            layer.selectionBoxes.geometry.getAttribute("position").count,
            24,
        );

        layer.setSelectedHardpoint(null, null);
        assert.equal(layer.selectionBoxes.visible, false);
    });

    it("boxes no part the scene does not hold", () => {
        const { layer } = layerWith();

        layer.setSelectedHardpoint("missing", null);
        assert.equal(layer.selectionBoxes.visible, false);
    });

    it("draws no bone axes for a bone the model does not have", () => {
        const { layer, scene } = layerWith();

        layer.setSelectedBones(new Set([3]));
        assert.ok(scene.children.every((overlay) => !overlay.visible));
    });

    it("picks the hull's joint under the pointer, and none when the pointer is away from every joint", () => {
        // One joint at the origin, a camera ten units back looking at it, a 100 x 100 canvas: the joint
        // sits in the middle of the canvas.
        const bone = new THREE.Bone();
        bone.updateMatrixWorld(true);
        const hull: LoadedPart = {
            id: "hull",
            root: bone,
            bones: new Map(),
            bonesByIndex: new Map([[4, bone]]),
            subjectId: "subject",
        };
        const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 100);
        camera.position.set(0, 0, 10);
        camera.updateMatrixWorld(true);
        const context = standInContext({
            scene: new THREE.Scene(),
            camera,
            canvas: {
                getBoundingClientRect: () => ({
                    left: 0,
                    top: 0,
                    width: 100,
                    height: 100,
                }),
            },
            labelLayer: null,
            parts: new Map(),
            rows: { rowParents: new Map(), rowTargets: new Map() },
            assembly: { hull: () => hull },
            queries: { skeleton: () => [], bonePosition: () => null },
            cameraRig: {
                modelSpan: () => 1,
                boundingSphere: () => ({
                    center: { x: 0, y: 0, z: 0 },
                    radius: 1,
                }),
            },
        });
        const layer = new OverlayLayer(context, {
            onReticleClicked: null,
        } as unknown as OverlayHost);
        const at = (clientX: number, clientY: number) =>
            ({ clientX, clientY }) as PointerEvent;

        assert.equal(layer.jointAt(at(52, 49)), 4);
        assert.equal(layer.jointAt(at(5, 5)), null);
    });

    it("hides the skeleton and the selection from a shot without annotations, and puts them back", () => {
        const { layer } = layerWith();
        layer.joints.visible = true;
        layer.jointLines.visible = true;
        layer.selectionBoxes.visible = true;

        const restore = layer.stageForCapture({
            width: 64,
            height: 64,
            background: null,
            grid: false,
            floor: true,
            particles: false,
            annotations: false,
        });

        assert.equal(layer.joints.visible, false);
        assert.equal(layer.jointLines.visible, false);
        assert.equal(layer.selectionBoxes.visible, false);

        restore();

        assert.equal(layer.joints.visible, true);
        assert.equal(layer.jointLines.visible, true);
        assert.equal(layer.selectionBoxes.visible, true);
    });

    it("shows no annotation in a shot that was not shown before it", () => {
        const { layer } = layerWith();

        layer.stageForCapture({
            ...{
                width: 64,
                height: 64,
                background: null,
                grid: false,
                floor: true,
                particles: false,
                annotations: false,
            },
            annotations: true,
        });

        assert.equal(layer.joints.visible, false);
        assert.equal(layer.selectionBoxes.visible, false);
    });
});
