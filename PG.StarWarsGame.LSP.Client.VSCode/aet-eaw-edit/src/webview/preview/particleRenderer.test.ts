// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import type {
    AlamoEmitter,
    AlamoEmitterProperties,
} from "../../protocol/modelPreview";
import { AlamoMaterial } from "./alamoMaterial";
import type { TranslatedEffect } from "./fx/effect";
import type { FxMaterialState } from "./fx/renderState";
import {
    emitter,
    properties,
    testSystem,
    track,
    volume,
} from "./particleFixture";
import {
    blendingFor,
    HEAT_LAYER,
    ParticleSystemInstance,
    tailGeometry,
} from "./particleRenderer";

describe("blendingFor", () => {
    /**
     * The shipped `Engine/PrimAdditive.fx` blends `SrcBlend = ONE, DestBlend = ONE`. Alpha does not
     * scale an additive particle AT ALL - which is why 588 of the corpus's 1019 emitters can author
     * an alpha track of zero and still be the brightest thing on screen. Boba Fett's jetpack fire
     * (`P_boba_jetpack`, alpha 0 throughout) and his flamethrower (`P_flamethrower00`, alpha 0 and
     * 0.118) drew nothing at all until this was right.
     */
    it("does not let alpha scale the additive modes", () => {
        for (const mode of ["Additive", "DepthAdditive"]) {
            const blend = blendingFor(mode);

            assert.equal(blend.blending, THREE.CustomBlending, mode);
            assert.equal(blend.blendSrc, THREE.OneFactor, mode);
            assert.equal(blend.blendDst, THREE.OneFactor, mode);
            assert.equal(blend.alphaGated, false, mode);
        }
    });

    /**
     * three's AdditiveBlending applies its factors to alpha as well. The canvas is composited over
     * the page, so accumulating alpha saturates the region to opaque and reveals the renderer's
     * black clear colour - a fireball ringed by one dark square per sprite.
     */
    it("leaves destination alpha alone wherever it adds light", () => {
        for (const mode of ["Additive", "DepthAdditive", "Heat"]) {
            const blend = blendingFor(mode);

            assert.equal(blend.blendSrcAlpha, THREE.ZeroFactor, mode);
            assert.equal(blend.blendDstAlpha, THREE.OneFactor, mode);
        }
    });

    /** `PrimAlpha.fx` and its siblings: `SRCALPHA / INVSRCALPHA`, the one family alpha gates. */
    it("gates the alpha-blended modes on alpha, once", () => {
        for (const mode of [
            "Transparent",
            "DepthTransparent",
            "DiffuseTransparent",
            "Scanlines",
            "Bump",
            "SomethingThisBuildDoesNotModel",
        ]) {
            const blend = blendingFor(mode);

            assert.equal(blend.blendSrc, THREE.SrcAlphaFactor, mode);
            assert.equal(blend.blendDst, THREE.OneMinusSrcAlphaFactor, mode);
            assert.equal(blend.alphaGated, true, mode);
        }
    });

    /**
     * `PrimModulate.fx` is `ZERO / SRCCOLOR` - a MULTIPLY, which darkens. It was mapped to three's
     * SubtractiveBlending, which is a different operator entirely.
     */
    it("multiplies rather than subtracts for the inverse modes", () => {
        for (const mode of ["Inverse", "DepthInverse"]) {
            const blend = blendingFor(mode);

            assert.equal(blend.blendSrc, THREE.ZeroFactor, mode);
            assert.equal(blend.blendDst, THREE.SrcColorFactor, mode);
            assert.equal(blend.alphaGated, false, mode);
        }
    });

    /** `PrimParticleBumpAlpha.fx` sets `AlphaTestEnable = TRUE; AlphaRef = 8`. */
    it("carries the bump mode`s alpha test", () => {
        assert.ok(Math.abs(blendingFor("Bump").alphaTest - 8 / 255) < 1e-6);
        assert.equal(blendingFor("Additive").alphaTest, 0);
    });

    /**
     * `PrimParticleBumpAlpha.fx` is the one blended mode that declares `ZWriteEnable = true`.
     *
     * It can afford to: it is alpha TESTED at `AlphaRef = 8`, so what it writes depth for is the
     * part of the sprite that is actually solid. 21 emitters use it.
     */
    it("writes depth for the bump mode, which is the one blended mode that does", () => {
        assert.equal(blendingFor("Bump").depthWrite, true);
        assert.equal(blendingFor("Transparent").depthWrite, false);
    });

    it("never writes depth, whatever the mode", () => {
        // Sprites are unsorted and translucent; writing depth makes them cut holes in each other.
        for (const mode of [
            "Additive",
            "Transparent",
            "Inverse",
            "StencilDarken",
            "Heat",
        ]) {
            assert.equal(blendingFor(mode).depthWrite, false, mode);
        }
    });
});

describe("following the emitter", () => {
    /**
     * A system anchored on a bone inside a model, which is the shape every attached effect has.
     *
     * The root is attached to the SPACE, not to the bone: the bone is motion data the emitters read,
     * never a parent whose visibility could take the effect with it. See `attachTo`.
     */
    function rig(
        over: Partial<AlamoEmitterProperties>,
        piece?: THREE.Object3D,
    ): {
        bone: THREE.Object3D;
        instance: ParticleSystemInstance;
    } {
        const space = new THREE.Object3D();
        const bone = new THREE.Object3D();

        if (piece === undefined) {
            space.add(bone);
        } else {
            space.add(piece);
            piece.add(bone);
        }

        const system = testSystem([
            emitter({
                name: "trail",
                properties: properties({
                    particlesPerSecond: 10,
                    lifetime: 100,
                    ...over,
                }),
            }),
        ]);

        const instance = new ParticleSystemInstance(
            "test",
            system,
            new Map(),
            1,
            null,
            space,
        );
        space.add(instance.root);
        instance.attachTo(bone);

        return { bone, instance };
    }

    /** What three asks before it draws anything: the object and every ancestor of it. */
    function drawn(object: THREE.Object3D): boolean {
        for (
            let at: THREE.Object3D | null = object;
            at !== null;
            at = at.parent
        ) {
            if (!at.visible) {
                return false;
            }
        }

        return true;
    }

    /**
     * The Nebulon-B's blast, and the reason the anchor is not the parent.
     *
     * `p_explosion_big00#12` is a proxy bone attached to `Busted_00#11` - the very chunk whose
     * breaking up it exists to cover - and the death clip hides that chunk on the frame the blast
     * fires. three prunes a hidden subtree, so the explosion vanished with the piece: one frame of
     * 91 particles, then nothing. In the engine those particles are world-space and outlive the
     * emitter's parent outright.
     */
    it("keeps drawing when the piece its bone belongs to is hidden", () => {
        const piece = new THREE.Object3D();
        const { instance } = rig({}, piece);

        instance.update(0.5);
        assert.ok(
            instance.emitterParticles(0).length > 0,
            "nothing was thrown",
        );

        piece.visible = false;

        assert.equal(drawn(instance.root), true);
    });

    /**
     * Spawns a few, moves the bone a known distance, then reports where the ALREADY-LIVING ones
     * ended up. Only those: the ones born on the second step are born at the bone's new place,
     * which says nothing about whether a particle follows.
     */
    function afterMoving(
        instance: ParticleSystemInstance,
        bone: THREE.Object3D,
    ): number[] {
        instance.update(0.5);
        const born = instance.emitterParticles(0).length;

        bone.position.set(0, 0, 10);
        instance.update(0.5);

        return instance
            .emitterParticles(0)
            .slice(0, born)
            .map((particle) => particle.position.z);
    }

    /**
     * 851 of the corpus's 1019 emitters leave `linkToSystem` off, and the engine means it: a
     * particle is born in world space and STAYS there (`WorldTranslaterPlugin::TranslateParticle`
     * does nothing at all). Hanging the sprites off the bone made every one of them ride along.
     */
    it("leaves a world-space effect behind when the emitter moves", () => {
        const { bone, instance } = rig({ linkToSystem: false });

        const born = afterMoving(instance, bone);

        assert.ok(
            born.length > 0,
            "nothing spawned, so the test proves nothing",
        );
        assert.ok(
            born.every((z) => Math.abs(z) < 0.001),
            `particles were carried along with the bone: ${born.slice(0, 4)}`,
        );
    });

    /**
     * `EmitterTranslaterPlugin` adds the emitter's translation delta to every live particle, which
     * is what keeps an engine wash sitting in its nozzle as the ship flies.
     */
    it("carries a linked effect along", () => {
        const { bone, instance } = rig({ linkToSystem: true });

        const born = afterMoving(instance, bone);

        assert.ok(
            born.every((z) => Math.abs(z - 10) < 0.001),
            `linked particles did not follow: ${born.slice(0, 4)}`,
        );
    });

    /** `parentLinkStrength` is a FRACTION - `Pi_damage_elec_sd00` uses 1, `P_bladeclash` 0.8. */
    it("follows part of the way for a fractional parent link", () => {
        const { bone, instance } = rig({
            linkToSystem: false,
            parentLinkStrength: 0.5,
        });

        const born = afterMoving(instance, bone);

        assert.ok(
            born.every((z) => Math.abs(z - 5) < 0.001),
            `half-linked particles landed at ${born.slice(0, 4)}`,
        );
    });

    /**
     * The other half of `parentLinkStrength`: a particle is born already carrying the emitter's
     * own motion, so debris thrown from a moving hull keeps up with it.
     */
    it("inherits the emitter`s velocity at birth", () => {
        const { bone, instance } = rig({
            linkToSystem: false,
            parentLinkStrength: 1,
        });

        instance.update(0.5);
        bone.position.set(0, 0, 10);
        instance.update(0.5);
        const before = instance.emitterParticles(0).length;

        // Born on this step, while the emitter is travelling at 20/s.
        const fresh = instance.emitterParticles(0).slice(before);

        assert.ok(
            fresh.every(
                (particle) => Math.abs(particle.velocity.z - 20) < 0.001,
            ),
            `a fresh particle did not inherit the emitter's speed: ${fresh[0]?.velocity.z}`,
        );
    });

    /**
     * The direction a particle was born in has to reach the simulation's space with everything
     * else, or the inward pull - which half the corpus's emitters use - drags along an axis of the
     * bone's own frame while the particle travels in the model's.
     */
    it("turns the birth direction into the simulation space as well", () => {
        const space = new THREE.Object3D();
        const bone = new THREE.Object3D();
        // A quarter turn about the up axis, so local +Z becomes world +X.
        bone.rotation.set(0, Math.PI / 2, 0);
        space.add(bone);

        const system = testSystem([
            emitter({
                name: "burst",
                position: volume({
                    shape: "Point",
                    exactValue: { x: 0, y: 0, z: 5 },
                }),
                properties: properties({
                    particlesPerSecond: 10,
                    lifetime: 100,
                }),
            }),
        ]);

        const instance = new ParticleSystemInstance(
            "test",
            system,
            new Map(),
            1,
            null,
            space,
        );
        space.add(instance.root);
        instance.attachTo(bone);
        instance.update(0.5);

        const born = instance.emitterParticles(0)[0];

        assert.ok(Math.abs(born.inward.x - 1) < 1e-6, `${born.inward.x}`);
        assert.ok(Math.abs(born.inward.z) < 1e-6, `${born.inward.z}`);
    });

    /** Spawn positions come out of the emitter's own frame, so a moved bone spawns where it is. */
    it("spawns at the emitter, wherever the emitter has got to", () => {
        const { bone, instance } = rig({ linkToSystem: false });

        bone.position.set(0, 0, 7);
        instance.update(0.5);

        assert.ok(
            instance
                .emitterParticles(0)
                .every((p) => Math.abs(p.position.z - 7) < 0.001),
            "particles were born at the model origin rather than at the bone",
        );
    });
});

