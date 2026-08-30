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
    /**
     * The whole rig as a light probe, three 4x4 matrices flattened to 48 numbers.
     *
     * `m_sphAll` to the shaders. This is not an ambient term with the lights added on top - for
     * every mesh effect it IS the diffuse lighting, all of it. `MeshAlpha.fx:53` has no N.L
     * anywhere in it.
     */
    sphericalHarmonics: readonly number[];
    /** The same, over the two fills alone. `m_sphFill`, for surfaces kept out of the sun. */
    sphericalHarmonicsFill: readonly number[];
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

/** One directional light of the rig, in the terms the probe projects it from. */
export interface ProbeLight {
    /** Unit direction TO the light, in the scene's world space. */
    toLight: readonly [number, number, number];
    colour: readonly [number, number, number];
    intensity: number;
}

/**
 * The irradiance constants of Ramamoorthi and Hanrahan, "An Efficient Representation for Irradiance
 * Environment Maps" - which is the paper the engine's own `SphericalHarmonics.cpp` cites.
 */
const SH_C1 = 0.429043;
const SH_C2 = 0.511664;
const SH_C3 = 0.743125;
const SH_C4 = 0.886227;
const SH_C5 = 0.247708;

/**
 * The nine real spherical harmonics at a direction, in the paper's order and its sign convention.
 *
 * Deliberately NOT the Direct3D convention, which is what `SphericalHarmonics.cpp` calls through
 * `D3DXSHEvalDirection`. The two differ by the Condon-Shortley phase: D3DX negates the four odd-m
 * functions, the paper does not, and the matrix composed below is the paper's. Feeding D3DX's
 * coefficients into the paper's matrix rotates the whole probe 180 degrees in azimuth - and that is
 * exactly what the engine's own `m_direction * Vector3(1,1,-1)` puts back, under a comment about
 * handedness. Two cancelling sign errors land on the right answer; one of them written down is
 * easier to keep right, so this evaluates the paper's basis at the direction TO the light and is
 * numerically identical to the engine for every rig.
 */
function shBasis(x: number, y: number, z: number): number[] {
    return [
        0.282095,
        0.488603 * y,
        0.488603 * z,
        0.488603 * x,
        1.092548 * x * y,
        1.092548 * y * z,
        0.315392 * (3 * z * z - 1),
        1.092548 * x * z,
        0.546274 * (x * x - y * y),
    ];
}

/**
 * The rig as a light probe, in the form the engine's shaders read.
 *
 * A port of `SphericalHarmonics::Calculate_Matrices`: project each light onto nine coefficients per
 * channel, compose them into the paper's three irradiance matrices, and add the ambient to the
 * constant term. The shaders then read it with `dot(n4, mul(m_sph[c], n4))`, `n4 = (normal, 1)`.
 *
 * What this replaces answered the same flat ambient for every normal - only the matrix's last
 * element was set, which collapses that dot product to the element itself. That was a stand-in for
 * the header's shipped probe, a PLACEHOLDER sky with constant terms R 0.7379 G 0.4108 B 0.5165 that
 * gave every translated model a mauve cast. Neutral was the right instinct and the wrong answer:
 * these shaders have no other diffuse term, so a flat probe renders the entire corpus at the
 * ambient's own value - 0.1 grey on the engine's default rig - with no shading in it at all. Six
 * times too dark on the lit side, and the reader saw it immediately.
 *
 * Nine terms is a band-limited cosine lobe, so the probe overshoots a delta light by about six per
 * cent and does not quite reach zero behind it. Both are the approximation the engine ships, not
 * something to correct.
 */
export function sphericalHarmonics(
    lights: readonly ProbeLight[],
    ambient: readonly [number, number, number],
): number[] {
    const projection = [new Array<number>(9).fill(0), new Array<number>(9).fill(0),
        new Array<number>(9).fill(0)];

    for (const light of lights) {
        const [x, y, z] = light.toLight;
        const length = Math.sqrt(x * x + y * y + z * z);

        // A light with no direction is not a dark light, it is an undefined one - projecting it
        // would spread NaN through all 48 numbers and black the whole scene out.
        if (!(length > 0)) {
            continue;
        }

        const basis = shBasis(x / length, y / length, z / length);

        for (let channel = 0; channel < 3; channel++) {
            // `colour * colour.a` in the engine's own terms: the rig's colour times its brightness.
            const weight = light.colour[channel] * light.intensity;

            for (let term = 0; term < 9; term++) {
                projection[channel][term] += weight * basis[term];
            }
        }
    }

    const probe: number[] = [];

    for (let channel = 0; channel < 3; channel++) {
        const l = projection[channel];

        // Symmetric, every time - so the row-major matrix written here and the column-major one
        // GLSL uploads are the same sixteen numbers, and `mul(m, n4)` cannot read it the wrong way.
        probe.push(
            SH_C1 * l[8], SH_C1 * l[4], SH_C1 * l[7], SH_C2 * l[3],
            SH_C1 * l[4], -SH_C1 * l[8], SH_C1 * l[5], SH_C2 * l[1],
            SH_C1 * l[7], SH_C1 * l[5], SH_C3 * l[6], SH_C2 * l[2],
            SH_C2 * l[3], SH_C2 * l[1], SH_C2 * l[2],
            SH_C4 * l[0] - SH_C5 * l[6] + ambient[channel]);
    }

    return probe;
}

/**
 * What a shader would read off the probe for one normal.
 *
 * The vertex shaders' own expression, `dot(n4, mul(m_sph[c], n4))`, so a test can ask what the
 * picture will be rather than what the coefficients are.
 */
export function probeDiffuse(
    probe: readonly number[],
    normal: readonly [number, number, number],
): [number, number, number] {
    const n = [normal[0], normal[1], normal[2], 1];

    return [0, 1, 2].map(channel => {
        let total = 0;

        for (let row = 0; row < 4; row++) {
            for (let col = 0; col < 4; col++) {
                total += n[row] * probe[channel * 16 + row * 4 + col] * n[col];
            }
        }

        return total;
    }) as [number, number, number];
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
            return source.set === 'fill' ? frame.sphericalHarmonicsFill : frame.sphericalHarmonics;

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
