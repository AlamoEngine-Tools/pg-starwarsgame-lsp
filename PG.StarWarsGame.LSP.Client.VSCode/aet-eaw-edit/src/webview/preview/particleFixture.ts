// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Emitters to test against, filled in with the ENGINE's own defaults.
//
// Test-only, and shared rather than copied: an emitter is forty-odd fields, and a copy that drifts
// from the real defaults tests a system nobody ships. `OldEmitter::SetDefaults` is the authority -
// the file omits any property left at its default, so a fixture that zero-initialises instead gives
// every emitter a one-second lifetime of zero and draws nothing.

import type {
    AlamoEmitter, AlamoEmitterProperties, AlamoParticleContent, AlamoSpawnVolume, AlamoTrack,
} from '../../protocol/modelPreview';

export const ZERO = { x: 0, y: 0, z: 0 };

export function volume(over: Partial<AlamoSpawnVolume> = {}): AlamoSpawnVolume {
    return {
        shape: 'Point',
        min: { ...ZERO },
        max: { ...ZERO },
        sideLength: 0,
        sphereRadius: 0,
        sphereEdgeOnly: false,
        cylinderRadius: 0,
        cylinderEdgeOnly: false,
        cylinderHeight: 0,
        exactValue: { ...ZERO },
        ...over,
    };
}

/** The engine's defaults, which the server always fills in. */
export function properties(over: Partial<AlamoEmitterProperties> = {}): AlamoEmitterProperties {
    return {
        blendMode: 'Additive',
        triangleCount: 2,
        useBursts: false,
        linkToSystem: false,
        inwardSpeed: 0,
        acceleration: { ...ZERO },
        inwardAcceleration: 0,
        gravity: 0,
        lifetime: 1,
        textureSize: 64,
        randomScalePercent: 0,
        randomLifetimePercent: 0,
        randomRotationVariance: 0,
        randomRotationDirection: false,
        initialDelay: 0,
        burstDelay: 1,
        particlesPerBurst: 1,
        burstCount: 0,
        parentLinkStrength: 0,
        particlesPerSecond: 1,
        randomColors: { x: 0, y: 0, z: 0, w: 0 },
        colorAddGrayscale: false,
        worldOriented: false,
        groundBehavior: 'None',
        bounciness: 0.2,
        affectedByWind: false,
        freezeTime: 0,
        skipTime: 0,
        emitFromMesh: 'Disabled',
        objectSpaceAcceleration: false,
        isHeatParticle: false,
        emitFromMeshOffset: 0.5,
        isWeatherParticle: false,
        weatherCubeSize: 500,
        weatherFadeoutDistance: 100,
        hasTail: false,
        tailSize: 50,
        noDepthTest: false,
        weatherCubeDistance: 0,
        randomRotation: false,
        randomRotationAverage: 0,
        ...over,
    };
}

export function track(channel: AlamoTrack['channel'], keys: [number, number][],
    interpolation: AlamoTrack['interpolation'] = 'Linear'): AlamoTrack {
    return { channel, interpolation, keys: keys.map(([time, value]) => ({ time, value })) };
}

export function emitter(over: Partial<AlamoEmitter> = {}): AlamoEmitter {
    return {
        name: 'test',
        colorTexture: 'p_particle_master.tga',
        normalTexture: null,
        speed: volume(),
        lifetime: volume(),
        position: volume(),
        tracks: [],
        spawnOnDeath: -1,
        spawnDuringLife: -1,
        properties: properties(),
        ...over,
    };
}

/** A system wrapping the given emitters, for the renderer's sake. */
export function testSystem(emitters: AlamoEmitter[], name = 'test'): AlamoParticleContent {
    return { name, leaveParticles: false, emitters };
}
