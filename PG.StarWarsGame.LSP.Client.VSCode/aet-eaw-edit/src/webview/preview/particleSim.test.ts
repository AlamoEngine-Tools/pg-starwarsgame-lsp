// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    accelerationIn, appearanceOf, atlasFrame, emitterExtent, sampleTrack, sampleVolume, seededRandom,
    SpawnClock, spawnParticle, stepParticle, systemExtent, volumeExtent,
} from './particleSim';
import { emitter, properties, track, volume } from './particleFixture';
import type { AlamoEmitterProperties } from '../../protocol/modelPreview';

// ── track sampling ───────────────────────────────────────────────────────────

describe('sampleTrack', () => {
    const ramp = track('Alpha', [[0, 0], [1, 1]]);

    it('interpolates linearly between keys', () => {
        assert.equal(sampleTrack(ramp, 0), 0);
        assert.equal(sampleTrack(ramp, 0.5), 0.5);
        assert.equal(sampleTrack(ramp, 1), 1);
    });

    it('clamps outside the curve rather than extrapolating', () => {
        // A particle can reach exactly 1.0 on its last step and drift a hair past; extrapolating
        // there would push alpha negative and flicker the particle out early.
        assert.equal(sampleTrack(ramp, -0.5), 0);
        assert.equal(sampleTrack(ramp, 1.5), 1);
    });

    it('picks the right segment of a multi-key curve', () => {
        const curve = track('Scale', [[0, 0], [0.25, 4], [1, 0]]);

        assert.equal(sampleTrack(curve, 0.125), 2);
        assert.equal(sampleTrack(curve, 0.25), 4);
        assert.equal(sampleTrack(curve, 0.625), 2);
    });

    /** A texture-index track is why this mode exists; frame 3.5 is not a frame. */
    it('holds the previous value in step mode', () => {
        const frames = track('TextureIndex', [[0, 0], [0.5, 3], [1, 7]], 'Step');

        assert.equal(sampleTrack(frames, 0.25), 0);
        assert.equal(sampleTrack(frames, 0.5), 3);
        assert.equal(sampleTrack(frames, 0.75), 3);
    });

    it('eases in and out in smooth mode but keeps the endpoints', () => {
        const smooth = track('Alpha', [[0, 0], [1, 1]], 'Smooth');

        assert.equal(sampleTrack(smooth, 0), 0);
        assert.equal(sampleTrack(smooth, 1), 1);
        assert.equal(sampleTrack(smooth, 0.5), 0.5);
        // Slower at the start than linear - that is the whole difference.
        assert.ok(sampleTrack(smooth, 0.25) < 0.25);
        assert.ok(sampleTrack(smooth, 0.75) > 0.75);
    });

    it('survives coincident keys instead of dividing by zero', () => {
        const stacked = track('Alpha', [[0, 0], [0.5, 1], [0.5, 0], [1, 0]]);

        assert.ok(Number.isFinite(sampleTrack(stacked, 0.5)));
    });

    it('returns zero for an empty track', () => {
        assert.equal(sampleTrack(track('Alpha', []), 0.5), 0);
    });
});

// ── spawn volumes ────────────────────────────────────────────────────────────