describe("object-space acceleration", () => {
    /**
     * Spawns on a bone with the given rotation and runs a while, then reports which way the
     * particle ended up being pushed. The direction is the whole question here, and asserting on it
     * sidesteps the simulation's own step clamp.
     */
    function pushedTowards(objectSpace: boolean, boneYaw = 0): THREE.Vector3 {
        const space = new THREE.Object3D();
        const bone = new THREE.Object3D();
        bone.rotation.set(0, boneYaw, 0);
        space.add(bone);

        const system = testSystem([
            emitter({
                name: "wash",
                properties: properties({
                    particlesPerSecond: 20,
                    lifetime: 100,
                    acceleration: { x: 0, y: 0, z: 10 },
                    objectSpaceAcceleration: objectSpace,
                }),
            }),
        ]);

        const instance = new ParticleSystemInstance(
            "test",
            system,
            new Map(),
            1,
            null,
            space,
        );
        space.add(instance.root);
        instance.attachTo(bone);

        for (let step = 0; step < 6; step++) {
            instance.update(0.1);
        }

        const { x, y, z } = instance.emitterParticles(0)[0].velocity;

        return new THREE.Vector3(x, y, z).normalize();
    }

    /**
     * 31 emitters set `objectSpaceAcceleration`, 18 of them with a real acceleration, and they are
     * almost all `Pte_*engines` pushing their wash out of the nozzle at 15 to 60 a second. That
     * only works if the push follows the SHIP: `AccelerationModifierPlugin::ModifyParticle`
     * re-reads it through the emitter's transform every frame when the flag is set.
     */
    it("turns an object-space acceleration with the bone", () => {
        // A quarter turn about the up axis takes the emitter's own +Z round to +X.
        const turned = pushedTowards(true, Math.PI / 2);

        assert.ok(
            turned.dot(new THREE.Vector3(1, 0, 0)) > 0.999,
            `${turned.toArray()}`,
        );
    });

    /** Without the flag it is a WORLD vector and only needs Alamo's axes turned into glTF's. */
    it("leaves a world-space acceleration pointing where the file put it", () => {
        const straight = pushedTowards(false, Math.PI / 2);

        // Alamo +Z is up, which is +Y here, whatever the bone is doing.
        assert.ok(
            straight.dot(new THREE.Vector3(0, 1, 0)) > 0.999,
            `${straight.toArray()}`,
        );
    });
});

describe("tailGeometry", () => {
    const kite = (
        over: Partial<AlamoEmitterProperties> = {},
    ): AlamoEmitterProperties =>
        properties({ hasTail: true, tailSize: 50, ...over });

    /**
     * Measured in the game: the trailing corner is pulled out to `tailSize * speed / maxSpeed + 1`
     * times the half size, where speed is the length of the velocity across the view and maxSpeed
     * the emitter's ceiling, raised to the speed when the particle is faster.
     */
    it("pulls the trailing corner out by the tail size over the emitter's ceiling", () => {
        // Straight across the view, so none of the speed is foreshortened away.
        const kited = tailGeometry(kite(), { x: 10, y: 0, z: 0 }, 20)!;

        assert.equal(kited.stretch, (50 * 10) / 20 + 1);
    });

    /** Flying at the camera there is nothing to streak: the quad stays a quad. */
    it("leaves a particle coming straight at the camera unstretched", () => {
        assert.equal(
            tailGeometry(kite(), { x: 0, y: 0, z: 10 }, 20)!.stretch,
            1,
        );
    });

    /** A particle faster than the ceiling takes its own speed as the ceiling: the full tail. */
    it("draws the full tail for a particle past the emitter's ceiling", () => {
        assert.equal(
            tailGeometry(kite(), { x: 30, y: 0, z: 0 }, 20)!.stretch,
            51,
        );
    });

    /**
     * The corner that gets pulled sits at the quad's 135 degree diagonal, so the extra quarter
     * turn puts it opposite the direction of travel - the streak trails, rather than leading.
     */
    it("points the tail away from where the particle is going", () => {
        const kited = tailGeometry(kite(), { x: 10, y: 0, z: 0 }, 10)!;

        assert.ok(Math.abs(kited.turns - 0.125) < 1e-6, `${kited.turns}`);
    });

    it("says nothing to do for an emitter without a tail", () => {
        assert.equal(
            tailGeometry(properties(), { x: 10, y: 0, z: 0 }, 20),
            null,
        );
    });

    it("leaves a motionless particle alone rather than dividing by zero", () => {
        const kited = tailGeometry(kite(), { x: 0, y: 0, z: 0 }, 0)!;

        assert.equal(kited.stretch, 1);
        assert.equal(kited.turns, 0);
    });
});

describe("world-oriented emitters", () => {
    /**
     * `XYAlignedRendererPlugin`, not the billboard one: 148 of the corpus's emitters lay their
     * quads flat in the world's XY plane - the GROUND, since Alamo is Z-up - and they are named
     * for it: "dotted ring", "inverted ring", the shockwave under an explosion. Drawn as
     * camera-facing billboards they stand up on edge as a flat sheet in the air.
     */
    it("tells the shader to lay the quad flat", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "ring",
                    properties: properties({ worldOriented: true }),
                }),
                emitter({ name: "smoke" }),
            ]),
        );

        assert.equal(instance.emitterWorldOriented(0), true);
        assert.equal(instance.emitterWorldOriented(1), false);
    });
});

