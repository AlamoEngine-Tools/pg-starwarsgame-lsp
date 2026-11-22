// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { parsedModel, partOf } from "./parsedModel";

/** What the loader hands back for a model of one bone carrying one mesh. */
function loaded(): {
    scene: THREE.Group;
    bone: THREE.Object3D;
    mesh: THREE.Mesh;
    animations: THREE.AnimationClip[];
    parser: { json: unknown };
} {
    const scene = new THREE.Group();
    const bone = new THREE.Object3D();
    bone.name = "Root#0";
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute(
        "position",
        new THREE.BufferAttribute(new Float32Array(9), 3),
    );
    const mesh = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial());
    mesh.name = "Hull#1";
    mesh.userData = { name: "Hull.01#1" };
    bone.add(mesh);
    scene.add(bone);
    return { scene, bone, mesh, animations: [], parser: { json: {} } };
}

const meshOf = (root: THREE.Object3D): THREE.Mesh => {
    let found: THREE.Mesh | undefined;
    root.traverse((node) => {
        if (node instanceof THREE.Mesh) {
            found = node;
        }
    });
    assert.ok(found, "the copy has no mesh");
    return found;
};

describe("a parsed model", () => {
    it("gives each part nodes of its own over the same vertex data", () => {
        const gltf = loaded();
        const model = parsedModel(gltf);

        const first = partOf(model);
        const second = partOf(model);

        assert.notEqual(first, second);
        assert.notEqual(first, gltf.scene);
        assert.notEqual(meshOf(first), meshOf(second));
        assert.notEqual(meshOf(first), gltf.mesh);
        assert.equal(meshOf(first).geometry, gltf.mesh.geometry);
        assert.equal(meshOf(second).geometry, gltf.mesh.geometry);
        assert.equal(meshOf(first).name, "Hull#1");
    });

    // A part takes its copy apart and writes on it; the next part starts from the file's shape.
    it("stays as it was read when a part changes its copy", () => {
        const gltf = loaded();
        const model = parsedModel(gltf);

        const part = partOf(model);
        const mesh = meshOf(part);
        mesh.userData.alamo = { alamoShader: "MeshAlpha.fx" };
        mesh.material = new THREE.MeshBasicMaterial();
        mesh.removeFromParent();

        assert.equal(gltf.mesh.parent, gltf.bone);
        assert.deepEqual(gltf.mesh.userData, { name: "Hull.01#1" });
        assert.equal(meshOf(partOf(model)).parent?.name, "Root#0");
    });

    it("binds a copy's skinned mesh to the copy's own bones", () => {
        const scene = new THREE.Group();
        const joint = new THREE.Bone();
        joint.name = "Arm#1";
        const skinned = new THREE.SkinnedMesh(
            new THREE.BufferGeometry(),
            new THREE.MeshBasicMaterial(),
        );
        scene.add(joint, skinned);
        skinned.bind(new THREE.Skeleton([joint]));
        const model = parsedModel({
            scene,
            animations: [],
            parser: { json: {} },
        });

        const part = partOf(model);
        const copy = meshOf(part) as THREE.SkinnedMesh;

        assert.notEqual(copy.skeleton, skinned.skeleton);
        assert.notEqual(copy.skeleton.bones[0], joint);
        assert.equal(copy.skeleton.bones[0].parent, part);
    });

    // The loader names a track by the node's uuid where the node has no name, and a copy's nodes
    // have uuids of their own.
    it("plays its clips on a copy, on a node without a name too", () => {
        const scene = new THREE.Group();
        const named = new THREE.Object3D();
        named.name = "Turret#1";
        const nameless = new THREE.Object3D();
        scene.add(named, nameless);
        const clip = new THREE.AnimationClip("idle", 1, [
            new THREE.VectorKeyframeTrack(
                `${named.name}.position`,
                [0, 1],
                [0, 0, 0, 0, 0, 10],
            ),
            new THREE.VectorKeyframeTrack(
                `${nameless.uuid}.position`,
                [0, 1],
                [0, 0, 0, 20, 0, 0],
            ),
        ]);
        const model = parsedModel({
            scene,
            animations: [clip],
            parser: { json: {} },
        });

        const part = partOf(model);
        const mixer = new THREE.AnimationMixer(part);
        mixer.clipAction(model.clips[0]).play();
        mixer.setTime(0.5);

        assert.equal(part.children[0].position.z, 5);
        assert.equal(part.children[1].position.x, 10);
        assert.equal(named.position.z, 0, "the clip moved the model kept");
        assert.equal(nameless.position.x, 0, "the clip moved the model kept");
    });

    it("carries each clip's visibility tracks", () => {
        const gltf = loaded();
        gltf.parser.json = {
            animations: [
                {
                    name: "die",
                    extras: {
                        alamoVisibility: { fps: 30, bones: { "Hull#1": "10" } },
                    },
                },
            ],
        };

        const model = parsedModel(gltf);

        assert.deepEqual([...model.visibility.keys()], ["die"]);
    });
});