describe('sampleVolume', () => {
    it('returns the exact value for a point, ignoring the generator', () => {
        const point = volume({ shape: 'Point', exactValue: { x: 1, y: 2, z: 3 } });

        assert.deepEqual(sampleVolume(point, seededRandom(1)), { x: 1, y: 2, z: 3 });
    });

    it('stays inside a box', () => {
        const box = volume({ shape: 'Box', min: { x: -1, y: 0, z: 2 }, max: { x: 1, y: 5, z: 4 } });
        const random = seededRandom(7);

        for (let i = 0; i < 200; i++) {
            const p = sampleVolume(box, random);
            assert.ok(p.x >= -1 && p.x <= 1, `x was ${p.x}`);
            assert.ok(p.y >= 0 && p.y <= 5, `y was ${p.y}`);
            assert.ok(p.z >= 2 && p.z <= 4, `z was ${p.z}`);
        }
    });

    it('centres a cube on the origin', () => {
        const cube = volume({ shape: 'Cube', sideLength: 4 });
        const random = seededRandom(3);

        for (let i = 0; i < 200; i++) {
            const p = sampleVolume(cube, random);
            for (const v of [p.x, p.y, p.z]) {
                assert.ok(v >= -2 && v <= 2, `component was ${v}`);
            }
        }
    });

    it('keeps a sphere sample inside its radius', () => {
        const sphere = volume({ shape: 'Sphere', sphereRadius: 3 });
        const random = seededRandom(11);

        for (let i = 0; i < 300; i++) {
            const p = sampleVolume(sphere, random);
            assert.ok(Math.hypot(p.x, p.y, p.z) <= 3.0001);
        }
    });

    /** Edge-only means a shell: the engine uses it for ring and burst effects. */
    it('puts every edge-only sphere sample on the surface', () => {
        const shell = volume({ shape: 'Sphere', sphereRadius: 3, sphereEdgeOnly: true });
        const random = seededRandom(5);

        for (let i = 0; i < 200; i++) {
            const p = sampleVolume(shell, random);
            assert.ok(Math.abs(Math.hypot(p.x, p.y, p.z) - 3) < 1e-6);
        }
    });

    it('keeps a cylinder sample within its radius and height', () => {
        const cylinder = volume({ shape: 'Cylinder', cylinderRadius: 2, cylinderHeight: 10 });
        const random = seededRandom(13);

        for (let i = 0; i < 300; i++) {
            const p = sampleVolume(cylinder, random);
            assert.ok(Math.hypot(p.x, p.z) <= 2.0001);
            assert.ok(p.y >= 0 && p.y <= 10);
        }
    });

    /** A uniform radius crowds the axis, because area grows with it. */
    it('spreads a solid cylinder by area, not by radius', () => {
        const cylinder = volume({ shape: 'Cylinder', cylinderRadius: 1, cylinderHeight: 1 });
        const random = seededRandom(19);
        const n = 2000;

        let outerHalf = 0;
        for (let i = 0; i < n; i++) {
            const p = sampleVolume(cylinder, random);
            if (Math.hypot(p.x, p.z) > Math.SQRT1_2) {
                outerHalf++;
            }
        }

        // The outer half by AREA is r > 1/sqrt(2), so it should hold about half the samples.
        assert.ok(Math.abs(outerHalf / n - 0.5) < 0.06, `outer half held ${outerHalf / n}`);
    });
});

describe('seededRandom', () => {
    it('gives the same sequence for the same seed', () => {
        const a = seededRandom(42);
        const b = seededRandom(42);

        for (let i = 0; i < 20; i++) {
            assert.equal(a(), b());
        }
    });

    it('stays in range', () => {
        const random = seededRandom(1);
        for (let i = 0; i < 500; i++) {
            const v = random();
            assert.ok(v >= 0 && v < 1, `got ${v}`);
        }
    });
});

// ── spawn scheduling ─────────────────────────────────────────────────────────

describe('SpawnClock', () => {
    /**
     * At 40 a second and a 16ms frame an emitter owes 0.64 of a particle. Rounding that away every
     * frame emits nothing at all, which is the bug this carries a remainder to avoid.
     */
    it('carries the fractional remainder across frames', () => {
        const clock = new SpawnClock(emitter({ properties: properties({ particlesPerSecond: 40 }) }));

        let total = 0;
        for (let i = 0; i < 60; i++) {
            total += clock.advance(1 / 60);
        }

        assert.ok(total >= 39 && total <= 41, `emitted ${total} in a second, expected about 40`);
    });

    it('emits nothing until the initial delay has passed', () => {
        const clock = new SpawnClock(
            emitter({ properties: properties({ particlesPerSecond: 100, initialDelay: 0.5 }) }));

        assert.equal(clock.advance(0.25), 0);
        assert.ok(clock.advance(0.5) > 0);
    });

    it('fires a burst of the whole count at once', () => {
        const clock = new SpawnClock(emitter({
            properties: properties({ useBursts: true, particlesPerBurst: 25, burstDelay: 1 }),
        }));

        assert.equal(clock.advance(0.01), 25);
        assert.equal(clock.advance(0.5), 0);
        assert.equal(clock.advance(0.6), 25);
    });

    it('stops after the last burst and says so', () => {
        const clock = new SpawnClock(emitter({
            properties: properties({
                useBursts: true, particlesPerBurst: 3, burstDelay: 0.1, burstCount: 2,
            }),
        }));

        let total = 0;
        for (let i = 0; i < 50; i++) {
            total += clock.advance(0.05);
        }

        assert.equal(total, 6);
        assert.equal(clock.finished, true);
    });

    /** Zero means forever, which is how a continuous burst emitter is authored. */
    it('never finishes when the burst count is zero', () => {
        const clock = new SpawnClock(emitter({
            properties: properties({ useBursts: true, particlesPerBurst: 1, burstDelay: 0.1 }),
        }));

        for (let i = 0; i < 30; i++) {
            clock.advance(0.05);
        }

        assert.equal(clock.finished, false);
    });
});

