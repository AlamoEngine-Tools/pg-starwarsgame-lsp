// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Turning a `UniformSource` into an actual value, once per frame.
//
// Kept free of three.js so the whole mapping is testable without a GPU: the viewport hands in a
// plain record of what it already knows - matrices it computed with three's own maths, the light
// rig, the clock - and this says which part of it each semantic wants.

import type { Wind } from '../viewerSettings';
import type { AlamoMatrix, UniformSource } from './semantics';

/** One directional light, as the engine's uniforms describe it. */
export interface FrameLight {
    /** Direction TO the light, in world space. */
    vector: [number, number, number];
    /** The same direction in the object's space. */
    objectVector: [number, number, number];
    diffuse: [number, number, number, number];
    specular: [number, number, number, number];
}

/** Everything the viewport knows at the moment it draws one mesh. */
export interface AlamoFrame {
    /**
     * All nine matrices, each as 16 numbers in the order GLSL wants (column-major).
     *
     * Supplied rather than derived here: three's `Matrix4` already multiplies and inverts correctly,
     * and re-implementing that would be a second, worse copy of it.
     */
    matrices: Record<AlamoMatrix, readonly number[]>;
    eyeWorld: [number, number, number];
    eyeObject: [number, number, number];
    lights: FrameLight[];
    ambient: [number, number, number, number];
    lightScale: [number, number, number, number];
    /** Seconds since the preview opened. */
    time: number;
    resolution: [number, number, number, number];
    /** The ambient probe the effects read, as three 4x4 matrices flattened to 48 numbers. */
    sphericalHarmonics: readonly number[];
    /** The bone palette for this mesh, or null when it is not skinned. */
    skinMatrices: ArrayLike<number> | null;
    /** The weather the reader set. One wind, read by the foliage shaders and by the particles. */
    wind: Wind;
}

/** How high a tree is when it leans over by its full bend. The engine pins this at 0.002. */
const BEND_HEIGHT_FACTOR = 0.002;

/**
 * The wind, as the two shaders that read it want it.
 *
 * `Tree.fx` bends by `BendScale * m_bendVector.xyz * (Pos.z * Pos.z * m_bendVector.w)` and 174 of
 * the corpus's sub-meshes use it, so supplying nothing leaves every tree either dead still or -
 * worse - leaning permanently at the header's own constant `{1, 0, 0, 0.01}`. The oscillation is
 * the engine's own: `(cos(h) * sin(t), sin(h) * sin(t), 0, 0.002)`, `alo-viewer Render.cpp:300`.
 *
 * Written in the space the shaders work in: they add the vector to a WORLD position, and ours is
 * the Y-up one the exporter's root correction produces, so Alamo's ground plane XY is XZ here.
 */
function windVector(of: 'bend' | 'grass', time: number, wind: Wind): number[] {
    const heading = (wind.heading * Math.PI) / 180;

    // The bend SWAYS and the grass does not: the tree shader is handed an oscillation
    // (`alo-viewer Render.cpp:300` multiplies by `sin(time)`), while the grass shader reads a
    // steady vector with the wind's length in `w` and does its own thing with it.
    const sway = of === 'bend' ? Math.sin(time) : 1;

    // `|| 0` collapses negative zero, which a heading on an axis produces and which reads as a
    // different value to anything comparing the vector.
    const x = Math.cos(heading) * sway || 0;
    const z = -Math.sin(heading) * sway || 0;

    return of === 'bend'
        ? [x, 0, z, BEND_HEIGHT_FACTOR]
        : [x, 0, z, wind.speed];
}

/**
 * A spherical-harmonic probe that answers the same ambient for every normal.
 *
 * The effects read their ambient through `dot(n4, n4 * m_sph[c])` with `n4 = (normal, 1)`. Setting
 * nothing but the matrix's last element collapses that to the element itself, whatever the normal -
 * so this is a flat grey environment expressed in the form the shaders expect.
 *
 * It replaces the probe shipped in the header, which is a PLACEHOLDER environment rather than a
 * neutral one: its constant terms are R 0.7379, G 0.4108, B 0.5165, and leaving it in gave every
 * translated model a mauve cast. The engine supplies the real scene's harmonics; a model viewer has
 * no scene to supply, so it supplies nothing rather than someone else's sky.
 */