describe("chained emitters", () => {
    /**
     * A system where emitter 1 trails emitter 0, which is the shape 21 of the corpus's systems
     * have: an explosion's `sparks` dragging its `smoke trail`.
     */
    function trailing(
        over: Partial<AlamoEmitter> = {},
    ): ParticleSystemInstance {
        return new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "sparks",
                    spawnDuringLife: 1,
                    position: volume({
                        shape: "Point",
                        exactValue: { x: 0, y: 0, z: 20 },
                    }),
                    properties: properties({
                        particlesPerSecond: 20,
                        lifetime: 100,
                    }),
                    ...over,
                }),
                emitter({
                    name: "smoke trail",
                    properties: properties({
                        particlesPerSecond: 20,
                        lifetime: 100,
                    }),
                }),
            ]),
        );
    }

    /**
     * A child emitter is not an emitter in its own right - the engine only ever spawns it from the
     * parent. Running its own clock as well put an explosion's smoke trail at the blast's centre
     * as a puff, quite apart from the trails it should have been drawing.
     */
    it("does not let a trail emitter spawn on its own", () => {
        // The parent spawns nothing, so neither should its child.
        const instance = trailing({
            properties: properties({ particlesPerSecond: 0 }),
        });

        instance.update(0.1);

        assert.equal(instance.emitterParticles(1).length, 0);
    });

    it("spawns the trail at the particle it follows, not at the emitter", () => {
        const instance = trailing();

        instance.update(0.1);
        instance.update(0.1);

        const trail = instance.emitterParticles(1);

        assert.ok(trail.length > 0, "the trail never spawned");
        assert.ok(
            trail.every((p) => Math.abs(p.position.z - 20) < 1e-6),
            `trail spawned at the emitter instead: ${trail[0].position.z}`,
        );
    });

    /**
     * Measured in the game (FoC x64, 2024 release): after loading, an emitter's spawn links are
     * resolved by looking up the emitter whose PRIORITY TICKET equals the stored value, not the
     * emitter at that position in the file. Every shipped system numbers its tickets 0, 1, 2 in
     * file order, so the two readings agree there; a file the Particle Editor reordered is where
     * they part.
     */
    it("resolves a spawn link by the child's priority ticket, not its position", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "debris",
                    spawnOnDeath: 7,
                    position: volume({
                        shape: "Point",
                        exactValue: { x: 0, y: 0, z: 7 },
                    }),
                    properties: properties({
                        priorityTicket: 5,
                        particlesPerSecond: 20,
                        lifetime: 0.05,
                    }),
                }),
                emitter({
                    name: "puff",
                    properties: properties({
                        priorityTicket: 7,
                        particlesPerSecond: 20,
                        lifetime: 100,
                    }),
                }),
            ]),
        );

        instance.update(0.1);
        instance.update(0.1);

        const puffs = instance.emitterParticles(1);
        assert.ok(
            puffs.length > 0,
            "the child named by ticket 7 was not spawned",
        );
        assert.ok(
            puffs.every((p) => Math.abs(p.position.z - 7) < 1e-6),
            "the child emitted on its own instead of where its parent died: " +
                "the link was read as a position, not a ticket",
        );
    });

    /**
     * Measured in the game: after loading, a system's emitters are sorted by priority ticket,
     * ascending, and that order is the update and draw order from then on. A file whose tickets
     * are not in file order draws in ticket order, whatever the file says.
     */
    it("updates and draws the emitters in ascending ticket order", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "c",
                    properties: properties({ priorityTicket: 2 }),
                }),
                emitter({
                    name: "a",
                    properties: properties({ priorityTicket: 0 }),
                }),
                emitter({
                    name: "b",
                    properties: properties({ priorityTicket: 1 }),
                }),
            ]),
        );

        assert.deepEqual(instance.drawOrder(), [1, 2, 0]);
    });

    it("keeps the file order where no emitter carries a ticket", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({ name: "first" }),
                emitter({ name: "second" }),
            ]),
        );

        assert.deepEqual(instance.drawOrder(), [0, 1]);
    });

    /** `spawnOnDeath` is the other half: three emitters in the corpus, all explosion debris. */
    it("bursts a death child where its parent died", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "debris",
                    spawnOnDeath: 1,
                    position: volume({
                        shape: "Point",
                        exactValue: { x: 0, y: 0, z: 7 },
                    }),
                    // Dies within the first step.
                    properties: properties({
                        particlesPerSecond: 20,
                        lifetime: 0.05,
                    }),
                }),
                emitter({
                    name: "puff",
                    properties: properties({
                        particlesPerSecond: 20,
                        lifetime: 100,
                    }),
                }),
            ]),
        );

        instance.update(0.1);
        instance.update(0.1);

        const puffs = instance.emitterParticles(1);

        assert.ok(puffs.length > 0, "nothing was spawned when the parent died");
        assert.ok(
            puffs.every((p) => Math.abs(p.position.z - 7) < 1e-6),
            `the puff was not where its parent died: ${puffs[0].position.z}`,
        );
    });
});

describe("freezing", () => {
    /**
     * Measured in the game (FoC x64, 2024 release): on the first tick where the emitter's age plus
     * the step would pass its freeze time, the emitter bakes the particles it has - as they are
     * BEFORE that step - and never steps, spawns, ages or fades again. A tick that lands exactly
     * on the freeze time still steps. Every asteroid field and the galaxy's dust freeze this way.
     */
    function freezing(
        freezeTime: number,
        over: Partial<AlamoEmitter> = {},
    ): ParticleSystemInstance {
        return new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "field",
                    speed: volume({
                        shape: "Point",
                        exactValue: { x: 0, y: 0, z: 5 },
                    }),
                    properties: properties({
                        particlesPerSecond: 10,
                        lifetime: 100,
                        freezeTime,
                    }),
                    ...over,
                }),
            ]),
        );
    }

    function snapshot(instance: ParticleSystemInstance): string {
        return JSON.stringify(
            instance
                .emitterParticles(0)
                .map((p) => [p.position.x, p.position.y, p.position.z, p.age]),
        );
    }

    it("keeps stepping and spawning while the freeze time is not reached", () => {
        const instance = freezing(0.5);
        for (let i = 0; i < 5; i++) {
            instance.update(0.1);
        }
        // Five steps of 0.1 s at ten a second, the last one landing exactly on the freeze time.
        assert.equal(instance.emitterParticles(0).length, 5);
    });

    it("bakes the particles once the freeze time has passed and leaves them alone", () => {
        const instance = freezing(0.5);
        for (let i = 0; i < 5; i++) {
            instance.update(0.1);
        }
        const baked = snapshot(instance);
        for (let i = 0; i < 20; i++) {
            instance.update(0.1);
        }
        assert.equal(
            snapshot(instance),
            baked,
            "a frozen emitter moved or aged its particles",
        );
    });

    it("spawns nothing after the freeze", () => {
        const instance = freezing(0.5);
        for (let i = 0; i < 30; i++) {
            instance.update(0.1);
        }
        assert.equal(instance.emitterParticles(0).length, 5);
    });

    it("never freezes an emitter whose freeze time is zero", () => {
        const instance = freezing(0);
        for (let i = 0; i < 5; i++) {
            instance.update(0.1);
        }
        const before = snapshot(instance);
        for (let i = 0; i < 20; i++) {
            instance.update(0.1);
        }
        assert.notEqual(snapshot(instance), before);
        assert.ok(instance.emitterParticles(0).length > 5);
    });

    it("drives no trail off a frozen parent", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "sparks",
                    spawnDuringLife: 1,
                    properties: properties({
                        particlesPerSecond: 20,
                        lifetime: 100,
                        freezeTime: 0.3,
                    }),
                }),
                emitter({
                    name: "smoke trail",
                    properties: properties({
                        particlesPerSecond: 20,
                        lifetime: 100,
                    }),
                }),
            ]),
        );
        for (let i = 0; i < 3; i++) {
            instance.update(0.1);
        }
        const trailed = instance.emitterParticles(1).length;
        assert.ok(trailed > 0, "the trail never spawned before the freeze");
        for (let i = 0; i < 10; i++) {
            instance.update(0.1);
        }
        assert.equal(instance.emitterParticles(1).length, trailed);
    });

    it("takes nothing more into a frozen trail child", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "sparks",
                    spawnDuringLife: 1,
                    properties: properties({
                        particlesPerSecond: 20,
                        lifetime: 100,
                    }),
                }),
                emitter({
                    name: "smoke trail",
                    properties: properties({
                        particlesPerSecond: 20,
                        lifetime: 100,
                        freezeTime: 0.3,
                    }),
                }),
            ]),
        );
        for (let i = 0; i < 3; i++) {
            instance.update(0.1);
        }
        const trailed = instance.emitterParticles(1).length;
        const sparks = instance.emitterParticles(0).length;
        assert.ok(trailed > 0, "the trail never spawned before the freeze");
        for (let i = 0; i < 10; i++) {
            instance.update(0.1);
        }
        // The parent went on spawning, so the trail's standstill is the child's freeze and not
        // the parent's.
        assert.ok(
            instance.emitterParticles(0).length > sparks,
            "the parent stopped spawning, so the test proves nothing",
        );
        assert.equal(instance.emitterParticles(1).length, trailed);
    });

    /**
     * Measured in the game: the no-depth-test flag is read on the live path only; the frozen
     * path always tests against the depth buffer. No shipped emitter sets both, so this is
     * fidelity rather than a visible fix.
     */
    it("depth-tests a frozen batch whatever the file says", () => {
        const instance = freezing(0.3, {
            properties: properties({
                particlesPerSecond: 10,
                lifetime: 100,
                freezeTime: 0.3,
                noDepthTest: true,
            }),
        });
        const material = instance.emitterMesh(0)!.material as THREE.Material;
        instance.update(0.1);
        assert.equal(material.depthTest, false);
        for (let i = 0; i < 4; i++) {
            instance.update(0.1);
        }
        assert.equal(material.depthTest, true);
    });
});