// ── particle life ────────────────────────────────────────────────────────────

describe('spawnParticle', () => {
    it('takes its velocity from the speed volume and its place from the position volume', () => {
        const p = spawnParticle(emitter({
            speed: volume({ exactValue: { x: 0, y: 5, z: 0 } }),
            position: volume({ exactValue: { x: 1, y: 2, z: 3 } }),
        }), seededRandom(1));

        assert.deepEqual(p.velocity, { x: 0, y: 5, z: 0 });
        assert.deepEqual(p.position, { x: 1, y: 2, z: 3 });
    });

    it('falls back to the property lifetime when the volume gives none', () => {
        const p = spawnParticle(
            emitter({ properties: properties({ lifetime: 2.5 }) }), seededRandom(1));

        assert.equal(p.lifetime, 2.5);
    });

    it('varies the lifetime within the declared percentage', () => {
        const e = emitter({
            properties: properties({ lifetime: 10, randomLifetimePercent: 0.5 }),
        });
        const random = seededRandom(23);

        const lifetimes = Array.from({ length: 200 }, () => spawnParticle(e, random).lifetime);

        assert.ok(Math.min(...lifetimes) >= 5, 'shorter than the range allows');
        assert.ok(Math.max(...lifetimes) <= 15, 'longer than the range allows');
        assert.ok(new Set(lifetimes).size > 50, 'every particle got the same lifetime');
    });

    it('never gives a particle a zero lifetime', () => {
        // Dividing by it is how the appearance is sampled, so zero would be a division by zero on
        // every frame of a particle that should simply not exist.
        const p = spawnParticle(
            emitter({ properties: properties({ lifetime: 0 }) }), seededRandom(1));

        assert.ok(p.lifetime > 0);
    });
});

describe('stepParticle', () => {
    it('moves by its velocity', () => {
        const e = emitter();
        const p = spawnParticle(e, seededRandom(1));
        p.velocity = { x: 1, y: 0, z: 0 };
        p.position = { x: 0, y: 0, z: 0 };

        stepParticle(p, e, 0.5);

        assert.equal(p.position.x, 0.5);
    });

    it('pulls downwards under gravity', () => {
        const e = emitter({ properties: properties({ gravity: 10 }) });
        const p = spawnParticle(e, seededRandom(1));
        p.velocity = { x: 0, y: 0, z: 0 };

        stepParticle(p, e, 0.1);

        assert.ok(p.velocity.y < 0, 'gravity pushed the particle up');
    });

    it('retires a particle once it is older than its lifetime', () => {
        const e = emitter({ properties: properties({ lifetime: 1 }) });
        const p = spawnParticle(e, seededRandom(1));

        assert.equal(stepParticle(p, e, 0.5), true);
        assert.equal(stepParticle(p, e, 0.6), false);
    });

    describe('ground behaviour', () => {
        function falling(behavior: AlamoEmitterProperties['groundBehavior']) {
            const e = emitter({ properties: properties({ groundBehavior: behavior, lifetime: 10 }) });
            const p = spawnParticle(e, seededRandom(1));
            p.position = { x: 0, y: 0.1, z: 0 };
            p.velocity = { x: 1, y: -10, z: 0 };
            return { e, p };
        }

        it('bounces back up, damped', () => {
            const { e, p } = falling('Bounce');

            stepParticle(p, e, 0.1);

            assert.equal(p.position.y, 0);
            assert.ok(p.velocity.y > 0, 'did not bounce');
            assert.ok(p.velocity.y < 10, 'bounced without losing energy');
        });

        it('stops dead when it sticks', () => {
            const { e, p } = falling('Stick');

            stepParticle(p, e, 0.1);

            assert.deepEqual(p.velocity, { x: 0, y: 0, z: 0 });
            assert.equal(p.position.y, 0);
        });

        it('retires the particle when it should disappear', () => {
            const { e, p } = falling('Disappear');

            stepParticle(p, e, 0.1);

            assert.equal(stepParticle(p, e, 0.001), false);
        });

        /**
         * The ground is the SCENE's, not the emitter's.
         *
         * `particle.position` is relative to the system's root, which is parented to a bone - so
         * testing `y < 0` put the ground wherever that bone happened to be. An emitter attached 50
         * units up a hull had its dirt vanish the instant it was thrown, 50 units in the air, and
         * one attached below the origin let it fall through the floor. `groundY` is that offset,
         * expressed in the same local space the particle lives in.
         */
        it('measures the ground from the scene, not from the emitter', () => {
            const { e, p } = falling('Disappear');
            p.position = { x: 0, y: 0.1, z: 0 };

            // The system hangs 50 units up, so the ground is 50 units BELOW the particle's origin.
            stepParticle(p, e, 0.1, -50);

            assert.equal(stepParticle(p, e, 0.001, -50), true,
                'retired at the emitter height instead of at the ground 50 units below');
            assert.ok(p.position.y < 0, 'stopped at the emitter instead of falling past it');
        });

        it('still retires a particle that reaches a raised ground', () => {
            const { e, p } = falling('Disappear');
            p.position = { x: 0, y: 20.1, z: 0 };

            // Mounted 20 units DOWN, so the scene's ground sits at local +20.
            stepParticle(p, e, 0.1, 20);

            assert.equal(stepParticle(p, e, 0.001, 20), false);
        });

        it('passes straight through when there is no ground behaviour', () => {
            const { e, p } = falling('None');

            stepParticle(p, e, 0.1);

            assert.ok(p.position.y < 0, 'stopped at the ground with no ground behaviour');
        });
    });
});

