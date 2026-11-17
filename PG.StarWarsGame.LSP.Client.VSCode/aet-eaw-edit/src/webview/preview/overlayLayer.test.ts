// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type OverlayHost, OverlayLayer } from "./overlayLayer";
import { type LoadedPart } from "./loadedPart";
import { type ViewportContext } from "./viewportContext";

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
    const context = {
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
    } as unknown as ViewportContext;
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
});