describe("pre-simulation", () => {
    /**
     * Measured in the game (FoC x64, 2024 release): at an emitter's first tick, one with a skip
     * time is stepped forward in tenths of a second until its clock is past that time, and only
     * then does the tick's own step run. Every freezing field sets skip equal to freeze - the
     * asteroid field is a populated scene on its first frame and frozen on its second.
     */
    function skipping(
        skipTime: number,
        freezeTime = 0,
    ): ParticleSystemInstance {
        return new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "field",
                    speed: volume({
                        shape: "Point",
                        exactValue: { x: 0, y: 0, z: 5 },
                    }),
                    properties: properties({
                        particlesPerSecond: 10,
                        lifetime: 100,
                        skipTime,
                        freezeTime,
                    }),
                }),
            ]),
        );
    }

    it("runs the skip time through before the first step", () => {
        const instance = skipping(1);
        instance.update(0.1);
        const particles = instance.emitterParticles(0);
        // Eleven tenths to pass one second, then the tick itself: twelve spawns at ten a second,
        // the oldest of them a full second down the road.
        assert.equal(particles.length, 12);
        assert.ok(
            particles.some((p) => p.position.z > 5),
            "nothing had travelled the skipped second",
        );
    });

    /**
     * The engine's loop runs while the clock is AT OR BELOW the skip time, so a clock that lands
     * exactly on it takes one more step. Five tenths sum to exactly one half, which is what makes
     * this case decidable.
     */
    it("steps once more when the clock lands exactly on the skip time", () => {
        const instance = skipping(0.5);
        instance.update(0.1);
        // Six pre-simulated steps (0.0 to 0.5 inclusive) plus the tick: seven spawns.
        assert.equal(instance.emitterParticles(0).length, 7);
    });

    it("pre-simulates once, on the first tick only", () => {
        const instance = skipping(1);
        instance.update(0.1);
        instance.update(0.1);
        assert.equal(instance.emitterParticles(0).length, 13);
    });

    it("does nothing without a skip time", () => {
        const instance = skipping(0);
        instance.update(0.1);
        assert.equal(instance.emitterParticles(0).length, 1);
    });

    /** Skip equal to freeze, the shape every shipped field has: populated, then frozen at once. */
    it("freezes a field with skip equal to freeze on its second tick", () => {
        const instance = skipping(1, 1);
        instance.update(0.1);
        const populated = instance.emitterParticles(0).length;
        assert.equal(populated, 12);
        instance.update(0.1);
        const baked = JSON.stringify(
            instance.emitterParticles(0).map((p) => [p.position.z, p.age]),
        );
        for (let i = 0; i < 10; i++) {
            instance.update(0.1);
        }
        assert.equal(instance.emitterParticles(0).length, populated);
        assert.equal(
            JSON.stringify(
                instance.emitterParticles(0).map((p) => [p.position.z, p.age]),
            ),
            baked,
        );
    });
});

describe("depth-sorted frozen batch", () => {
    /**
     * Measured in the game (FoC x64, 2024 release): a FROZEN emitter with its depth-sort flag set
     * and the alpha, depth-sprite-alpha or opaque blend mode submits its baked particles sorted by
     * squared distance from the camera, descending - back to front - and re-sorts every frame
     * against the camera as it then stands. Any other mode, or the flag clear, submits them in
     * bake order. Live particles are never sorted.
     */
    function field(
        over: Partial<AlamoEmitterProperties> = {},
    ): ParticleSystemInstance {
        return new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "chunks",
                    speed: volume({
                        shape: "Point",
                        exactValue: { x: 0, y: 0, z: 5 },
                    }),
                    properties: properties({
                        blendMode: "Transparent",
                        particlesPerSecond: 10,
                        lifetime: 100,
                        freezeTime: 0.5,
                        ...over,
                    }),
                }),
            ]),
        );
    }

    /** A camera standing at `z`, looking at the origin. */
    function viewFrom(z: number): THREE.Matrix4 {
        const camera = new THREE.PerspectiveCamera();
        camera.position.set(0, 0, z);
        camera.lookAt(0, 0, 0);
        camera.updateMatrixWorld(true);
        return camera.matrixWorldInverse;
    }

    /** The z of each drawn instance, in the order the instances are drawn. */
    function drawnZ(instance: ParticleSystemInstance): number[] {
        const geometry = instance.emitterMesh(0)!
            .geometry as THREE.InstancedBufferGeometry;
        const offsets = geometry.getAttribute("iOffset").array;
        const out: number[] = [];
        for (let i = 0; i < geometry.instanceCount; i++) {
            out.push(offsets[i * 3 + 2]);
        }
        return out;
    }

    /** Five ticks at ten a second: particles at z 2, 1.5, 1, 0.5, 0 in spawn order. */
    function fill(
        instance: ParticleSystemInstance,
        view: THREE.Matrix4 | null,
    ) {
        for (let i = 0; i < 5; i++) {
            instance.update(0.1, view);
        }
    }

    const BAKE_ORDER = [2, 1.5, 1, 0.5, 0];

    it("draws the frozen batch back to front from the camera", () => {
        const instance = field();
        fill(instance, viewFrom(100));
        instance.update(0.1, viewFrom(100));
        assert.deepEqual(drawnZ(instance), [0, 0.5, 1, 1.5, 2]);
    });

    it("re-sorts every frame as the camera moves", () => {
        const instance = field();
        fill(instance, viewFrom(100));
        instance.update(0.1, viewFrom(100));
        instance.update(0.1, viewFrom(-100));
        assert.deepEqual(drawnZ(instance), BAKE_ORDER);
        instance.update(0.1, viewFrom(100));
        assert.deepEqual(drawnZ(instance), [0, 0.5, 1, 1.5, 2]);
    });

    for (const blendMode of ["None", "DepthTransparent"] as const) {
        it(`sorts the ${blendMode} mode too`, () => {
            const instance = field({ blendMode });
            fill(instance, viewFrom(100));
            instance.update(0.1, viewFrom(100));
            assert.deepEqual(drawnZ(instance), [0, 0.5, 1, 1.5, 2]);
        });
    }

    it("leaves an additive batch in bake order", () => {
        const instance = field({ blendMode: "Additive" });
        fill(instance, viewFrom(100));
        instance.update(0.1, viewFrom(100));
        assert.deepEqual(drawnZ(instance), BAKE_ORDER);
    });

    it("leaves the batch in bake order with the flag clear", () => {
        const instance = field({ depthSort: false });
        fill(instance, viewFrom(100));
        instance.update(0.1, viewFrom(100));
        assert.deepEqual(drawnZ(instance), BAKE_ORDER);
    });

    /** An older server sends no flag; the engine's default is on. */
    it("sorts when the server sent no flag", () => {
        const instance = field({ depthSort: undefined });
        fill(instance, viewFrom(100));
        instance.update(0.1, viewFrom(100));
        assert.deepEqual(drawnZ(instance), [0, 0.5, 1, 1.5, 2]);
    });

    it("never sorts live particles", () => {
        const instance = field({ freezeTime: 0 });
        fill(instance, viewFrom(100));
        assert.deepEqual(drawnZ(instance), BAKE_ORDER);
    });

    it("keeps bake order with no camera to sort against", () => {
        const instance = field();
        fill(instance, null);
        instance.update(0.1, null);
        assert.deepEqual(drawnZ(instance), BAKE_ORDER);
    });
});