// ── appearance ───────────────────────────────────────────────────────────────

describe('spin', () => {
    /**
     * The rotation track is a SPEED, and the engine INTEGRATES it over the particle's life
     * (`EmitterInstance.cpp:574-579` through `IntegrateTrack`) - it does not sample the first key
     * and call that a constant. `p_boba_jetpack`'s smoke ramps 15 down to 1, so reading the first
     * key spins it nearly twice as fast as the file asks for its whole life.
     */
    it('integrates the speed track rather than holding its first key', () => {
        const e = emitter({
            tracks: [track('RotationSpeed', [[0, 4], [1, 0]])],
            properties: properties({ lifetime: 2, randomRotationDirection: false }),
        });

        const p = spawnParticle(e, seededRandom(1));
        p.age = 2;

        // Mean speed 2 turns a second over 2 seconds: 4 turns, not the 8 the first key implies.
        assert.ok(Math.abs(appearanceOf(p, e).rotation - 4) < 1e-6,
            `${appearanceOf(p, e).rotation}`);
    });

    it('turns at the track`s rate for a constant speed', () => {
        const e = emitter({
            tracks: [track('RotationSpeed', [[0, 3], [1, 3]])],
            properties: properties({ lifetime: 2, randomRotationDirection: false }),
        });

        const p = spawnParticle(e, seededRandom(1));
        p.age = 1;

        assert.ok(Math.abs(appearanceOf(p, e).rotation - 3) < 1e-6,
            `${appearanceOf(p, e).rotation}`);
    });

    /** Every particle starts square-on unless the emitter asks for a random angle. */
    it('starts a particle unrotated', () => {
        const e = emitter({ tracks: [track('RotationSpeed', [[0, 5], [1, 5]])] });

        assert.equal(appearanceOf(spawnParticle(e, seededRandom(9)), e).rotation, 0);
    });

    /**
     * `randomRotation` is a fixed ANGLE, not a speed: the engine sets `m_baseRotation` from the
     * average and then does NOT integrate the track at all. Treating it as a speed set every one of
     * those particles spinning.
     */
    it('holds a random-rotation particle at its angle instead of spinning it', () => {
        const e = emitter({
            tracks: [track('RotationSpeed', [[0, 5], [1, 5]])],
            properties: properties({
                lifetime: 4, randomRotation: true, randomRotationAverage: 0.25,
                randomRotationVariance: 0, randomRotationDirection: false,
            }),
        });

        const p = spawnParticle(e, seededRandom(1));
        const born = appearanceOf(p, e).rotation;
        p.age = 4;

        assert.ok(Math.abs(born - 0.25) < 1e-6, `${born}`);
        assert.ok(Math.abs(appearanceOf(p, e).rotation - 0.25) < 1e-6, 'it spun');
    });

    /** `randomRotationDirection` flips the whole rotation, spin and fixed angle alike. */
    it('spins some particles the other way when the emitter asks', () => {
        const e = emitter({
            tracks: [track('RotationSpeed', [[0, 2], [1, 2]])],
            properties: properties({ lifetime: 1, randomRotationDirection: true }),
        });

        const seen = new Set<number>();
        for (let seed = 1; seed < 30; seed++) {
            const p = spawnParticle(e, seededRandom(seed));
            p.age = 1;
            seen.add(Math.sign(appearanceOf(p, e).rotation));
        }

        assert.deepEqual([...seen].sort(), [-1, 1]);
    });
});