export function neutralHarmonics(ambient: readonly [number, number, number]): number[] {
    const probe: number[] = [];

    for (const channel of ambient) {
        const matrix = new Array<number>(16).fill(0);
        matrix[15] = channel;
        probe.push(...matrix);
    }

    return probe;
}

/**
 * One light of the rig, in the terms the engine's own shaders read.
 *
 * The values here are the engine's, NOT three's. three's `DirectionalLight` carries the same rig
 * multiplied by PI, because `MeshStandardMaterial` applies a Lambert BRDF of `albedo / PI` that
 * Alamo's fixed-function shading does not - so reading the intensity back off the three light and
 * handing it to a TRANSLATED shader lights it 3.14 times too hot, and every surface facing the sun
 * blows out to flat white. The rig is the source of truth for both; only three needs the factor.
 */
export function engineLight(
    light: { colour: readonly [number, number, number]; intensity: number },
    specular: readonly [number, number, number],
    vector: [number, number, number],
    objectVector: [number, number, number],
): FrameLight {
    const [r, g, b] = light.colour;
    const scale = light.intensity;

    return {
        vector,
        objectVector,

        // `Light0Diffuse = colour * colour.a` in the engine's own RenderEngine.cpp, which is what
        // the rig's colour and intensity are: a white light at 0.5 is a mid grey.
        diffuse: [r * scale, g * scale, b * scale, 1],

        // ONE global specular for the whole rig, as the engine keeps it and as AloViewer's settings
        // dialog exposes it - but scaled by this light, so turning a light down stops it
        // highlighting at full strength.
        specular: [specular[0] * scale, specular[1] * scale, specular[2] * scale, 0],
    };
}

/**
 * The value for one semantic, or `undefined` when this frame carries nothing for it.
 *
 * Undefined is the useful answer: the caller leaves the uniform at the default the shader's own
 * header declared, which is a real value chosen by the people who wrote the effect.
 */
export type UniformValue = number | ArrayLike<number> | undefined;

/**
 * What to put in the uniform this frame.
 *
 * The sources with no counterpart in a model preview - fog, distance fade, the wind vectors, the
 * fog-of-war projection - deliberately return undefined rather than a made-up number. There is no
 * fog in a model viewer and no wind, and the header's own defaults are better than a guess.
 */
export function uniformValue(source: UniformSource, frame: AlamoFrame): UniformValue {
    switch (source.kind) {
        case 'matrix':
            return frame.matrices[source.of];

        case 'eye':
            return source.space === 'world' ? frame.eyeWorld : frame.eyeObject;

        case 'light': {
            const light = frame.lights[source.index];
            if (light === undefined) {
                return undefined;
            }

            switch (source.channel) {
                case 'vector':
                    return source.space === 'world' ? light.vector : light.objectVector;
                case 'diffuse':
                    return light.diffuse;
                case 'specular':
                    return light.specular;
            }

            return undefined;
        }

        case 'ambient':
            return frame.ambient;

        case 'lightScale':
            return frame.lightScale;

        case 'time':
            return frame.time;

        case 'resolution':
            return frame.resolution;

        case 'skinMatrices':
            return frame.skinMatrices ?? undefined;

        case 'sphericalHarmonics':
            return frame.sphericalHarmonics;

        // Everything below is engine state a model preview has none of, and whose shipped default
        // is a sensible answer - fog and fade switch themselves off, the wind sits still.
        case 'wind':
            return windVector(source.of, frame.time, frame.wind);

        case 'fog':
        case 'distanceFade':
        case 'shadowExtrusion':
        case 'texture':
        case 'projectedTexcoord':
            return undefined;
    }

    return undefined;
}
