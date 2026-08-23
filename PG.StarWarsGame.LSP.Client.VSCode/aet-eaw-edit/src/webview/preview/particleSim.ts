// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The particle simulation, with no rendering in it.
//
// Simulated here rather than on the server because the alternative is thousands of positions at
// sixty frames a second over a JSON-RPC channel. The server sends the emitter description once and
// this turns it into particles.
//
// Everything is deterministic given a seed. That is not for reproducibility's own sake: a simulation
// seeded from Math.random cannot be tested at all, and "the sparks go the wrong way" is otherwise
// only ever settled by squinting at it.

import type { AlamoEmitter, AlamoSpawnVolume, AlamoTrack } from '../../protocol/modelPreview';

/** Named for what it is: a value in [0, 1). */
export type Random = () => number;

/**
 * A small deterministic generator.
 *
 * mulberry32 - short, well-distributed enough for scattering particles, and seedable, which
 * Math.random is not.
 */
export function seededRandom(seed: number): Random {
    let state = seed >>> 0;

    return () => {
        state = (state + 0x6d2b79f5) >>> 0;
        let t = state;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

export interface Vec3 {
    x: number;
    y: number;
    z: number;
}

/** One live particle. */
export interface Particle {
    position: Vec3;
    velocity: Vec3;
    /** Seconds since birth. */
    age: number;
    /** Total seconds this particle will live. */
    lifetime: number;
    /**
     * The particle's own angle in TURNS, fixed at birth.
     *
     * Zero for an ordinary particle, which starts square-on and takes all its spin from the
     * integrated speed track; the emitter's random angle for a `randomRotation` one, which does not
     * spin at all.
     */
    rotation: number;
    /** Which way it turns: -1 or 1, drawn at birth when the emitter randomises it. */
    direction: number;
    /**
     * Fractional particles this one's trail child still owes.
     *
     * Per PARTICLE, because the engine gives each its own child emitter: a hundred sparks each drag
     * a full trail rather than sharing one emitter's worth between them.
     */
    childDebt: number;
    /** Randomised multiplier applied on top of the scale track. */
    scaleJitter: number;
    /** Fixed at birth, for emitters whose colour varies per particle. */
    /**
     * The per-particle colour ADDITION, fixed at birth. `randomColors`, and it brightens.
     *
     * `ColorVarianceModifierPlugin` draws `GetRandom(0, randomColors)` per channel and applies
     * `saturate(color + addition)` - alpha included, which is what a transparent sprite is read
     * through. This used to be a multiplier, `1 - random() * randomColors`, which did the opposite
     * of what the author asked for: a value meant to brighten some particles dimmed all of them.
     */
    addition: { r: number; g: number; b: number; a: number };
    /**
     * The normalised direction this particle was born in, within the emitter's own volume.
     *
     * Kept because the inward pull is measured from it and NOT from where the particle has got to -
     * the engine stores `normpos` at spawn and uses it for the whole life. It is rotated into the
     * simulation's space along with the velocity.
     */
    inward: Vec3;
}

/**
 * Samples a track at a normalised age.
 *
 * Step holds the value of the key it is at or after - a texture-index track is the reason this mode
 * exists, and interpolating between frame 3 and frame 4 would show neither.
 */
export function sampleTrack(track: AlamoTrack, t: number): number {
    const keys = track.keys;
    if (keys.length === 0) {
        return 0;
    }

    const clamped = Math.min(Math.max(t, 0), 1);

    if (clamped <= keys[0].time) {
        return keys[0].value;
    }
    if (clamped >= keys[keys.length - 1].time) {
        return keys[keys.length - 1].value;
    }

    // `<=`, not `<`: a key takes effect AT its own time. With `<`, sampling exactly on a key left the
    // previous segment selected, so a step track held the outgoing value for that instant - a texture
    // atlas would show the old frame on the frame it was meant to change. Where two keys share a
    // time, this also picks the later one, which is what a hard cut means.
    let i = 0;
    while (i < keys.length - 1 && keys[i + 1].time <= clamped) {
        i++;
    }

    const a = keys[i];
    const b = keys[i + 1];

    if (track.interpolation === 'Step') {
        return a.value;
    }

    const span = b.time - a.time;
    // Coincident keys are a step in disguise; dividing by the gap would give infinity.
    const u = span <= 0 ? 0 : (clamped - a.time) / span;

    // Smoothstep, which is what the engine's "smooth" mode is: ease in and out, same endpoints.
    const eased = track.interpolation === 'Smooth' ? u * u * (3 - 2 * u) : u;

    return a.value + (b.value - a.value) * eased;
}

/**
 * Draws a vector from a spawn volume.
 *
 * The same record describes a speed, a lifetime and a position, so this is used for all three -
 * which is why it returns a vector even for lifetime, where only x is read.
 */
export function sampleVolume(volume: AlamoSpawnVolume, random: Random): Vec3 {
    switch (volume.shape) {
        case 'Box':
            return {
                x: lerp(volume.min.x, volume.max.x, random()),
                y: lerp(volume.min.y, volume.max.y, random()),
                z: lerp(volume.min.z, volume.max.z, random()),
            };

        case 'Cube': {
            const half = volume.sideLength / 2;
            return {
                x: lerp(-half, half, random()),
                y: lerp(-half, half, random()),
                z: lerp(-half, half, random()),
            };
        }

        case 'Sphere':
            return onSphere(random,
                volume.sphereEdgeOnly ? volume.sphereRadius : volume.sphereRadius * cubeRoot(random()));

        case 'Cylinder': {
            const angle = random() * Math.PI * 2;
            // Edge-only puts everything on the wall; otherwise the area grows with the radius, so a
            // uniform radius would crowd the axis.
            const radius = volume.cylinderEdgeOnly
                ? volume.cylinderRadius
                : volume.cylinderRadius * Math.sqrt(random());

            return {
                x: Math.cos(angle) * radius,
                y: random() * volume.cylinderHeight,
                z: Math.sin(angle) * radius,
            };
        }

        default:
            // Point: one exact value, not random at all.
            return { ...volume.exactValue };
    }
}

/**
 * How far from its origin a spawn volume reaches.
 *
 * The framing needs a size for a subject that has none yet: a particle system's geometry buffers are
 * allocated full of zeros and filled in over the following seconds, so measuring the meshes gives a
 * radius of zero, which puts the camera exactly on its own target and blacks out the viewport - grid
 * included. The emitter's own numbers are the honest answer, and they are known before the first
 * particle exists.
 */
export function volumeExtent(volume: AlamoSpawnVolume): number {
    switch (volume.shape) {
        case 'Box':
            return Math.max(length(volume.min), length(volume.max));

        case 'Cube':
            // To the corner, not the face - half a side would leave the corners outside the frame.
            return (volume.sideLength / 2) * Math.sqrt(3);

        case 'Sphere':
            return volume.sphereRadius;

        case 'Cylinder':
            return Math.hypot(volume.cylinderRadius, volume.cylinderHeight);

        default:
            return length(volume.exactValue);
    }
}

/**
 * Roughly how far this emitter's particles get.
 *
 * Spawn offset plus the distance a particle covers over its life. Acceleration and gravity are left
 * out: this frames a camera, and being a little tight beats a hull lost in the middle of a huge
 * empty frame.
 */
export function emitterExtent(emitter: AlamoEmitter): number {
    const lifetimeFromVolume = volumeExtent(emitter.lifetime);
    const lifetime = lifetimeFromVolume > 0 ? lifetimeFromVolume : emitter.properties.lifetime;

    const reach = volumeExtent(emitter.position) + volumeExtent(emitter.speed) * lifetime;

    // The sprite is often the bigger half: p_explosion_huge01's smoke travels about 14 units and then
    // draws itself 224 across. Scale is a half-extent, matching the engine's own quad.
    const scale = trackFor(emitter, 'Scale');
    const sprite = scale === undefined
        ? 0
        : Math.max(0, ...scale.keys.map(key => key.value));

    const extent = reach + sprite;

    // Never zero: an emitter parked at the origin still has to be framed by something.
    return extent > 0 ? extent : 1;
}

/**
 * How large a whole system reads on screen.
 *
 * The median rather than the maximum, because one emitter routinely travels an order of magnitude
 * further than the rest and framing it shows the effect as a dot in an empty field:
 * p_explosion_huge01's debris reaches 1290 units while its smoke, fire and heat all sit inside 350.
 * Debris is a handful of specks; the smoke is the explosion. The largest sprite is then a floor, so
 * the biggest puff is never cropped however few emitters draw it.
 */
export function systemExtent(emitters: readonly AlamoEmitter[]): number {
    if (emitters.length === 0) {
        return 1;
    }

    const extents = emitters.map(emitterExtent).sort((a, b) => a - b);
    const median = extents[Math.floor((extents.length - 1) / 2)];

    const sprites = emitters.map(emitter => {
        const scale = trackFor(emitter, 'Scale');
        return scale === undefined ? 0 : Math.max(0, ...scale.keys.map(key => key.value));
    });

    return Math.max(median, ...sprites);
}

function length(v: Vec3): number {
    return Math.hypot(v.x, v.y, v.z);
}

/** A direction on the unit sphere, scaled - uniform, not the pole-clustered naive version. */
function onSphere(random: Random, radius: number): Vec3 {
    const z = random() * 2 - 1;
    const angle = random() * Math.PI * 2;
    const r = Math.sqrt(Math.max(0, 1 - z * z));

    return { x: Math.cos(angle) * r * radius, y: Math.sin(angle) * r * radius, z: z * radius };
}

function cubeRoot(v: number): number {
    return Math.cbrt(v);
}

function lerp(a: number, b: number, t: number): number {
    return a + (b - a) * t;
}

/**
 * Decides how many particles to emit in one step.
 *
 * Carries the remainder rather than rounding: at 40 particles a second and a 16ms frame it owes 0.64
 * of a particle, and rounding that to zero every frame emits nothing at all.
 */
export class SpawnClock {
    private owed = 0;
    private elapsed = 0;
    private burstsFired = 0;
    private nextBurst: number;

    constructor(private readonly emitter: AlamoEmitter) {
        this.nextBurst = emitter.properties.initialDelay;
    }

    /** How many particles this step wants, and whether the emitter has finished for good. */
    advance(dt: number): number {
        this.elapsed += dt;

        if (this.elapsed < this.emitter.properties.initialDelay) {
            return 0;
        }

        if (this.emitter.properties.useBursts) {
            let count = 0;
            const limit = this.emitter.properties.burstCount;

            while (this.elapsed >= this.nextBurst
                && (limit === 0 || this.burstsFired < limit)) {
                count += this.emitter.properties.particlesPerBurst;
                this.burstsFired++;
                this.nextBurst += Math.max(this.emitter.properties.burstDelay, 1e-4);
            }

            return count;
        }

        this.owed += this.emitter.properties.particlesPerSecond * dt;
        const whole = Math.floor(this.owed);
        this.owed -= whole;
        return whole;
    }

    /** True once a burst emitter has fired every burst it was given. */
    get finished(): boolean {
        const limit = this.emitter.properties.burstCount;
        return this.emitter.properties.useBursts && limit > 0 && this.burstsFired >= limit;
    }
}

/** Creates one particle at birth, drawing everything random about it now. */
export function spawnParticle(
    emitter: AlamoEmitter, random: Random, wind: Vec3 = { x: 0, y: 0, z: 0 },
): Particle {
    const properties = emitter.properties;

    const position = sampleVolume(emitter.position, random);
    const speed = sampleVolume(emitter.speed, random);
    const lifetimeSample = sampleVolume(emitter.lifetime, random);

    // The lifetime volume yields a vector because the record is shared; only x is a duration.
    const baseLifetime = lifetimeSample.x !== 0 ? lifetimeSample.x : properties.lifetime;
    const lifetime = Math.max(
        0.01, baseLifetime * (1 + (random() * 2 - 1) * properties.randomLifetimePercent));

    // Two different things wear the same track. With `randomRotation` the particle takes a FIXED
    // angle from the average and the speed track is not read at all; without it the particle starts
    // square-on and the track is integrated over its life. The direction flips either way.
    const rotation = properties.randomRotation
        ? properties.randomRotationAverage
            * (1 + (random() * 2 - 1) * properties.randomRotationVariance)
        : 0;

    const direction = properties.randomRotationDirection && random() < 0.5 ? -1 : 1;

    // Downwind, once, at birth: `if (affectedByWind) initialSpeed += GetWind()`. 484 of the
    // corpus's 1019 emitters set the flag, so half the drifting smoke in the game depends on it.
    const blown = properties.affectedByWind ? wind : { x: 0, y: 0, z: 0 };

    // The direction the particle was born in, which is what the inward terms pull along.
    const spread = Math.hypot(position.x, position.y, position.z);
    const inward = spread < 1e-6
        ? { x: 0, y: 0, z: 0 }
        : { x: position.x / spread, y: position.y / spread, z: position.z / spread };

    return {
        position,
        // `initialSpeed -= normpos * inwardSpeed`, with the reader having already negated the
        // stored value - so adding it here throws the particle outward exactly as the file asks.
        // Half the corpus sets this, and it was doing nothing at all.
        velocity: {
            x: speed.x + inward.x * properties.inwardSpeed + blown.x,
            y: speed.y + inward.y * properties.inwardSpeed + blown.y,
            z: speed.z + inward.z * properties.inwardSpeed + blown.z,
        },
        inward,
        age: 0,
        lifetime,
        rotation,
        direction,
        childDebt: 0,
        // Only ever SHRINKS: `GetRandom(1.0f - randomScalePerc, 1.0f)`. A symmetric jitter would
        // let sprites grow past the size the author set.
        scaleJitter: 1 - random() * properties.randomScalePercent,
        addition: colourAddition(properties, random),
    };
}

/**
 * The colour a particle is born brighter by.
 *
 * `colorAddGrayscale` makes it MONOCHROME - one draw reused on every channel, alpha included:
 * `addition.g = m_grayscale ? addition.r : GetRandom(m_min.g, m_max.g)`. Without it an emitter
 * asking for a brightness jitter got a colour jitter instead.
 */
function colourAddition(
    properties: AlamoEmitter['properties'], random: () => number,
): { r: number; g: number; b: number; a: number } {
    const max = properties.randomColors;
    const red = random() * max.x;

    if (properties.colorAddGrayscale) {
        return { r: red, g: red, b: red, a: red };
    }

    return {
        r: red,
        g: random() * max.y,
        b: random() * max.z,
        a: random() * max.w,
    };
}

/** Clamped to the unit range, as every one of the engine's colour writes is. */
function saturate(value: number): number {
    return Math.min(1, Math.max(0, value));
}

/**
 * Advances one particle.
 *
 * Returns false when it has died, so the caller can retire it.
 */
/**
 * The acceleration an emitter applies, in the space the simulation runs in.
 *
 * The file's vector is in ALAMO axes, where Z is up; the simulation runs in the model's glTF space,
 * where Y is. Gravity needs no such turn - it is a scalar down the same axis in both, which is why
 * it was right while a declared acceleration pushed sideways.
 *
 * Not used for the 31 emitters that set `objectSpaceAcceleration`: theirs is a vector in the
 * EMITTER's own frame, and the caller rotates it by that instead.
 */
export function accelerationIn(properties: AlamoEmitter['properties']): Vec3 {
    return {
        x: properties.acceleration.x,
        y: properties.acceleration.z - properties.gravity,
        z: -properties.acceleration.y,
    };
}

export function stepParticle(
    particle: Particle, emitter: AlamoEmitter, dt: number, groundY = 0,
    acceleration: Vec3 = accelerationIn(emitter.properties),
): boolean {
    const properties = emitter.properties;

    particle.age += dt;
    if (particle.age >= particle.lifetime) {
        return false;
    }

    particle.velocity.x += acceleration.x * dt;
    particle.velocity.y += acceleration.y * dt;
    particle.velocity.z += acceleration.z * dt;

    // Pulls along the direction the particle was BORN in - `normpos`, fixed at spawn. Stored
    // positive and negated by the reader, so adding it here moves the way the file asks.
    if (properties.inwardAcceleration !== 0) {
        const scale = properties.inwardAcceleration * dt;
        particle.velocity.x += particle.inward.x * scale;
        particle.velocity.y += particle.inward.y * scale;
        particle.velocity.z += particle.inward.z * scale;
    }

    particle.position.x += particle.velocity.x * dt;
    particle.position.y += particle.velocity.y * dt;
    particle.position.z += particle.velocity.z * dt;

    // `groundY`, not zero. Particle positions are relative to the system's root, which hangs off a
    // bone, so a bare `y < 0` puts the ground at whatever height that bone sits at - the dirt of an
    // explosion mounted up a hull vanished in mid-air the moment it was thrown.
    if (properties.groundBehavior !== 'None' && particle.position.y < groundY) {
        applyGround(particle, properties.groundBehavior, properties.bounciness, groundY);
    }

    return true;
}

function applyGround(
    particle: Particle, behavior: string, bounciness: number, groundY: number,
): void {
    if (behavior === 'Disappear') {
        // Aged out rather than removed here, so one rule retires particles.
        particle.age = particle.lifetime;
        return;
    }

    particle.position.y = groundY;

    if (behavior === 'Bounce') {
        particle.velocity.y = Math.abs(particle.velocity.y) * bounciness;
        return;
    }

    // Stick.
    particle.velocity.x = 0;
    particle.velocity.y = 0;
    particle.velocity.z = 0;
}

/** The particle's appearance right now. */
export interface ParticleAppearance {
    r: number;
    g: number;
    b: number;
    a: number;
    size: number;
    rotation: number;
    /** Frame index into the texture atlas. */
    frame: number;
}

/** The scale track gives a WIDTH; the renderer wants the half-extent it is drawn at. */
const HALF = 0.5;

/**
 * The area under a track from the start of life to `t`, in value-times-lifetime units.
 *
 * The rotation track is a SPEED, so the angle at any moment is its integral - not a sample of it.
 * Each interpolation mode has its own closed form (`EmitterInstance.cpp:IntegrateTrack`); doing it
 * numerically instead would drift differently at every frame rate.
 */
export function integrateTrack(track: AlamoTrack, t: number): number {
    const keys = track.keys;
    if (keys.length === 0) {
        return 0;
    }

    const until = Math.min(Math.max(t, 0), 1);

    // Before the first key the curve holds that key's value, as sampling does.
    let total = keys[0].value * Math.min(until, keys[0].time);

    for (let i = 0; i + 1 < keys.length && keys[i].time < until; i++) {
        const [from, to] = [keys[i], keys[i + 1]];
        const span = to.time - from.time;

        if (span <= 0) {
            continue;
        }

        // How far into this segment the particle has got, as a fraction of it.
        const u = Math.min(1, (until - from.time) / span);

        const area = track.interpolation === 'Step'
            ? from.value * u
            : track.interpolation === 'Smooth'
                // The integral of the cubic ease, which is what Smooth interpolates with.
                ? (from.value - to.value) * u ** 4 / 2 + (to.value - from.value) * u ** 3
                    + from.value * u
                : u * (from.value + u * (to.value - from.value) / 2);

        total += area * span;
    }

    // And after the last key it holds again.
    const last = keys[keys.length - 1];
    total += last.value * Math.max(0, until - last.time);

    return total;
}

export function appearanceOf(particle: Particle, emitter: AlamoEmitter): ParticleAppearance {
    const t = particle.lifetime <= 0 ? 1 : particle.age / particle.lifetime;

    const channel = (name: string, fallback: number): number => {
        const track = trackFor(emitter, name);
        return track === undefined ? fallback : sampleTrack(track, t);
    };

    return {
        r: saturate(channel('Red', 1) + particle.addition.r),
        g: saturate(channel('Green', 1) + particle.addition.g),
        b: saturate(channel('Blue', 1) + particle.addition.b),
        a: saturate(channel('Alpha', 1) + particle.addition.a),
        // HALF the scale track. Both references agree and neither says so out loud: the editor
        // builds its quad at `baseScale * scaleSample / 2` (`EmitterInstance.cpp:538`), and
        // alo-viewer turns the same track into a size plugin at `value / 2`
        // (`ParticleSystem.cpp:734`) before drawing at +/-size. Taking the raw value made every
        // sprite twice as wide and four times the area, which on an additive effect - a jetpack,
        // a flamethrower - reads as far too intense.
        size: channel('Scale', 1) * HALF * particle.scaleJitter,
        // A fixed angle for a random-rotation particle, otherwise the integral of the speed track
        // over the life so far. Either way the direction drawn at birth decides which way round.
        rotation: particle.direction * (particle.rotation + spin(particle, emitter, t)),
        frame: Math.max(0, Math.floor(channel('TextureIndex', 0))),
    };
}

/** How far a particle has turned by `t`, in turns. Zero for the emitters that do not spin. */
function spin(particle: Particle, emitter: AlamoEmitter, t: number): number {
    const track = trackFor(emitter, 'RotationSpeed');

    if (track === undefined || emitter.properties.randomRotation) {
        return 0;
    }

    // The track is turns per SECOND and the integral is over the fraction of a life, so the
    // lifetime is what converts one into the other.
    return integrateTrack(track, t) * particle.lifetime;
}

function trackFor(emitter: AlamoEmitter, channel: string): AlamoTrack | undefined {
    return emitter.tracks.find(track => track.channel === channel);
}

/**
 * How many frames across the sheet is.
 *
 * `textureSize` is the frame COUNT, despite the name - the engine squares it to get the grid:
 * `int texsize = (int)ceil(sqrtf((float)e.textureSize))` (alo-viewer `ParticleSystem.cpp:754`).
 * Reading it as a pixel edge and dividing the sheet by it happens to agree for the common 64, which
 * is why it survived: p_particle_master is 512 wide with textureSize 64, and 512/64 is also 8. It
 * falls apart everywhere else - p_explosion_huge01's fire declares 16, which is a 4x4 grid, not the
 * 32x32 that 512/16 gives, so every frame sampled a sliver of the wrong cell.
 */
export function atlasColumns(textureSize: number): number {
    return Math.max(1, Math.ceil(Math.sqrt(Math.max(1, textureSize))));
}

/**
 * Where a frame sits in the atlas.
 *
 * Rows run top-down from frame 0, which is how the engine indexes them and how a DDS stores its rows.
 */
export function atlasFrame(
    frame: number, textureSize: number,
): { u: number; v: number; size: number } {
    const columns = atlasColumns(textureSize);
    const size = 1 / columns;
    const index = ((frame % (columns * columns)) + columns * columns) % (columns * columns);

    return {
        u: (index % columns) * size,
        v: Math.floor(index / columns) * size,
        size,
    };
}