describe('inward motion', () => {
    /**
     * Half the corpus - 508 of 1019 emitters - sets `inwardSpeed`, and it was doing nothing at all.
     * The engine throws the particle along the direction it was BORN in:
     * `initialSpeed -= normpos * inwardSpeed` (`EmitterInstance.cpp:339`), where the reader has
     * already negated the stored value.
     */
    it('throws a particle along the direction it was born in', () => {
        const e = emitter({
            // Born at a fixed offset, so the direction is known.
            position: volume({ shape: 'Point', exactValue: { x: 3, y: 0, z: 4 } }),
            properties: properties({ inwardSpeed: -10 }),
        });

        const p = spawnParticle(e, seededRandom(1));

        // Straight out along the spawn direction, which is (0.6, 0, 0.8).
        assert.ok(Math.abs(p.velocity.x - -6) < 1e-6, `${p.velocity.x}`);
        assert.ok(Math.abs(p.velocity.z - -8) < 1e-6, `${p.velocity.z}`);
    });

    it('leaves a particle born at the origin alone rather than dividing by zero', () => {
        const e = emitter({ properties: properties({ inwardSpeed: -10 }) });

        const p = spawnParticle(e, seededRandom(1));

        assert.ok(Number.isFinite(p.velocity.x) && p.velocity.x === 0, `${p.velocity.x}`);
    });

    /**
     * The acceleration works the same way and is fixed at BIRTH - the engine keeps `normpos`, the
     * normalised spawn offset, not the running position. Measuring from where the particle has got
     * to turns a steady pull into one that swings round as it travels; and since the simulation
     * moved into model space, "the running position" was being measured from the MODEL's origin
     * rather than the emitter's.
     */
    it('keeps pulling along the birth direction, not the current position', () => {
        const e = emitter({
            position: volume({ shape: 'Point', exactValue: { x: 0, y: 0, z: 1 } }),
            properties: properties({ inwardAcceleration: -4, lifetime: 10 }),
        });

        const p = spawnParticle(e, seededRandom(1));
        // Somewhere else entirely by now.
        p.position = { x: 100, y: 0, z: -50 };

        stepParticle(p, e, 1);

        assert.ok(Math.abs(p.velocity.z - -4) < 1e-6, `${p.velocity.z}`);
        assert.ok(Math.abs(p.velocity.x) < 1e-6, `pulled sideways: ${p.velocity.x}`);
    });
});

describe('wind', () => {
    /**
     * `if (affectedByWind) particle.m_initialSpeed += m_engine.GetWind()` - the editor adds it once,
     * at birth. 484 of the corpus's 1019 emitters set the flag and it was doing nothing, so half
     * the smoke in the game hung in dead air.
     */
    it('throws a wind-affected particle downwind at birth', () => {
        const e = emitter({ properties: properties({ affectedByWind: true }) });

        const p = spawnParticle(e, seededRandom(1), { x: 3, y: 0, z: -4 });

        assert.deepEqual(p.velocity, { x: 3, y: 0, z: -4 });
    });

    it('leaves an emitter that ignores the wind alone', () => {
        const p = spawnParticle(emitter(), seededRandom(1), { x: 3, y: 0, z: -4 });

        assert.deepEqual(p.velocity, { x: 0, y: 0, z: 0 });
    });
});

