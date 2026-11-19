// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type MaterialExtras } from "./materials";
import { MaterialBinder } from "./materialBinder";
import { standInContext } from "./viewportContext.testing";
import { TextureStore } from "./textureStore";

/**
 * A binder over a stand-in viewport: a model root whose meshes are all drawn, no stencil shadows, no
 * particle systems. Nothing here draws a frame, so the camera and the lighting frame are never read.
 */
function binderWith(): { materials: MaterialBinder; modelRoot: THREE.Group } {
    const modelRoot = new THREE.Group();
    const eachMesh = (visit: (mesh: THREE.Mesh) => void): void =>
        modelRoot.traverse((node) => {
            if (node instanceof THREE.Mesh) {
                visit(node);
            }
        });
    const context = standInContext({
        camera: new THREE.PerspectiveCamera(),
        modelRoot,
        textures: new TextureStore(),
        particles: { particleSystems: new Map() },
        environment: {
            stencilShadowing: false,
            frameFor: () => {
                throw new Error("no frame is drawn here");
            },
        },
        assembly: { forEachDrawnMesh: eachMesh, forEachPartMesh: eachMesh },
    });
    return { materials: new MaterialBinder(context), modelRoot };
}

/** A sub-mesh as the exporter hands it over: the Alamo shader and its parameters in the extras. */
function meshDrawing(extras: MaterialExtras): THREE.Mesh {
    const carrier = new THREE.MeshStandardMaterial();
    carrier.userData = { ...extras };
    return new THREE.Mesh(new THREE.BufferGeometry(), carrier);
}

const HULL: MaterialExtras = {
    alamoShader: "MeshGloss.fx",
    alamoMesh: "FC_Hull",
    "param:BaseTexture": "Hull.dds",
};

const baseMap = (mesh: THREE.Mesh): THREE.Texture | null =>
    (mesh.material as THREE.MeshStandardMaterial).map;

describe("MaterialBinder", () => {
    it("binds a texture to every mesh that names it, whatever the case", () => {
        const { materials, modelRoot } = binderWith();
        const mesh = meshDrawing(HULL);
        modelRoot.add(mesh);
        materials.applyMaterial(mesh);

        const texture = new THREE.Texture();
        materials.setTexture("HULL.DDS", texture);

        assert.equal(baseMap(mesh), texture);
    });

    it("hands a texture already in hand to a subtree added afterwards", () => {
        const { materials, modelRoot } = binderWith();
        const texture = new THREE.Texture();
        materials.setTexture("Hull.dds", texture);

        const wreck = new THREE.Group();
        const mesh = meshDrawing(HULL);
        wreck.add(mesh);
        modelRoot.add(wreck);
        materials.applyMaterial(mesh);
        materials.bindLoadedTextures(wreck);

        assert.equal(baseMap(mesh), texture);
    });

    it("reports the team-coloured meshes by name", () => {
        const { materials, modelRoot } = binderWith();
        const hull = meshDrawing(HULL);
        const engine = meshDrawing({
            alamoShader: "MeshGloss.fx",
            alamoMesh: "Engine",
        });
        modelRoot.add(hull, engine);
        materials.applyMaterial(hull);
        materials.applyMaterial(engine);

        assert.deepEqual(materials.colorizedMeshes(), ["FC_Hull"]);
    });

    it("keeps wireframe on across a material rebuilt later", () => {
        const { materials, modelRoot } = binderWith();
        const mesh = meshDrawing(HULL);
        modelRoot.add(mesh);
        materials.applyMaterial(mesh);

        materials.setWireframe(true);
        materials.applyMaterial(mesh);
        materials.applyWireframe();

        assert.equal(
            (mesh.material as THREE.MeshStandardMaterial).wireframe,
            true,
        );
    });

    it("names every shader the model draws with, once", () => {
        const { materials, modelRoot } = binderWith();
        for (const extras of [
            HULL,
            HULL,
            { alamoShader: "MeshAdditive.fx", alamoMesh: "Glow" },
        ]) {
            const mesh = meshDrawing(extras);
            modelRoot.add(mesh);
            materials.applyMaterial(mesh);
        }

        assert.deepEqual(materials.shaderNames(), [
            "MeshAdditive.fx",
            "MeshGloss.fx",
        ]);
    });
});