describe("engine effects", () => {
    /** The smallest thing that is still a translated effect. */
    const EFFECT: TranslatedEffect = {
        vertex: "#version 300 es\nvoid main() { gl_Position = vec4(0.0); }",
        fragment:
            "#version 300 es\nprecision highp float;\nout vec4 c;\nvoid main() { c = vec4(1.0); }",
        semantics: new Map(),
        defaults: new Map(),
        samplers: new Map(),
        types: new Map(),
    };
    /** `PrimParticleBumpAlpha.fx`'s DX8 pass, as `materialStateFrom` reads it. */
    const STATE: FxMaterialState = {
        depthTest: true,
        depthWrite: true,
        depthFunc: "lessEqual",
        blend: "alpha",
        cull: "back",
        alphaTest: 8 / 255,
        colourScale: 1,
    };
    const BUMP = "primparticlebumpalpha.fx";

    function still(over: Partial<AlamoEmitterProperties> = {}): AlamoEmitter {
        return emitter({
            name: "rocks",
            normalTexture: "p_asteroid_n.tga",
            properties: properties({
                blendMode: "Bump",
                particlesPerSecond: 10,
                lifetime: 100,
                ...over,
            }),
        });
    }

    function withEffect(
        instance: ParticleSystemInstance,
        enabled = true,
    ): ParticleSystemInstance {
        instance.applyEffects(
            new Map([[BUMP, EFFECT]]),
            new Map([[BUMP, STATE]]),
            enabled,
        );
        return instance;
    }

    /** A camera at +z looking at the origin: view right is +x, view up is +y. */
    function viewFrom(z: number): THREE.Matrix4 {
        const camera = new THREE.PerspectiveCamera();
        camera.position.set(0, 0, z);
        camera.lookAt(0, 0, 0);
        camera.updateMatrixWorld(true);
        return camera.matrixWorldInverse;
    }

    function attribute(
        instance: ParticleSystemInstance,
        name: string,
    ): THREE.BufferAttribute {
        return instance
            .emitterMesh(0)!
            .geometry.getAttribute(name) as THREE.BufferAttribute;
    }

    it("names the engine effect each of its emitters is drawn with, once", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                still(),
                still(),
                emitter({ properties: properties({ blendMode: "Inverse" }) }),
            ]),
        );
        assert.deepEqual(instance.effectNames(), [
            "PrimModulate.fx",
            "PrimParticleBumpAlpha.fx",
        ]);
    });

    it("draws an emitter through its translated effect once the effect is in hand", () => {
        const instance = withEffect(
            new ParticleSystemInstance("test", testSystem([still()])),
        );
        const mesh = instance.emitterMesh(0)!;
        assert.ok(mesh.material instanceof AlamoMaterial);
        assert.equal(attribute(instance, "position").itemSize, 3);
        assert.equal(attribute(instance, "color").itemSize, 4);
        assert.equal(attribute(instance, "uv").itemSize, 2);
    });

    /**
     * Measured: the engine builds the four corners on the CPU - position plus the camera-rotated
     * corner offset times the size - and the effect's vertex shader only projects them.
     */
    it("writes four corners per particle, facing the camera", () => {
        const instance = withEffect(
            new ParticleSystemInstance("test", testSystem([still()])),
        );
        instance.update(0.1, viewFrom(100));
        assert.equal(instance.emitterParticles(0).length, 1);
        const positions = attribute(instance, "position");
        const corners = [0, 1, 2, 3]
            .map((i) =>
                [positions.getX(i), positions.getY(i), positions.getZ(i)]
                    .map((v) => Math.round(v * 1000) / 1000)
                    .join(","),
            )
            .sort();
        // Half a unit either way: the scale track's default of 1, halved as the engine halves it.
        assert.deepEqual(corners, [
            "-0.5,-0.5,0",
            "-0.5,0.5,0",
            "0.5,-0.5,0",
            "0.5,0.5,0",
        ]);
        assert.equal(instance.emitterMesh(0)!.geometry.drawRange.count, 6);
    });

    /**
     * Measured: for the bump effect the engine replaces the vertex colour's RGB with the sprite's
     * rotation tangent in view space, encoded as `0.5 + 0.5 * t`, and keeps the alpha. An
     * unrotated sprite's tangent is view right.
     */
    it("packs the rotation tangent into a bump sprite's colour", () => {
        const instance = withEffect(
            new ParticleSystemInstance("test", testSystem([still()])),
        );
        instance.update(0.1, viewFrom(100));
        const colour = attribute(instance, "color");
        assert.deepEqual(
            [colour.getX(0), colour.getY(0), colour.getZ(0), colour.getW(0)],
            [1, 0.5, 0.5, 1],
        );
    });

    /**
     * The corner offsets are the VIEW axes expressed in the particles' own space - the rows of
     * the simulation-to-view rotation, not its columns. A camera off to the side tells them apart.
     */
    it("faces the camera wherever it stands", () => {
        const instance = withEffect(
            new ParticleSystemInstance("test", testSystem([still()])),
        );
        // Off to the side AND above, so no axis lines up with another and a transposed basis
        // lands the corners somewhere else.
        const camera = new THREE.PerspectiveCamera();
        camera.position.set(100, 50, 30);
        camera.lookAt(0, 0, 0);
        camera.updateMatrixWorld(true);
        instance.update(0.1, camera.matrixWorldInverse);
        const positions = attribute(instance, "position");
        const right = new THREE.Vector3().setFromMatrixColumn(
            camera.matrixWorld,
            0,
        );
        const up = new THREE.Vector3().setFromMatrixColumn(
            camera.matrixWorld,
            1,
        );
        const key = (v: THREE.Vector3): string =>
            [v.x, v.y, v.z]
                .map((n) => Math.round(n * 1000) / 1000 + 0)
                .join(",");
        const expected = [
            [-1, -1],
            [1, -1],
            [1, 1],
            [-1, 1],
        ]
            .map(([cx, cy]) =>
                key(
                    right
                        .clone()
                        .multiplyScalar(cx * 0.5)
                        .addScaledVector(up, cy * 0.5),
                ),
            )
            .sort();
        const corners = [0, 1, 2, 3]
            .map((i) =>
                key(
                    new THREE.Vector3(
                        positions.getX(i),
                        positions.getY(i),
                        positions.getZ(i),
                    ),
                ),
            )
            .sort();
        assert.deepEqual(corners, expected);
    });

    it("packs the tangent whatever colour the track says", () => {
        const instance = withEffect(
            new ParticleSystemInstance(
                "test",
                testSystem([
                    {
                        ...still(),
                        tracks: [
                            track("Red", [
                                [0, 0.2],
                                [1, 0.2],
                            ]),
                            track("Green", [
                                [0, 0.3],
                                [1, 0.3],
                            ]),
                            track("Alpha", [
                                [0, 0.7],
                                [1, 0.7],
                            ]),
                        ],
                    },
                ]),
            ),
        );
        instance.update(0.1, viewFrom(100));
        const colour = attribute(instance, "color");
        assert.deepEqual(
            [
                colour.getX(0),
                colour.getY(0),
                colour.getZ(0),
                colour.getW(0),
            ].map((v) => Math.round(v * 1000) / 1000),
            [1, 0.5, 0.5, 0.7],
        );
    });

    /** A quarter turn: the tangent swings from view right to view up. */
    it("packs the tangent of a rotated sprite", () => {
        const instance = withEffect(
            new ParticleSystemInstance(
                "test",
                testSystem([
                    still({
                        randomRotation: true,
                        randomRotationAverage: 0.25,
                        randomRotationVariance: 0,
                    }),
                ]),
            ),
        );
        instance.update(0.1, viewFrom(100));
        const colour = attribute(instance, "color");
        assert.deepEqual(
            [colour.getX(0), colour.getY(0), colour.getZ(0)].map(
                (v) => Math.round(v * 1000) / 1000,
            ),
            [0.5, 1, 0.5],
        );
    });

    it("writes the tracked colour for any other effect", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    properties: properties({
                        blendMode: "Inverse",
                        particlesPerSecond: 10,
                        lifetime: 100,
                    }),
                }),
            ]),
        );
        instance.applyEffects(
            new Map([["primmodulate.fx", EFFECT]]),
            new Map([["primmodulate.fx", STATE]]),
            true,
        );
        instance.update(0.1, viewFrom(100));
        const colour = attribute(instance, "color");
        assert.deepEqual(
            [colour.getX(0), colour.getY(0), colour.getZ(0), colour.getW(0)],
            [1, 1, 1, 1],
        );
    });

    /**
     * Measured: a depth sprite's second texture is the emitter's own depth map, read from the same
     * chunk as a bump emitter's normal map, and the frozen renderer binds it to the effect's
     * second stage. Not the scene's depth.
     */
    it("draws a depth sprite through its effect, its depth map bound as DepthSampler", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "chunks",
                    normalTexture: "p_chunk_depth.tga",
                    properties: properties({
                        blendMode: "DepthTransparent",
                        particlesPerSecond: 10,
                        lifetime: 100,
                    }),
                }),
            ]),
        );
        instance.applyEffects(
            new Map([["primdepthspritealpha.fx", EFFECT]]),
            new Map([["primdepthspritealpha.fx", STATE]]),
            true,
        );
        const depthMap = new THREE.Texture();
        instance.setTexture("p_chunk_depth.tga", depthMap);
        const material = instance.emitterMesh(0)!.material;
        assert.ok(material instanceof AlamoMaterial);
        assert.equal(material.uniforms.DepthSampler.value, depthMap);
    });

    /**
     * The real order of events: the field freezes on its second frame, the effect comes back from
     * the host later. A bump field is not depth-sorted, so a frozen one is never written again -
     * and a fresh effect buffer created on arrival stayed empty. Measured on P_asteroid_field00
     * with the headless render probe: 832 particles, 37 in the frustum, nothing drawn.
     */
    it("writes the frozen bake into the effect's buffer when the effect arrives after the freeze", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([still({ freezeTime: 0.3 })]),
        );
        for (let i = 0; i < 5; i++) {
            instance.update(0.1, viewFrom(100));
        }
        assert.ok(instance.emitterParticles(0).length > 0);

        withEffect(instance);
        instance.update(0.1, viewFrom(100));
        const mesh = instance.emitterMesh(0)!;
        assert.ok(mesh.material instanceof AlamoMaterial);
        assert.equal(
            mesh.geometry.drawRange.count,
            instance.emitterParticles(0).length * 6,
        );
    });

    it("writes the frozen bake back into the preview's own buffer when the effect is switched off", () => {
        const instance = withEffect(
            new ParticleSystemInstance(
                "test",
                testSystem([still({ freezeTime: 0.3 })]),
            ),
        );
        for (let i = 0; i < 5; i++) {
            instance.update(0.1, viewFrom(100));
        }

        withEffect(instance, false);
        instance.update(0.1, viewFrom(100));
        const geometry = instance.emitterMesh(0)!
            .geometry as THREE.InstancedBufferGeometry;
        assert.equal(
            geometry.instanceCount,
            instance.emitterParticles(0).length,
        );
    });

    /**
     * Measured in the game: the corner table is rotated into camera space once per camera change,
     * and a frozen particle's corners are built from it at every draw. The bake is the particle
     * STATE, never the vertex stream, so a frozen sprite faces the camera wherever it moves. The
     * effect buffer wrote the view basis into its positions at the freeze and was not written
     * again, so the asteroid field's rocks faced wherever the camera stood when it froze.
     */
    it("turns a frozen bake drawn through its effect to face a camera that has moved", () => {
        const instance = withEffect(
            new ParticleSystemInstance(
                "test",
                testSystem([still({ freezeTime: 0.3 })]),
            ),
        );
        for (let i = 0; i < 5; i++) {
            instance.update(0.1, viewFrom(100));
        }
        assert.ok(instance.emitterParticles(0).length > 0);

        // The camera moves to +x, looking at the origin: a sprite facing it spans y and z and
        // keeps one x across its four corners.
        const side = new THREE.PerspectiveCamera();
        side.position.set(100, 0, 0);
        side.lookAt(0, 0, 0);
        side.updateMatrixWorld(true);
        instance.update(0.1, side.matrixWorldInverse);

        const positions = attribute(instance, "position").array;
        const xs = [0, 1, 2, 3].map((corner) => positions[corner * 3]);
        const zs = [0, 1, 2, 3].map((corner) => positions[corner * 3 + 2]);
        assert.ok(
            xs.every((x) => Math.abs(x - xs[0]) < 1e-6),
            `corners spread in x: ${xs.join(", ")}`,
        );
        assert.ok(
            zs.some((z) => Math.abs(z - zs[0]) > 1e-6),
            `corners flat in z: ${zs.join(", ")}`,
        );
    });

    it("depth-tests a frozen batch drawn through its effect, whatever the file says", () => {
        const instance = withEffect(
            new ParticleSystemInstance(
                "test",
                testSystem([still({ freezeTime: 0.3, noDepthTest: true })]),
            ),
        );
        const material = instance.emitterMesh(0)!.material as THREE.Material;
        instance.update(0.1, viewFrom(100));
        assert.equal(material.depthTest, false);
        for (let i = 0; i < 4; i++) {
            instance.update(0.1, viewFrom(100));
        }
        assert.equal(material.depthTest, true);
    });

    for (const [why, over, effectName] of [
        ["a heat sprite", { isHeatParticle: true }, BUMP],
        ["a tailed emitter", { hasTail: true }, BUMP],
    ] as const) {
        it(`keeps the preview's own shader for ${why}`, () => {
            const instance = new ParticleSystemInstance(
                "test",
                testSystem([still(over)]),
            );
            instance.applyEffects(
                new Map([[effectName, EFFECT]]),
                new Map([[effectName, STATE]]),
                true,
            );
            assert.ok(
                !(instance.emitterMesh(0)!.material instanceof AlamoMaterial),
            );
        });
    }

    it("goes back to the preview's own shader when translation is switched off", () => {
        const instance = withEffect(
            new ParticleSystemInstance("test", testSystem([still()])),
        );
        withEffect(instance, false);
        assert.ok(
            !(instance.emitterMesh(0)!.material instanceof AlamoMaterial),
        );
        assert.equal(
            instance.emitterMesh(0)!.geometry.getAttribute("iOffset").itemSize,
            3,
        );
    });

    it("asks for the normal map a bump emitter names, beside its colour texture", () => {
        const instance = new ParticleSystemInstance(
            "test",
            testSystem([still()]),
        );
        assert.deepEqual(instance.textureNames(), [
            "p_particle_master.tga",
            "p_asteroid_n.tga",
        ]);
    });
});