describe('acceleration axes', () => {
    /**
     * The file's acceleration is in ALAMO axes - Z is up - while the simulation runs in the model's
     * glTF space, where Y is up. 59 emitters set one, 13 of them off the vertical, and every one of
     * those was being pushed sideways.
     */
    it('turns a file acceleration into the space the simulation runs in', () => {
        const e = emitter({
            properties: properties({ acceleration: { x: 1, y: 2, z: 3 }, lifetime: 10 }),
        });

        const p = spawnParticle(e, seededRandom(1));

        stepParticle(p, e, 1);

        // Alamo (x, y, z) becomes (x, z, -y).
        assert.ok(Math.abs(p.velocity.x - 1) < 1e-6, `${p.velocity.x}`);
        assert.ok(Math.abs(p.velocity.y - 3) < 1e-6, `${p.velocity.y}`);
        assert.ok(Math.abs(p.velocity.z - -2) < 1e-6, `${p.velocity.z}`);
    });

    /** The same turn, on its own, so the renderer can ask for it without stepping anything. */
    it('reports the acceleration it will apply', () => {
        const p = properties({ acceleration: { x: 1, y: 2, z: 3 }, gravity: 4 });

        assert.deepEqual(accelerationIn(p), { x: 1, y: -1, z: -2 });
    });

    /** Gravity is a scalar down Alamo's Z, which is the simulation's own down. */
    it('pulls gravity straight down', () => {
        const e = emitter({ properties: properties({ gravity: 9.8, lifetime: 10 }) });

        const p = spawnParticle(e, seededRandom(1));

        stepParticle(p, e, 1);

        assert.ok(Math.abs(p.velocity.y - -9.8) < 1e-6, `${p.velocity.y}`);
    });
});

