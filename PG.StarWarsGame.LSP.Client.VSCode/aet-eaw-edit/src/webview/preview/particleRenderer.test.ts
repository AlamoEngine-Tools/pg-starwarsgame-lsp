// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import type { AlamoEmitter, AlamoEmitterProperties } from '../../protocol/modelPreview';
import { emitter, properties, testSystem, volume } from './particleFixture';
import {
    blendingFor, tailGeometry, HEAT_LAYER, ParticleSystemInstance,
} from './particleRenderer';

describe('blendingFor', () => {
    /**
     * The shipped `Engine/PrimAdditive.fx` blends `SrcBlend = ONE, DestBlend = ONE`. Alpha does not
     * scale an additive particle AT ALL - which is why 588 of the corpus's 1019 emitters can author
     * an alpha track of zero and still be the brightest thing on screen. Boba Fett's jetpack fire
     * (`P_boba_jetpack`, alpha 0 throughout) and his flamethrower (`P_flamethrower00`, alpha 0 and
     * 0.118) drew nothing at all until this was right.
     */
    it('does not let alpha scale the additive modes', () => {
        for (const mode of ['Additive', 'DepthAdditive']) {
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
    it('leaves destination alpha alone wherever it adds light', () => {
        for (const mode of ['Additive', 'DepthAdditive', 'Heat']) {
            const blend = blendingFor(mode);

            assert.equal(blend.blendSrcAlpha, THREE.ZeroFactor, mode);
            assert.equal(blend.blendDstAlpha, THREE.OneFactor, mode);
        }
    });

    /** `PrimAlpha.fx` and its siblings: `SRCALPHA / INVSRCALPHA`, the one family alpha gates. */
    it('gates the alpha-blended modes on alpha, once', () => {
        for (const mode of ['Transparent', 'DepthTransparent', 'DiffuseTransparent', 'Scanlines',
            'Bump', 'SomethingThisBuildDoesNotModel']) {
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
    it('multiplies rather than subtracts for the inverse modes', () => {
        for (const mode of ['Inverse', 'DepthInverse']) {
            const blend = blendingFor(mode);

            assert.equal(blend.blendSrc, THREE.ZeroFactor, mode);
            assert.equal(blend.blendDst, THREE.SrcColorFactor, mode);
            assert.equal(blend.alphaGated, false, mode);
        }
    });

    /** `PrimParticleBumpAlpha.fx` sets `AlphaTestEnable = TRUE; AlphaRef = 8`. */
    it('carries the bump mode`s alpha test', () => {
        assert.ok(Math.abs(blendingFor('Bump').alphaTest - 8 / 255) < 1e-6);
        assert.equal(blendingFor('Additive').alphaTest, 0);
    });

    /**
     * `PrimParticleBumpAlpha.fx` is the one blended mode that declares `ZWriteEnable = true`.
     *
     * It can afford to: it is alpha TESTED at `AlphaRef = 8`, so what it writes depth for is the
     * part of the sprite that is actually solid. 21 emitters use it.
     */
    it('writes depth for the bump mode, which is the one blended mode that does', () => {
        assert.equal(blendingFor('Bump').depthWrite, true);
        assert.equal(blendingFor('Transparent').depthWrite, false);
    });

    it('never writes depth, whatever the mode', () => {
        // Sprites are unsorted and translucent; writing depth makes them cut holes in each other.
        for (const mode of ['Additive', 'Transparent', 'Inverse', 'StencilDarken', 'Heat']) {
            assert.equal(blendingFor(mode).depthWrite, false, mode);
        }
    });
});

describe('following the emitter', () => {
    /**
     * A system anchored on a bone inside a model, which is the shape every attached effect has.
     *
     * The root hangs off the SPACE, not off the bone: the bone is motion data the emitters read,
     * never a parent whose visibility could take the effect with it. See `attachTo`.
     */
    function rig(over: Partial<AlamoEmitterProperties>, piece?: THREE.Object3D): {
        bone: THREE.Object3D; instance: ParticleSystemInstance;
    } {
        const space = new THREE.Object3D();
        const bone = new THREE.Object3D();

        if (piece === undefined) {
            space.add(bone);
        } else {
            space.add(piece);
            piece.add(bone);
        }

        const system = testSystem([emitter({
            name: 'trail',
            properties: properties({ particlesPerSecond: 10, lifetime: 100, ...over }),
        })]);

        const instance = new ParticleSystemInstance('test', system, new Map(), 1, null, space);
        space.add(instance.root);
        instance.attachTo(bone);

        return { bone, instance };
    }

    /** What three asks before it draws anything: the object and every ancestor of it. */
    function drawn(object: THREE.Object3D): boolean {
        for (let at: THREE.Object3D | null = object; at !== null; at = at.parent) {
            if (!at.visible) {
                return false;
            }
        }

        return true;
    }

    /**
     * The Nebulon-B's blast, and the reason the anchor is not the parent.
     *
     * `p_explosion_big00#12` is a proxy bone hanging under `Busted_00#11` - the very chunk whose
     * breaking up it exists to cover - and the death clip hides that chunk on the frame the blast
     * fires. three prunes a hidden subtree, so the explosion vanished with the piece: one frame of
     * 91 particles, then nothing. In the engine those particles are world-space and outlive the
     * emitter's parent outright.
     */
    it('keeps drawing when the piece its bone belongs to is hidden', () => {
        const piece = new THREE.Object3D();
        const { instance } = rig({}, piece);

        instance.update(0.5);
        assert.ok(instance.emitterParticles(0).length > 0, 'nothing was thrown');

        piece.visible = false;

        assert.equal(drawn(instance.root), true);
    });

    /**
     * Spawns a few, moves the bone a known distance, then reports where the ALREADY-LIVING ones
     * ended up. Only those: the ones born on the second step are born at the bone's new place,
     * which says nothing about whether a particle follows.
     */
    function afterMoving(instance: ParticleSystemInstance, bone: THREE.Object3D): number[] {
        instance.update(0.5);
        const born = instance.emitterParticles(0).length;

        bone.position.set(0, 0, 10);
        instance.update(0.5);

        return instance.emitterParticles(0).slice(0, born).map(particle => particle.position.z);
    }

    /**
     * 851 of the corpus's 1019 emitters leave `linkToSystem` off, and the engine means it: a
     * particle is born in world space and STAYS there (`WorldTranslaterPlugin::TranslateParticle`
     * does nothing at all). Hanging the sprites off the bone made every one of them ride along.
     */
    it('leaves a world-space effect behind when the emitter moves', () => {
        const { bone, instance } = rig({ linkToSystem: false });

        const born = afterMoving(instance, bone);

        assert.ok(born.length > 0, 'nothing spawned, so the test proves nothing');
        assert.ok(born.every(z => Math.abs(z) < 0.001),
            `particles were carried along with the bone: ${born.slice(0, 4)}`);
    });

    /**
     * `EmitterTranslaterPlugin` adds the emitter's translation delta to every live particle, which
     * is what keeps an engine wash sitting in its nozzle as the ship flies.
     */
    it('carries a linked effect along', () => {
        const { bone, instance } = rig({ linkToSystem: true });

        const born = afterMoving(instance, bone);

        assert.ok(born.every(z => Math.abs(z - 10) < 0.001),
            `linked particles did not follow: ${born.slice(0, 4)}`);
    });

    /** `parentLinkStrength` is a FRACTION - `Pi_damage_elec_sd00` uses 1, `P_bladeclash` 0.8. */
    it('follows part of the way for a fractional parent link', () => {
        const { bone, instance } = rig({ linkToSystem: false, parentLinkStrength: 0.5 });

        const born = afterMoving(instance, bone);

        assert.ok(born.every(z => Math.abs(z - 5) < 0.001),
            `half-linked particles landed at ${born.slice(0, 4)}`);
    });

    /**
     * The other half of `parentLinkStrength`: a particle is born already carrying the emitter's
     * own motion, so debris thrown from a moving hull keeps up with it.
     */
    it('inherits the emitter`s velocity at birth', () => {
        const { bone, instance } = rig({ linkToSystem: false, parentLinkStrength: 1 });

        instance.update(0.5);
        bone.position.set(0, 0, 10);
        instance.update(0.5);
        const before = instance.emitterParticles(0).length;

        // Born on this step, while the emitter is travelling at 20/s.
        const fresh = instance.emitterParticles(0).slice(before);

        assert.ok(fresh.every(particle => Math.abs(particle.velocity.z - 20) < 0.001),
            `a fresh particle did not inherit the emitter's speed: ${fresh[0]?.velocity.z}`);
    });

    /**
     * The direction a particle was born in has to reach the simulation's space with everything
     * else, or the inward pull - which half the corpus's emitters use - drags along an axis of the
     * bone's own frame while the particle travels in the model's.
     */
    it('turns the birth direction into the simulation space as well', () => {
        const space = new THREE.Object3D();
        const bone = new THREE.Object3D();
        // A quarter turn about the up axis, so local +Z becomes world +X.
        bone.rotation.set(0, Math.PI / 2, 0);
        space.add(bone);

        const system = testSystem([emitter({
            name: 'burst',
            position: volume({ shape: 'Point', exactValue: { x: 0, y: 0, z: 5 } }),
            properties: properties({ particlesPerSecond: 10, lifetime: 100 }),
        })]);

        const instance = new ParticleSystemInstance('test', system, new Map(), 1, null, space);
        space.add(instance.root);
        instance.attachTo(bone);
        instance.update(0.5);

        const born = instance.emitterParticles(0)[0];

        assert.ok(Math.abs(born.inward.x - 1) < 1e-6, `${born.inward.x}`);
        assert.ok(Math.abs(born.inward.z) < 1e-6, `${born.inward.z}`);
    });

    /** Spawn positions come out of the emitter's own frame, so a moved bone spawns where it is. */
    it('spawns at the emitter, wherever the emitter has got to', () => {
        const { bone, instance } = rig({ linkToSystem: false });

        bone.position.set(0, 0, 7);
        instance.update(0.5);

        assert.ok(instance.emitterParticles(0).every(p => Math.abs(p.position.z - 7) < 0.001),
            'particles were born at the model origin rather than at the bone');
    });
});

describe('object-space acceleration', () => {
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

        const system = testSystem([emitter({
            name: 'wash',
            properties: properties({
                particlesPerSecond: 20, lifetime: 100,
                acceleration: { x: 0, y: 0, z: 10 },
                objectSpaceAcceleration: objectSpace,
            }),
        })]);

        const instance = new ParticleSystemInstance('test', system, new Map(), 1, null, space);
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
    it('turns an object-space acceleration with the bone', () => {
        // A quarter turn about the up axis takes the emitter's own +Z round to +X.
        const turned = pushedTowards(true, Math.PI / 2);

        assert.ok(turned.dot(new THREE.Vector3(1, 0, 0)) > 0.999, `${turned.toArray()}`);
    });

    /** Without the flag it is a WORLD vector and only needs Alamo's axes turned into glTF's. */
    it('leaves a world-space acceleration pointing where the file put it', () => {
        const straight = pushedTowards(false, Math.PI / 2);

        // Alamo +Z is up, which is +Y here, whatever the bone is doing.
        assert.ok(straight.dot(new THREE.Vector3(0, 1, 0)) > 0.999, `${straight.toArray()}`);
    });
});

describe('tailGeometry', () => {
    const kite = (over: Partial<AlamoEmitterProperties> = {}): AlamoEmitterProperties =>
        properties({ hasTail: true, tailSize: 50, ...over });

    /**
     * A kite is an ordinary quad with ONE corner pulled out behind the particle, which is how the
     * engine draws a streak (`EmitterInstance.cpp:625-640`). The pull is `tailSize` scaled by how
     * much of the velocity lies across the view - `sqrt(len*len/2)` in the reference, which is
     * len over root two.
     */
    it('pulls the trailing corner out by the tail size', () => {
        // Straight across the view, so none of the speed is foreshortened away.
        const kited = tailGeometry(kite(), { x: 10, y: 0, z: 0 }, 10)!;

        assert.ok(Math.abs(kited.stretch - 50 / Math.SQRT2) < 1e-6, `${kited.stretch}`);
    });

    /** Flying at the camera there is nothing to streak: the quad stays a quad. */
    it('leaves a particle coming straight at the camera unstretched', () => {
        assert.equal(tailGeometry(kite(), { x: 0, y: 0, z: 10 }, 10)!.stretch, 1);
    });

    /** `mult = length / 1000` when the particle inherited its emitter's motion. */
    it('scales the tail by speed for an emitter its particles are linked to', () => {
        const linked = kite({ parentLinkStrength: 1 });

        const slow = tailGeometry(linked, { x: 10, y: 0, z: 0 }, 10)!.stretch;
        const fast = tailGeometry(linked, { x: 400, y: 0, z: 0 }, 400)!.stretch;

        assert.equal(slow, 1, 'a slow linked particle should have no tail worth drawing');
        assert.ok(Math.abs(fast - 50 * 0.4 / Math.SQRT2) < 1e-6, `${fast}`);
    });

    /**
     * The corner that gets pulled sits at the quad's 135 degree diagonal, so the extra quarter
     * turn puts it opposite the direction of travel - the streak trails, rather than leading.
     */
    it('points the tail away from where the particle is going', () => {
        const kited = tailGeometry(kite(), { x: 10, y: 0, z: 0 }, 10)!;

        assert.ok(Math.abs(kited.turns - 0.125) < 1e-6, `${kited.turns}`);
    });

    it('says nothing to do for an emitter without a tail', () => {
        assert.equal(tailGeometry(properties(), { x: 10, y: 0, z: 0 }, 10), null);
    });

    it('leaves a motionless particle alone rather than dividing by zero', () => {
        const kited = tailGeometry(kite(), { x: 0, y: 0, z: 0 }, 0)!;

        assert.equal(kited.stretch, 1);
        assert.equal(kited.turns, 0);
    });
});

describe('world-oriented emitters', () => {
    /**
     * `XYAlignedRendererPlugin`, not the billboard one: 148 of the corpus's emitters lay their
     * quads flat in the world's XY plane - the GROUND, since Alamo is Z-up - and they are named
     * for it: "dotted ring", "inverted ring", the shockwave under an explosion. Drawn as
     * camera-facing billboards they stand up on edge as a flat sheet in the air.
     */
    it('tells the shader to lay the quad flat', () => {
        const instance = new ParticleSystemInstance('test', testSystem([
            emitter({ name: 'ring', properties: properties({ worldOriented: true }) }),
            emitter({ name: 'smoke' }),
        ]));

        assert.equal(instance.emitterWorldOriented(0), true);
        assert.equal(instance.emitterWorldOriented(1), false);
    });
});

describe('chained emitters', () => {
    /**
     * A system where emitter 1 trails emitter 0, which is the shape 21 of the corpus's systems
     * have: an explosion's `sparks` dragging its `smoke trail`.
     */
    function trailing(over: Partial<AlamoEmitter> = {}): ParticleSystemInstance {
        return new ParticleSystemInstance('test', testSystem([
            emitter({
                name: 'sparks',
                spawnDuringLife: 1,
                position: volume({ shape: 'Point', exactValue: { x: 0, y: 0, z: 20 } }),
                properties: properties({ particlesPerSecond: 20, lifetime: 100 }),
                ...over,
            }),
            emitter({
                name: 'smoke trail',
                properties: properties({ particlesPerSecond: 20, lifetime: 100 }),
            }),
        ]));
    }

    /**
     * A child emitter is not an emitter in its own right - the engine only ever spawns it from the
     * parent. Running its own clock as well put an explosion's smoke trail at the blast's centre
     * as a puff, quite apart from the trails it should have been drawing.
     */
    it('does not let a trail emitter spawn on its own', () => {
        // The parent spawns nothing, so neither should its child.
        const instance = trailing({ properties: properties({ particlesPerSecond: 0 }) });

        instance.update(0.1);

        assert.equal(instance.emitterParticles(1).length, 0);
    });

    it('spawns the trail at the particle it follows, not at the emitter', () => {
        const instance = trailing();

        instance.update(0.1);
        instance.update(0.1);

        const trail = instance.emitterParticles(1);

        assert.ok(trail.length > 0, 'the trail never spawned');
        assert.ok(trail.every(p => Math.abs(p.position.z - 20) < 1e-6),
            `trail spawned at the emitter instead: ${trail[0].position.z}`);
    });

    /** `spawnOnDeath` is the other half: three emitters in the corpus, all explosion debris. */
    it('bursts a death child where its parent died', () => {
        const instance = new ParticleSystemInstance('test', testSystem([
            emitter({
                name: 'debris',
                spawnOnDeath: 1,
                position: volume({ shape: 'Point', exactValue: { x: 0, y: 0, z: 7 } }),
                // Dies within the first step.
                properties: properties({ particlesPerSecond: 20, lifetime: 0.05 }),
            }),
            emitter({
                name: 'puff',
                properties: properties({ particlesPerSecond: 20, lifetime: 100 }),
            }),
        ]));

        instance.update(0.1);
        instance.update(0.1);

        const puffs = instance.emitterParticles(1);

        assert.ok(puffs.length > 0, 'nothing was spawned when the parent died');
        assert.ok(puffs.every(p => Math.abs(p.position.z - 7) < 1e-6),
            `the puff was not where its parent died: ${puffs[0].position.z}`);
    });
});

describe('heat particles', () => {
    const system = testSystem([
        emitter({ name: 'fire' }),
        emitter({ name: 'heat', properties: properties({ isHeatParticle: true }) }),
    ]);

    /**
     * `PrimHeat.fx` adds no colour: it re-samples the SCENE texture at a screen position offset by
     * the sprite's normals. Drawn as an ordinary sprite it is a normal map added to the image, and
     * `p_particle_heat_master` is mostly flat violet-white - which is where the "flat sheet
     * rectangle" over the flamethrower came from. It goes on a layer of its own so the main pass
     * skips it and the distortion pass can draw it into the heat buffer instead.
     */
    it('draws a heat emitter on the heat layer, out of the ordinary pass', () => {
        const instance = new ParticleSystemInstance('test', system);

        assert.equal(instance.emitterMesh(0)!.layers.isEnabled(HEAT_LAYER), false);
        assert.equal(instance.emitterMesh(1)!.layers.isEnabled(HEAT_LAYER), true);
        assert.equal(instance.emitterMesh(1)!.layers.isEnabled(0), false,
            'a heat sprite must not also draw in the ordinary pass');
    });

    it('still shows and hides one like any other emitter', () => {
        const instance = new ParticleSystemInstance('test', system);

        assert.equal(instance.emitterVisible(1), true);

        instance.setEmitterVisible(1, false);

        assert.equal(instance.emitterVisible(1), false);
    });

    it('says which emitters are heat, so the dock can label them', () => {
        const instance = new ParticleSystemInstance('test', system);

        assert.deepEqual(instance.emitterIsHeat(), [false, true]);
    });

    /** The pass is only worth its two render targets when something is actually distorting. */
    it('reports whether anything needs the distortion pass', () => {
        const instance = new ParticleSystemInstance('test', system);

        assert.equal(instance.hasVisibleHeat, true);

        instance.setEmitterVisible(1, false);

        assert.equal(instance.hasVisibleHeat, false);
    });
});

describe('emission gating', () => {
    function running(): ParticleSystemInstance {
        const system = testSystem([emitter({
            name: 'fire',
            properties: properties({ particlesPerSecond: 20, lifetime: 100 }),
        })]);

        return new ParticleSystemInstance('test', system, new Map(), 1, null, new THREE.Object3D());
    }

    /**
     * An Alamo animation switches a proxy bone off to say "this effect is not running yet", and
     * 2285 of the shipped visibility tracks are exactly that. Without a gate the emitter fires from
     * frame zero of every clip, so a rancor's death explosion plays through its idle.
     */
    it('spawns nothing while emission is switched off', () => {
        const instance = running();
        instance.hold('clip', true);

        instance.update(0.5);

        assert.equal(instance.emitterParticles(0).length, 0);
    });

    /**
     * Switching off must not delete what is already in the air. An effect that stops emitting has
     * its last sparks fly out and die; one that vanishes mid-flight is a rendering fault.
     */
    it('lets the particles already alive finish', () => {
        const instance = running();
        instance.update(0.5);
        const born = instance.emitterParticles(0).length;
        assert.ok(born > 0, 'nothing spawned to begin with');

        instance.hold('clip', true);
        instance.update(0.1);

        assert.equal(instance.emitterParticles(0).length, born);
    });

    it('spawns again once emission comes back', () => {
        const instance = running();
        instance.hold('clip', true);
        instance.update(0.5);
        instance.hold('clip', false);
        instance.update(0.5);

        assert.ok(instance.emitterParticles(0).length > 0);
    });

    /**
     * The defect a wholesale `setEmitting(true)` caused, in its own right.
     *
     * More than one thing can want an effect held, and they do not know about each other. The clip
     * holds a proxy whose moment has not come; hiding the part a system hangs off holds it too. But
     * `applyEmitterGating` runs EVERY frame and, with no clip playing, resolved "the clip says
     * nothing" to `setEmitting(true)` - so it re-armed the emitters of a part that had been hidden,
     * one frame after they were held. `stopAnimation` did the same for the whole scene.
     *
     * A default is not a permission: a link with no opinion must decline, not vote yes.
     */
    it('stays held while a second reason still stands', () => {
        const instance = running();

        instance.hold('part', true);
        instance.hold('clip', true);

        // The clip stops having an opinion. The part is still hidden, so nothing may spawn.
        instance.hold('clip', false);
        instance.update(0.5);

        assert.equal(instance.emitterParticles(0).length, 0, 'the part hold was overridden');

        // And when the last reason goes, it runs again.
        instance.hold('part', false);
        instance.update(0.5);

        assert.ok(instance.emitterParticles(0).length > 0, 'never resumed');
    });

    /** Releasing a hold nobody placed is not a decision either - it must not start anything. */
    it('is unmoved by a hold that was never placed', () => {
        const instance = running();

        instance.hold('part', true);
        instance.hold('clip', false);
        instance.update(0.5);

        assert.equal(instance.emitterParticles(0).length, 0);
    });

    it('emits by default, since most systems are never gated', () => {
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
    it('spawns nothing while the system is not drawn', () => {
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
    it('fires a burst that was held hidden, when it is finally shown', () => {
        const system = testSystem([emitter({
            name: 'boom',
            properties: properties({
                useBursts: true, particlesPerBurst: 12, burstDelay: 0.1, burstCount: 1,
                lifetime: 100,
            }),
        })]);
        const instance = new ParticleSystemInstance(
            'test', system, new Map(), 1, null, new THREE.Object3D());

        instance.setVisible(false);

        // Far longer than the burst schedule: the whole point is that the clock cannot run out
        // while nobody is looking.
        for (let i = 0; i < 100; i++) {
            instance.update(0.1);
        }

        assert.equal(instance.emitterParticles(0).length, 0, 'emitted while hidden');
        assert.equal(instance.exhausted, false, 'burnt out while hidden');

        instance.setVisible(true);
        instance.update(0.2);

        assert.equal(instance.emitterParticles(0).length, 12);
    });

    /**
     * The same rule one level down. Unticking a single emitter in the dock is the reader saying
     * they do not want to see it; an emitter that goes on filling its buffer behind that would
     * be back mid-life the moment they tick it again, and a burst would be gone for good.
     */
    it('spawns nothing while one emitter alone is hidden', () => {
        const instance = running();
        instance.setEmitterVisible(0, false);

        instance.update(0.5);
        assert.equal(instance.emitterParticles(0).length, 0);

        instance.setEmitterVisible(0, true);
        instance.update(0.5);
        assert.ok(instance.emitterParticles(0).length > 0, 'never came back');
    });

    /**
     * Hiding must not delete what is in the air, for the same reason switching emission off does
     * not: an effect held mid-flight and shown again would otherwise pop back fully populated.
     */
    it('lets the particles already alive finish while hidden', () => {
        const instance = running();
        instance.update(0.5);
        const born = instance.emitterParticles(0).length;
        assert.ok(born > 0, 'nothing spawned to begin with');

        instance.setVisible(false);
        instance.update(0.1);

        assert.equal(instance.emitterParticles(0).length, born);
    });
});

describe('the owning object Scale_Factor', () => {
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
    const drawn = (scaleFactor?: number): { offsets: number[]; sizes: number[] } => {
        const system = testSystem([emitter({
            properties: properties({ particlesPerSecond: 40, lifetime: 100 }),
        })]);

        const instance = new ParticleSystemInstance(
            'p', system, new Map(), 1, null, null, scaleFactor);

        instance.update(0.5);

        const mesh = instance.root.children[0] as THREE.Mesh;
        const live = instance.emitterParticles(0).length;

        return {
            offsets: [...(mesh.geometry.getAttribute('iOffset').array as Float32Array)]
                .slice(0, live * 3),
            sizes: [...(mesh.geometry.getAttribute('iSize').array as Float32Array)].slice(0, live),
        };
    };

    it('scales where a particle is and how big it draws, by the same amount', () => {
        // The same seed, so the two runs differ only by the factor.
        const plain = drawn(1);
        const scaled = drawn(20);

        assert.ok(plain.sizes.length > 0, 'nothing was drawn to compare');
        assert.deepEqual(
            scaled.sizes.map(s => +(s / 20).toFixed(4)),
            plain.sizes.map(s => +s.toFixed(4)));
        assert.deepEqual(
            scaled.offsets.map(o => +(o / 20).toFixed(3)),
            plain.offsets.map(o => +o.toFixed(3)));
    });

    it('leaves a system that declares none exactly as it was', () => {
        // 101 of the 107 shipped particle objects declare 1.0 or nothing, so this is the path that
        // must not move.
        assert.deepEqual(drawn(undefined), drawn(1));
    });
});