describe("heat particles", () => {
    const system = testSystem([
        emitter({ name: "fire" }),
        emitter({
            name: "heat",
            properties: properties({ isHeatParticle: true }),
        }),
    ]);

    /**
     * `PrimHeat.fx` adds no colour: it re-samples the SCENE texture at a screen position offset by
     * the sprite's normals. Drawn as an ordinary sprite it is a normal map added to the image, and
     * `p_particle_heat_master` is mostly flat violet-white - which is where the "flat sheet
     * rectangle" over the flamethrower came from. It goes on a layer of its own so the main pass
     * skips it and the distortion pass can draw it into the heat buffer instead.
     */
    it("draws a heat emitter on the heat layer, out of the ordinary pass", () => {
        const instance = new ParticleSystemInstance("test", system);

        assert.equal(
            instance.emitterMesh(0)!.layers.isEnabled(HEAT_LAYER),
            false,
        );
        assert.equal(
            instance.emitterMesh(1)!.layers.isEnabled(HEAT_LAYER),
            true,
        );
        assert.equal(
            instance.emitterMesh(1)!.layers.isEnabled(0),
            false,
            "a heat sprite must not also draw in the ordinary pass",
        );
    });

    it("still shows and hides one like any other emitter", () => {
        const instance = new ParticleSystemInstance("test", system);

        assert.equal(instance.emitterVisible(1), true);

        instance.setEmitterVisible(1, false);

        assert.equal(instance.emitterVisible(1), false);
    });

    it("says which emitters are heat, so the dock can label them", () => {
        const instance = new ParticleSystemInstance("test", system);

        assert.deepEqual(instance.emitterIsHeat(), [false, true]);
    });

    /** The pass is only worth its two render targets when something is actually distorting. */
    it("reports whether anything needs the distortion pass", () => {
        const instance = new ParticleSystemInstance("test", system);

        assert.equal(instance.hasVisibleHeat, true);

        instance.setEmitterVisible(1, false);

        assert.equal(instance.hasVisibleHeat, false);
    });
});