describe('appearanceOf', () => {
    it('samples every channel at the particle age', () => {
        const e = emitter({
            tracks: [
                track('Red', [[0, 1], [1, 0]]),
                track('Alpha', [[0, 1], [1, 0]]),
                track('Scale', [[0, 2], [1, 6]]),
            ],
            properties: properties({ lifetime: 2 }),
        });

        const p = spawnParticle(e, seededRandom(1));
        p.age = 1;

        const look = appearanceOf(p, e);

        assert.equal(look.r, 0.5);
        assert.equal(look.a, 0.5);

        // HALF the sampled scale. Both references agree and neither is obvious: the editor builds
        // its quad at `baseScale * scaleSample / 2` (`EmitterInstance.cpp:538`) and alo-viewer
        // converts the same track into a size plugin at `value / 2` (`ParticleSystem.cpp:734`),
        // then draws at +/-size. Drawing at the raw value makes every sprite twice as wide and
        // four times the area - which on an additive effect reads as far too intense.
        assert.equal(look.size, 2);
    });

    /**
     * `randomScalePerc` only ever SHRINKS a particle: `GetRandom(1 - randomScalePerc, 1)`. A
     * symmetric jitter would let sprites grow past the size the author set.
     */
    it('never jitters a particle larger than its track says', () => {
        const e = emitter({
            tracks: [track('Scale', [[0, 10], [1, 10]])],
            properties: properties({ randomScalePercent: 0.5 }),
        });

        for (let seed = 1; seed < 40; seed++) {
            const look = appearanceOf(spawnParticle(e, seededRandom(seed)), e);

            assert.ok(look.size <= 5 + 1e-9, `grew past half the track: ${look.size}`);
            assert.ok(look.size >= 2.5 - 1e-9, `shrank past the variation: ${look.size}`);
        }
    });

    /**
     * `randomColors` ADDS, it does not scale.
     *
     * `ColorVarianceModifierPlugin::InitializeParticle` draws `GetRandom(m_min, m_max)` per channel
     * with `m_min` zero, then `p->color = saturate(p->color + addition)`. Read as a multiplier -
     * `1 - random() * randomColors` - it does the opposite of what the author asked for: a value
     * meant to brighten some of the particles darkened all of them instead.
     */
    it('brightens a particle by the random colour rather than dimming it', () => {
        const e = emitter({
            tracks: [track('Red', [[0, 0.5], [1, 0.5]]), track('Green', [[0, 0.5], [1, 0.5]])],
            properties: properties({ randomColors: { x: 0.4, y: 0.4, z: 0, w: 0 } }),
        });

        for (let seed = 1; seed < 40; seed++) {
            const look = appearanceOf(spawnParticle(e, seededRandom(seed)), e);

            assert.ok(look.r >= 0.5 - 1e-9, `darkened the track: ${look.r}`);
            assert.ok(look.r <= 0.9 + 1e-9, `added more than declared: ${look.r}`);
        }
    });

    it('saturates rather than running past white', () => {
        const e = emitter({
            tracks: [track('Red', [[0, 0.9], [1, 0.9]])],
            properties: properties({ randomColors: { x: 1, y: 0, z: 0, w: 0 } }),
        });

        for (let seed = 1; seed < 20; seed++) {
            assert.ok(appearanceOf(spawnParticle(e, seededRandom(seed)), e).r <= 1);
        }
    });

    it('adds to ALPHA too, which is the channel a transparent sprite is read through', () => {
        const e = emitter({
            tracks: [track('Alpha', [[0, 0.2], [1, 0.2]])],
            properties: properties({ randomColors: { x: 0, y: 0, z: 0, w: 0.5 } }),
        });

        const seen = new Set<number>();

        for (let seed = 1; seed < 20; seed++) {
            seen.add(appearanceOf(spawnParticle(e, seededRandom(seed)), e).a);
        }

        assert.ok([...seen].every(a => a >= 0.2 - 1e-9 && a <= 0.7 + 1e-9));
        assert.ok(seen.size > 1, 'the alpha addition is not random at all');
    });

    /**
     * `colorAddGrayscale` makes the addition MONOCHROME - one random value on every channel,
     * alpha included: `addition.g = m_grayscale ? addition.r : GetRandom(...)`. Without it an
     * emitter that asked for a brightness jitter got a colour jitter.
     */
    it('uses one random value on every channel when the addition is grayscale', () => {
        const e = emitter({
            tracks: [
                track('Red', [[0, 0], [1, 0]]),
                track('Green', [[0, 0], [1, 0]]),
                track('Blue', [[0, 0], [1, 0]]),
            ],
            properties: properties({
                randomColors: { x: 0.5, y: 0.5, z: 0.5, w: 0 },
                colorAddGrayscale: true,
            }),
        });

        for (let seed = 1; seed < 20; seed++) {
            const look = appearanceOf(spawnParticle(e, seededRandom(seed)), e);

            assert.ok(Math.abs(look.r - look.g) < 1e-9, `${look.r} vs ${look.g}`);
            assert.ok(Math.abs(look.r - look.b) < 1e-9, `${look.r} vs ${look.b}`);
        }
    });

    it('defaults a missing channel to opaque white at full size', () => {
        const p = spawnParticle(emitter(), seededRandom(1));

        const look = appearanceOf(p, emitter());

        assert.equal(look.r, 1);
        assert.equal(look.a, 1);
        assert.equal(look.size, 0.5);
    });

    it('reports a whole frame index', () => {
        const e = emitter({ tracks: [track('TextureIndex', [[0, 0], [1, 8]])] });
        const p = spawnParticle(e, seededRandom(1));
        p.age = 0.5;
        p.lifetime = 1;

        assert.equal(appearanceOf(p, e).frame, 4);
        assert.ok(Number.isInteger(appearanceOf(p, e).frame));
    });
});

describe('atlasFrame', () => {
    // textureSize is the FRAME COUNT, not a pixel size. The engine squares it to get the grid:
    // `int texsize = ceil(sqrt(e.textureSize))` (alo-viewer ParticleSystem.cpp:754). Reading it as a
    // pixel edge put p_explosion_huge01's fire on a 32x32 grid instead of 4x4, so every frame sampled
    // a sliver of the wrong cell and the sprite came out a flat square.
    it('squares the frame count to get the grid', () => {
        const first = atlasFrame(0, 16);

        assert.equal(first.size, 0.25);
        assert.equal(first.u, 0);
        assert.equal(first.v, 0);
    });

    it('walks across then down, top-down like the engine', () => {
        assert.equal(atlasFrame(1, 16).u, 0.25);
        assert.equal(atlasFrame(4, 16).u, 0);
        assert.equal(atlasFrame(4, 16).v, 0.25);
    });

    it('rounds a count that is not a square up to the next grid', () => {
        // 60 frames does not tile evenly; the engine takes the ceiling and leaves the remainder blank.
        assert.equal(atlasFrame(0, 60).size, 1 / 8);
    });

    it('wraps rather than running off the sheet', () => {
        assert.deepEqual(atlasFrame(16, 16), atlasFrame(0, 16));
    });

    it('treats a single-frame texture as one cell', () => {
        assert.equal(atlasFrame(0, 1).size, 1);
    });

    it('does not divide by a zero frame count', () => {
        assert.ok(Number.isFinite(atlasFrame(0, 0).size));
        assert.equal(atlasFrame(0, 0).size, 1);
    });
});

