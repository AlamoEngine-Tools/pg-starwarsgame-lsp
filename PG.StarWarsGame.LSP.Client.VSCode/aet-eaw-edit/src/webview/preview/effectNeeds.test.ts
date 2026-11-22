// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import {
    modelNeeds,
    sceneEffectNames,
    sceneWreckModels,
    systemEffectNames,
    systemTextureNames,
} from "./effectNeeds";
import { emitter, properties, testSystem } from "./particleFixture";

describe("effectNeeds", () => {
    describe("the effects a scene can start by name", () => {
        // Each of these is started by something the reader does after the unit is on screen: a
        // hardpoint destroyed, the hull damaged, the unit killed, a wreck burning out. Asked for
        // only then, each was a wait in the middle of the thing being watched.
        it("lists what a hardpoint, the hull, the unit's death, a wreck and a death clone set off", () => {
            const names = sceneEffectNames({
                hardpoints: [
                    { deathExplosionParticles: "Large_Explosion_Space" },
                    { deathExplosionParticles: null },
                ],
                damagedSmokeAsset: "Damage_Smoke",
                deathExplosions: "Huge_Explosion_Space",
                spinAway: { explosion: "Spin_Explosion" },
                deathClones: [
                    {
                        modelFile: "wreck.alo",
                        endExplosions: ["Clone_End_A", "Clone_End_B"],
                        particles: [{ systemRef: "p_wreck_fire" }],
                    },
                ],
                breakoffProps: [
                    {
                        resolved: true,
                        modelRef: "debris.alo",
                        attachedParticle: "Debris_Fire",
                        deathExplosions: "Small_Explosion_Space",
                        particles: [{ systemRef: "p_debris_trail" }],
                    },
                ],
            });

            assert.deepEqual(names, [
                "Large_Explosion_Space",
                "Damage_Smoke",
                "Huge_Explosion_Space",
                "Spin_Explosion",
                "Clone_End_A",
                "Clone_End_B",
                "p_wreck_fire",
                "Debris_Fire",
                "Small_Explosion_Space",
                "p_debris_trail",
            ]);
        });

        // A Star Destroyer's hardpoints all name the one explosion: one name, one request.
        it("lists a name once however many name it and however it is cased", () => {
            const names = sceneEffectNames({
                hardpoints: [
                    { deathExplosionParticles: "Large_Explosion_Space" },
                    { deathExplosionParticles: "large_explosion_space" },
                    { deathExplosionParticles: " Large_Explosion_Space " },
                ],
                deathExplosions: "LARGE_EXPLOSION_SPACE",
            });

            assert.deepEqual(names, ["Large_Explosion_Space"]);
        });

        // A prop that is named but not defined, or defined without a model, is never dropped, so
        // nothing of its is ever started. The same for a clone without a model, except how it
        // ends: that is the clone type's own and plays whether or not a wreck is drawn.
        it("leaves out what a wreck or a clone without a model would have carried", () => {
            const names = sceneEffectNames({
                deathClones: [
                    {
                        modelFile: null,
                        endExplosions: ["Clone_End"],
                        particles: [{ systemRef: "p_wreck_fire" }],
                    },
                ],
                breakoffProps: [
                    {
                        resolved: false,
                        modelRef: "debris.alo",
                        attachedParticle: "Debris_Fire",
                    },
                    {
                        resolved: true,
                        modelRef: "",
                        deathExplosions: "Small_Explosion_Space",
                    },
                ],
            });

            assert.deepEqual(names, ["Clone_End"]);
        });

        it("lists nothing for a scene that names none", () => {
            assert.deepEqual(sceneEffectNames({}), []);
            assert.deepEqual(
                sceneEffectNames({ hardpoints: [], damagedSmokeAsset: " " }),
                [],
            );
        });
    });

    describe("what a system needs before any of it is built", () => {
        it("names every texture its emitters sample, normal maps too, each once", () => {
            const system = testSystem([
                emitter({ colorTexture: "P_Fire.tga" }),
                emitter({
                    colorTexture: "p_fire.tga",
                    normalTexture: "P_Fire_Bump.tga",
                }),
                emitter({ colorTexture: "" }),
            ]);

            assert.deepEqual(systemTextureNames(system), [
                "p_fire.tga",
                "p_fire_bump.tga",
            ]);
        });

        it("names the engine effect of each render mode it draws with", () => {
            const system = testSystem([
                emitter({ properties: properties({ blendMode: "Additive" }) }),
                emitter({ properties: properties({ blendMode: "Additive" }) }),
                emitter({
                    properties: properties({ blendMode: "Transparent" }),
                }),
            ]);

            assert.deepEqual(systemEffectNames(system), [
                "PrimAdditive.fx",
                "PrimAlpha.fx",
            ]);
        });

        // A heat sprite is drawn by the distortion pass, not through an effect of its own - but it
        // bends the frame by the strength the heat composite declares, so that one is needed.
        it("names the heat composite for a heat emitter, and no effect of its own", () => {
            const system = testSystem([
                emitter({
                    properties: properties({
                        blendMode: "Additive",
                        isHeatParticle: true,
                    }),
                }),
            ]);

            assert.deepEqual(systemEffectNames(system), ["SceneHeat.fx"]);
        });

        it("names the heat composite beside the Heat render mode's effect", () => {
            const system = testSystem([
                emitter({ properties: properties({ blendMode: "Heat" }) }),
            ]);

            assert.deepEqual(systemEffectNames(system), [
                "PrimHeat.fx",
                "SceneHeat.fx",
            ]);
        });
    });

    describe("the wreck models a scene can drop", () => {
        // A hardpoint's wreck was asked for at the moment the hardpoint was destroyed: the piece
        // appeared a beat after the explosion it came out of.
        it("lists each prop that has a model, with the clips of that model", () => {
            const wrecks = sceneWreckModels({
                breakoffProps: [
                    {
                        id: "Breakoff_FL",
                        resolved: true,
                        modelRef: "debris_fl.alo",
                        animations: ["debris_fl_die_00.ala"],
                    },
                    {
                        id: "Breakoff_FR",
                        resolved: true,
                        modelRef: "debris_fr.alo",
                    },
                ],
            });

            assert.deepEqual(wrecks, [
                {
                    id: "Breakoff_FL",
                    modelReference: "debris_fl.alo",
                    animations: ["debris_fl_die_00.ala"],
                },
                {
                    id: "Breakoff_FR",
                    modelReference: "debris_fr.alo",
                    animations: [],
                },
            ]);
        });

        it("leaves out a prop that is not defined or has no model", () => {
            const wrecks = sceneWreckModels({
                breakoffProps: [
                    { id: "Typo", resolved: false, modelRef: "debris.alo" },
                    { id: "Bare", resolved: true, modelRef: null },
                ],
            });

            assert.deepEqual(wrecks, []);
            assert.deepEqual(sceneWreckModels({}), []);
        });
    });

    describe("what a model needs before it is put in a scene", () => {
        const mesh = (extras: Record<string, unknown>): THREE.Mesh => {
            const made = new THREE.Mesh();
            made.userData.alamo = extras;
            return made;
        };

        it("names the textures and the effects its meshes are drawn with, each once", () => {
            const root = new THREE.Group();
            const bone = new THREE.Object3D();
            root.add(bone);
            bone.add(
                mesh({
                    alamoShader: "MeshBumpColorize.fx",
                    "param:BaseTexture": "debris.tga",
                    "param:NormalTexture": "debris_b.tga",
                }),
            );
            root.add(
                mesh({
                    alamoShader: "MeshAlpha.fx",
                    "param:BaseTexture": "debris.tga",
                }),
            );
            root.add(
                mesh({
                    alamoShader: "MeshAlpha.fx",
                    "param:BaseTexture": "glow.tga",
                }),
            );

            assert.deepEqual(modelNeeds(root), {
                textures: ["debris.tga", "debris_b.tga", "glow.tga"],
                shaders: ["MeshAlpha.fx", "MeshBumpColorize.fx"],
            });
        });

        it("names nothing for nodes that are not the model's meshes", () => {
            const root = new THREE.Group();
            root.add(new THREE.Mesh());
            root.add(new THREE.Object3D());

            assert.deepEqual(modelNeeds(root), { textures: [], shaders: [] });
        });
    });
});