describe("emission gating", () => {
    function running(): ParticleSystemInstance {
        const system = testSystem([
            emitter({
                name: "fire",
                properties: properties({
                    particlesPerSecond: 20,
                    lifetime: 100,
                }),
            }),
        ]);

        return new ParticleSystemInstance(
            "test",
            system,
            new Map(),
            1,
            null,
            new THREE.Object3D(),
        );
    }

    /**
     * An Alamo animation switches a proxy bone off to say "this effect is not running yet", and
     * 2285 of the shipped visibility tracks are exactly that. Without a gate the emitter fires from
     * frame zero of every clip, so a rancor's death explosion plays through its idle.
     */
    it("spawns nothing while emission is switched off", () => {
        const instance = running();
        instance.hold("clip", true);

        instance.update(0.5);

        assert.equal(instance.emitterParticles(0).length, 0);
    });

    /**
     * Switching off must not delete what is already in the air. An effect that stops emitting has
     * its last sparks fly out and die; one that vanishes mid-flight is a rendering fault.
     */
    it("lets the particles already alive finish", () => {
        const instance = running();
        instance.update(0.5);
        const born = instance.emitterParticles(0).length;
        assert.ok(born > 0, "nothing spawned to begin with");

        instance.hold("clip", true);
        instance.update(0.1);

        assert.equal(instance.emitterParticles(0).length, born);
    });

    it("spawns again once emission comes back", () => {
        const instance = running();
        instance.hold("clip", true);
        instance.update(0.5);
        instance.hold("clip", false);
        instance.update(0.5);

        assert.ok(instance.emitterParticles(0).length > 0);
    });

    /**
     * The defect a wholesale `setEmitting(true)` caused, in its own right.
     *
     * More than one thing can want an effect held, and they do not know about each other. The clip
     * holds a proxy whose moment has not come; hiding the part a system is attached to holds it too. But
     * `applyEmitterGating` runs EVERY frame and, with no clip playing, resolved "the clip says
     * nothing" to `setEmitting(true)` - so it re-armed the emitters of a part that had been hidden,
     * one frame after they were held. `stopAnimation` did the same for the whole scene.
     *
     * A default is not a permission: a link with no opinion must decline, not vote yes.
     */
    it("stays held while a second reason still stands", () => {
        const instance = running();

        instance.hold("part", true);
        instance.hold("clip", true);

        // The clip stops having an opinion. The part is still hidden, so nothing may spawn.
        instance.hold("clip", false);
        instance.update(0.5);

        assert.equal(
            instance.emitterParticles(0).length,
            0,
            "the part hold was overridden",
        );

        // And when the last reason goes, it runs again.
        instance.hold("part", false);
        instance.update(0.5);

        assert.ok(instance.emitterParticles(0).length > 0, "never resumed");
    });

    /** Releasing a hold nobody placed is not a decision either - it must not start anything. */
    it("is unmoved by a hold that was never placed", () => {
        const instance = running();

        instance.hold("part", true);
        instance.hold("clip", false);
        instance.update(0.5);

        assert.equal(instance.emitterParticles(0).length, 0);
    });

    it("emits by default, since most systems are never gated", () => {
        const instance = running();

        instance.update(0.5);

        assert.ok(instance.emitterParticles(0).length > 0);
    });

    /**
     * A system nobody can see is not running.
     *
     * The animation gate above already says this for the case a CLIP holds an effect back. The
     * reader's tick and the quiet-on-open rule hold one exactly the same way - by not drawing it -
     * and that half was never wired, so a hidden system emitted into an invisible scene for as long
     * as the preview was open.
     */
    it("spawns nothing while the system is not drawn", () => {
        const instance = running();
        instance.setVisible(false);

        instance.update(0.5);

        assert.equal(instance.emitterParticles(0).length, 0);
    });

    /**
     * The defect this fixes, in its own right.
     *
     * `p_explosion_empire_atat00` is six emitters of bursts. Opened quiet - which the opening rules
     * require - it fired its whole life into a hidden scene during the first seconds, and by the
     * time the reader ticked its row on, four of the six were exhausted and there was nothing left
     * to draw. Measured on the live AT-AT: mean luminance 51.61 at rest, 52.32 with the row ticked
     * on, 59.84 with the same system restarted while visible.
     */
    it("fires a burst that was held hidden, when it is finally shown", () => {
        const system = testSystem([
            emitter({
                name: "boom",
                properties: properties({
                    useBursts: true,
                    particlesPerBurst: 12,
                    burstDelay: 0.1,
                    burstCount: 1,
                    lifetime: 100,
                }),
            }),
        ]);
        const instance = new ParticleSystemInstance(
            "test",
            system,
            new Map(),
            1,
            null,
            new THREE.Object3D(),
        );

        instance.setVisible(false);

        // Far longer than the burst schedule: the whole point is that the clock cannot run out
        // while nobody is looking.
        for (let i = 0; i < 100; i++) {
            instance.update(0.1);
        }

        assert.equal(
            instance.emitterParticles(0).length,
            0,
            "emitted while hidden",
        );
        assert.equal(instance.exhausted, false, "burnt out while hidden");

        instance.setVisible(true);
        instance.update(0.2);

        assert.equal(instance.emitterParticles(0).length, 12);
    });

    /**
     * The same rule one level down. Unticking a single emitter in the dock is the reader saying
     * they do not want to see it; an emitter that goes on filling its buffer behind that would
     * be back mid-life the moment they tick it again, and a burst would be gone for good.
     */
    it("spawns nothing while one emitter alone is hidden", () => {
        const instance = running();
        instance.setEmitterVisible(0, false);

        instance.update(0.5);
        assert.equal(instance.emitterParticles(0).length, 0);

        instance.setEmitterVisible(0, true);
        instance.update(0.5);
        assert.ok(instance.emitterParticles(0).length > 0, "never came back");
    });

    /**
     * Hiding must not delete what is in the air, for the same reason switching emission off does
     * not: an effect held mid-flight and shown again would otherwise pop back fully populated.
     */
    it("lets the particles already alive finish while hidden", () => {
        const instance = running();
        instance.update(0.5);
        const born = instance.emitterParticles(0).length;
        assert.ok(born > 0, "nothing spawned to begin with");

        instance.setVisible(false);
        instance.update(0.1);

        assert.equal(instance.emitterParticles(0).length, born);
    });
});

describe("the owning object Scale_Factor", () => {
    /**
     * `Scale_Factor` sits on the base GameObjectType beside `Mass` and `LOD_Bias`
     * (`DatabaseMapExport.xml`), and the reference applies it as a uniform scale on the object's
     * WORLD MATRIX - so it scales where a particle spawns as well as how big it draws. Six shipped
     * particle objects declare one, 20.0 on the four hero powerup effects and 2.0 on the two
     * bombing-run explosions, and nothing read it at all.
     *
     * NOT a scale on the system's node: the billboard path adds the sprite's half-extent in VIEW
     * space, after the model matrix (`mvPosition.xy += spun * iSize`), so a node scale would move
     * every particle without resizing any of it.
     */
    const drawn = (
        scaleFactor?: number,
    ): { offsets: number[]; sizes: number[] } => {
        const system = testSystem([
            emitter({
                properties: properties({
                    particlesPerSecond: 40,
                    lifetime: 100,
                }),
            }),
        ]);

        const instance = new ParticleSystemInstance(
            "p",
            system,
            new Map(),
            1,
            null,
            null,
            scaleFactor,
        );

        instance.update(0.5);

        const mesh = instance.root.children[0] as THREE.Mesh;
        const live = instance.emitterParticles(0).length;

        return {
            offsets: [
                ...(mesh.geometry.getAttribute("iOffset")
                    .array as Float32Array),
            ].slice(0, live * 3),
            sizes: [
                ...(mesh.geometry.getAttribute("iSize").array as Float32Array),
            ].slice(0, live),
        };
    };

    it("scales where a particle is and how big it draws, by the same amount", () => {
        // The same seed, so the two runs differ only by the factor.
        const plain = drawn(1);
        const scaled = drawn(20);

        assert.ok(plain.sizes.length > 0, "nothing was drawn to compare");
        assert.deepEqual(
            scaled.sizes.map((s) => +(s / 20).toFixed(4)),
            plain.sizes.map((s) => +s.toFixed(4)),
        );
        assert.deepEqual(
            scaled.offsets.map((o) => +(o / 20).toFixed(3)),
            plain.offsets.map((o) => +o.toFixed(3)),
        );
    });

    it("leaves a system that declares none exactly as it was", () => {
        // 101 of the 107 shipped particle objects declare 1.0 or nothing, so this is the path that
        // must not move.
        assert.deepEqual(drawn(undefined), drawn(1));
    });
});

/**
 * Measured in the game: every particle group carries an "energy", 1.0 unless a weather phase,
 * planet corruption, transport dust or a surface effect sets it. An ordinary emitter's spawn
 * attempt fires only with that probability and its particle is born at that share of its speed;
 * a weather field draws that share of its particles, the share clamped to 0..1.
 */