describe('volumeExtent', () => {
    it('measures a sphere by its radius', () => {
        assert.equal(volumeExtent(volume({ shape: 'Sphere', sphereRadius: 3 })), 3);
    });

    it('measures a box by its furthest corner', () => {
        const box = volume({ shape: 'Box', min: { x: -1, y: 0, z: 0 }, max: { x: 0, y: 4, z: 0 } });
        assert.equal(volumeExtent(box), 4);
    });

    it('measures a cube to its corner, not its face', () => {
        // Half the side would put the corners outside the frame.
        const cube = volume({ shape: 'Cube', sideLength: 2 });
        assert.ok(Math.abs(volumeExtent(cube) - Math.sqrt(3)) < 1e-6);
    });

    it('measures a cylinder across radius and height together', () => {
        const cylinder = volume({ shape: 'Cylinder', cylinderRadius: 3, cylinderHeight: 4 });
        assert.equal(volumeExtent(cylinder), 5);
    });

    it('measures a point by how far off origin it sits', () => {
        assert.equal(volumeExtent(volume({ exactValue: { x: 0, y: -7, z: 0 } })), 7);
    });
});

describe('emitterExtent', () => {
    it('carries the spawn offset out along the particle path', () => {
        // Spawned within 2 units, travelling 3 a second for 4 seconds: the cloud reaches 14.
        const wide = emitter({
            position: volume({ shape: 'Sphere', sphereRadius: 2 }),
            speed: volume({ shape: 'Sphere', sphereRadius: 3 }),
            properties: properties({ lifetime: 4 }),
        });

        assert.equal(emitterExtent(wide), 14);
    });

    it('prefers the lifetime volume over the scalar when it has one', () => {
        const fromVolume = emitter({
            speed: volume({ shape: 'Sphere', sphereRadius: 1 }),
            lifetime: volume({ exactValue: { x: 10, y: 0, z: 0 } }),
            properties: properties({ lifetime: 1 }),
        });

        assert.equal(emitterExtent(fromVolume), 10);
    });

    it('counts the sprite itself, which is often the bigger half', () => {
        // p_explosion_huge01's smoke travels about 14 units and then draws itself 224 across. Framing
        // the travel alone puts most of the effect outside the viewport.
        const big = emitter({
            speed: volume({ shape: 'Sphere', sphereRadius: 1 }),
            properties: properties({ lifetime: 1 }),
            tracks: [track('Scale', [[0, 10], [1, 100]])],
        });

        assert.equal(emitterExtent(big), 101);
    });

    it('is never zero, so a still emitter still gets framed', () => {
        // Everything at the origin with no speed. A zero here collapses the camera onto its own
        // target and the viewport goes black - grid included.
        assert.ok(emitterExtent(emitter()) > 0);
    });
});

describe('systemExtent', () => {
    const reaching = (distance: number, sprite = 0) => emitter({
        speed: volume({ shape: 'Sphere', sphereRadius: distance }),
        properties: properties({ lifetime: 1 }),
        tracks: sprite > 0 ? [track('Scale', [[0, sprite], [1, sprite]])] : [],
    });

    it('ignores the one emitter that flies far past the rest', () => {
        // p_explosion_huge01's debris reaches 1290 units while its smoke and fire sit inside 350.
        // Framing the debris shows the explosion as a dot in an empty field.
        const extent = systemExtent(
            [reaching(100), reaching(200), reaching(300), reaching(1290)]);

        assert.ok(extent < 1290, `expected the outlier to be ignored, got ${extent}`);
        assert.ok(extent >= 200, `expected the bulk to still fit, got ${extent}`);
    });

    it('never crops the biggest sprite, however few emitters draw it', () => {
        // One huge puff among small fast specks is still the thing you came to look at.
        const extent = systemExtent([reaching(1), reaching(1), reaching(1), reaching(0, 500)]);

        assert.ok(extent >= 500, `expected the sprite to fit, got ${extent}`);
    });

    it('frames a single emitter by itself', () => {
        assert.equal(systemExtent([reaching(40)]), 40);
    });

    it('falls back to something visible when there are no emitters', () => {
        assert.ok(systemExtent([]) > 0);
    });
});