describe("particle energy", () => {
    function stream(): ParticleSystemInstance {
        return new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    speed: volume({
                        shape: "Point",
                        exactValue: { x: 0, y: 0, z: 10 },
                    }),
                    properties: properties({
                        particlesPerSecond: 100,
                        lifetime: 100,
                    }),
                }),
            ]),
        );
    }

    function field(): ParticleSystemInstance {
        return new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    properties: properties({
                        isWeatherParticle: true,
                        weatherCubeSize: 100,
                        particlesPerSecond: 10,
                        lifetime: 1,
                    }),
                }),
            ]),
        );
    }

    const instances = (instance: ParticleSystemInstance) =>
        (instance.emitterMesh(0)!.geometry as THREE.InstancedBufferGeometry)
            .instanceCount;

    it("spawns with the energy as its odds", () => {
        const full = stream();
        const half = stream();
        half.setEnergy(0.5);
        for (let i = 0; i < 10; i++) {
            full.update(0.1);
            half.update(0.1);
        }
        const born = half.emitterParticles(0).length;
        assert.equal(full.emitterParticles(0).length, 100);
        assert.ok(born > 30 && born < 70, `born ${born}`);
    });

    it("bears its particles at the energy's share of their speed", () => {
        const instance = stream();
        instance.setEnergy(0.5);
        while (instance.emitterParticles(0).length === 0) {
            instance.update(0.01);
        }
        const { x, y, z } = instance.emitterParticles(0)[0].velocity;
        assert.ok(Math.abs(Math.hypot(x, y, z) - 5) < 1e-6);
    });

    it("draws the energy's share of a weather field, never more than all of it", () => {
        const half = field();
        half.setEnergy(0.5);
        half.update(0.1);
        assert.equal(instances(half), 5);

        const over = field();
        over.setEnergy(3);
        over.update(0.1);
        assert.equal(instances(over), 10);
    });
});

/**
 * Measured in the game. A weather emitter is a field around the camera, not a stream from a point:
 * every particle it will ever have is born at once, uniformly inside a cube of the declared edge
 * around the emitter's origin, with a random age; none ever dies - an age past the lifetime wraps
 * round - and none is born later. Each step the position is wrapped back into the cube, axis by
 * axis. What is DRAWN is the copy of each particle inside the cube around the point the declared
 * camera offset in front of the camera, found by taking that point's phase within its period cell
 * off the particle before wrapping: the lattice stays fixed in the world, the camera moves through
 * it. The count is the file's per-second value taken outright, lifetime playing no part.
 */
describe("weather emitters", () => {
    const HALF = 50;

    function weather(
        over: Partial<AlamoEmitterProperties> = {},
    ): ParticleSystemInstance {
        return new ParticleSystemInstance(
            "test",
            testSystem([
                emitter({
                    name: "rain",
                    speed: volume({
                        shape: "Point",
                        exactValue: { x: 0, y: -5, z: 0 },
                    }),
                    properties: properties({
                        isWeatherParticle: true,
                        weatherCubeSize: HALF * 2,
                        weatherCubeDistance: 30,
                        weatherFadeoutDistance: 0,
                        weatherCutoffDistance: 0,
                        particlesPerSecond: 10,
                        lifetime: 1,
                        blendMode: "Additive",
                        ...over,
                    }),
                }),
            ]),
        );
    }

    /** A camera on +z looking at the origin, so the point in front of it is towards -z. */
    function viewFrom(z: number): THREE.Matrix4 {
        const camera = new THREE.PerspectiveCamera();
        camera.position.set(0, 0, z);
        camera.lookAt(0, 0, 0);
        camera.updateMatrixWorld(true);
        return camera.matrixWorldInverse;
    }

    function inside(value: number, centre: number): boolean {
        return value >= centre - HALF - 1e-6 && value <= centre + HALF + 1e-6;
    }

    it("is born complete, inside its cube, and never grows", () => {
        const instance = weather();
        instance.update(0.1, viewFrom(100));
        const born = instance.emitterParticles(0);
        assert.equal(born.length, 10);
        for (const particle of born) {
            assert.ok(
                inside(particle.position.x, 0),
                `x ${particle.position.x}`,
            );
            assert.ok(
                inside(particle.position.y, 0),
                `y ${particle.position.y}`,
            );
            assert.ok(
                inside(particle.position.z, 0),
                `z ${particle.position.z}`,
            );
        }

        for (let i = 0; i < 10; i++) {
            instance.update(0.1, viewFrom(100));
        }
        assert.equal(instance.emitterParticles(0).length, 10);
    });

    it("keeps every particle past its lifetime, the age wrapping round", () => {
        const instance = weather();
        for (let i = 0; i < 30; i++) {
            instance.update(0.1, viewFrom(100));
        }
        const live = instance.emitterParticles(0);
        assert.equal(live.length, 10);
        assert.ok(live.every((p) => p.age < p.lifetime));
    });

    it("wraps a particle that leaves a face back in through the opposite one", () => {
        const instance = weather();
        instance.update(0.1, viewFrom(100));
        // Falling at five a second for three seconds crosses the hundred-unit cube more than once.
        for (let i = 0; i < 30; i++) {
            instance.update(0.1, viewFrom(100));
        }
        for (const particle of instance.emitterParticles(0)) {
            assert.ok(
                inside(particle.position.y, 0),
                `y ${particle.position.y}`,
            );
        }
    });

    it("draws the field in the cube around the point in front of the camera", () => {
        const instance = weather();
        // Camera at z 100 looking at the origin; thirty units in front of it is z 70.
        instance.update(0.1, viewFrom(100));
        const offsets = instance
            .emitterMesh(0)!
            .geometry.getAttribute("iOffset").array;
        for (let i = 0; i < 10; i++) {
            assert.ok(inside(offsets[i * 3], 0), `x ${offsets[i * 3]}`);
            assert.ok(inside(offsets[i * 3 + 1], 0), `y ${offsets[i * 3 + 1]}`);
            assert.ok(
                inside(offsets[i * 3 + 2], 70),
                `z ${offsets[i * 3 + 2]}`,
            );
        }
    });

    /**
     * Measured: the fade distance is the length of twice the centre point minus the particle's
     * local position - as the game computes it, not as the numbers were presumably meant. With the
     * centre at z 70 that distance runs from 90 to 190 here, so a cutoff of 100 with a fade-out of
     * 200 hides the nearest and draws none fully; for an additive emitter it is the colour that
     * carries the fade.
     */
    it("fades an additive weather particle's colour by the game's distance", () => {
        const instance = weather({
            weatherCutoffDistance: 100,
            weatherFadeoutDistance: 200,
        });
        instance.update(0.1, viewFrom(100));
        const colours = instance
            .emitterMesh(0)!
            .geometry.getAttribute("iColor").array;
        const reds = Array.from({ length: 10 }, (_, i) => colours[i * 3]);
        assert.ok(
            reds.every((r) => r < 0.5),
            `reds ${reds.join(", ")}`,
        );
        assert.ok(
            reds.some((r) => r > 0),
            "every particle fully hidden",
        );
    });

    /**
     * Measured: a weather emitter that declares itself affected by wind has five times the
     * scene's wind added to its step every frame - a drift, not an acceleration, and nothing at
     * birth. The preview's scene wind is the viewer's wind setting.
     */
    it("drifts a wind-affected field at five times the wind, every step", () => {
        const instance = weather({ affectedByWind: true });
        instance.update(0.1, viewFrom(100));
        const particle = instance
            .emitterParticles(0)
            .find((p) => p.position.x > -HALF + 5 && p.position.x < HALF - 5)!;
        const before = particle.position.x;

        instance.update(0.1, viewFrom(100), { x: 2, y: 0, z: 0 });

        assert.ok(
            Math.abs(particle.position.x - (before + 2 * 5 * 0.1)) < 1e-6,
            `moved ${particle.position.x - before}`,
        );
    });

    it("leaves a field that ignores the wind where its own speed takes it", () => {
        const instance = weather({ affectedByWind: false });
        instance.update(0.1, viewFrom(100));
        const particle = instance
            .emitterParticles(0)
            .find((p) => p.position.x > -HALF + 5 && p.position.x < HALF - 5)!;
        const before = particle.position.x;

        instance.update(0.1, viewFrom(100), { x: 2, y: 0, z: 0 });

        assert.equal(particle.position.x, before);
    });

    /**
     * Measured: a streak is pulled along the particle's EFFECTIVE velocity - the stored velocity
     * plus the wind drift for a field - never the stored velocity alone. A uniform field shifted
     * by the wind still looks uniform; the tilt of the streaks is what the eye reads.
     */
    it("tilts a wind-affected field's streaks along the wind", () => {
        const calm = weather({ affectedByWind: true, hasTail: true });
        const windy = weather({ affectedByWind: true, hasTail: true });
        calm.update(0.1, viewFrom(100));
        windy.update(0.1, viewFrom(100));

        calm.update(0.1, viewFrom(100), { x: 0, y: 0, z: 0 });
        windy.update(0.1, viewFrom(100), { x: 20, y: 0, z: 0 });

        const turnsOf = (instance: ParticleSystemInstance) =>
            instance.emitterMesh(0)!.geometry.getAttribute("iRotation")
                .array[0];
        assert.notEqual(turnsOf(windy), turnsOf(calm));
    });
});
