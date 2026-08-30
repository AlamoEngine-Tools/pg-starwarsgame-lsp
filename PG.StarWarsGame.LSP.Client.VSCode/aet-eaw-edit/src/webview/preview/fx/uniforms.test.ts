// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { uniformSourceFor, type AlamoMatrix } from './semantics';
import {
    engineLight, probeDiffuse, sphericalHarmonics, uniformValue,
    type AlamoFrame, type ProbeLight,
} from './uniforms';

const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

const matrices = Object.fromEntries(([
    'world', 'worldInverse', 'worldView', 'worldViewInverse', 'worldViewProjection',
    'view', 'viewInverse', 'viewProjection', 'projection',
] as AlamoMatrix[]).map((name, index) => [name, identity.map(v => v * (index + 1))])) as
    Record<AlamoMatrix, number[]>;

const FRAME: AlamoFrame = {
    matrices,
    wind: { heading: 90, speed: 1 },
    eyeWorld: [1, 2, 3],
    eyeObject: [4, 5, 6],
    lights: [
        {
            vector: [0.7, 0, 0.7], objectVector: [0, 0.7, 0.7],
            diffuse: [1, 1, 1, 1], specular: [1, 1, 1, 0],
        },
        {
            vector: [-0.7, 0, -0.7], objectVector: [0, -0.7, -0.7],
            diffuse: [0.1, 0.1, 0.1, 1], specular: [0, 0, 0, 0],
        },
    ],
    ambient: [0.2, 0.2, 0.2, 1],
    lightScale: [1, 1, 1, 1],
    time: 12.5,
    resolution: [800, 600, 0, 0],
    sphericalHarmonics: sphericalHarmonics([], [0.2, 0.2, 0.2]),
    sphericalHarmonicsFill: sphericalHarmonics([], [0.05, 0.05, 0.05]),
    skinMatrices: [1, 2, 3],
};

const valueFor = (semantic: string) => {
    const source = uniformSourceFor(semantic);
    assert.notEqual(source, null, `no source for ${semantic}`);

    return uniformValue(source!, FRAME);
};

describe('uniformValue', () => {
    it('picks out the matrix the semantic names', () => {
        assert.deepEqual(valueFor('WORLD'), matrices.world);
        assert.deepEqual(valueFor('WORLDVIEWPROJECTION'), matrices.worldViewProjection);
        assert.notDeepEqual(valueFor('VIEW'), matrices.world);
    });

    it('keeps the two eye positions apart', () => {
        assert.deepEqual(valueFor('EYE_POSITION'), [1, 2, 3]);
        assert.deepEqual(valueFor('EYE_OBJ_POSITION'), [4, 5, 6]);
    });

    it('reads the right light and the right channel of it', () => {
        assert.deepEqual(valueFor('DIR_LIGHT_VEC_0'), [0.7, 0, 0.7]);
        assert.deepEqual(valueFor('DIR_LIGHT_OBJ_VEC_0'), [0, 0.7, 0.7]);
        assert.deepEqual(valueFor('DIR_LIGHT_DIFFUSE_1'), [0.1, 0.1, 0.1, 1]);
        assert.deepEqual(valueFor('DIR_LIGHT_SPECULAR_0'), [1, 1, 1, 0]);
    });

    it('has nothing for a light the rig does not have', () => {
        // The engine declares three; supplying a made-up third would light the model from nowhere.
        assert.equal(valueFor('DIR_LIGHT_DIFFUSE_2'), undefined);
    });

    it('supplies the clock and the viewport size', () => {
        assert.equal(valueFor('TIME'), 12.5);
        assert.deepEqual(valueFor('RESOLUTION_CONSTANTS'), [800, 600, 0, 0]);
    });

    it('supplies the bone palette only when the mesh is skinned', () => {
        assert.deepEqual(valueFor('SKINMATRIXARRAY'), [1, 2, 3]);
        assert.equal(
            uniformValue({ kind: 'skinMatrices' }, { ...FRAME, skinMatrices: null }),
            undefined);
    });

    it('leaves the engine-only state at whatever the shader`s own header declared', () => {
        // There is no fog in a model viewer and no wind. `undefined` means "do not overwrite the
        // default", which for fog is the header's own switch-it-off value.
        // NOT the light probes: the header's is a placeholder environment with a mauve bias, and
        // that one IS replaced. See the probe tests below.
        for (const semantic of ['FOG_VALS', 'DISTANCE_FADE_VALS',
            'SHADOW_EXTRUSION_DISTANCE', 'FOW_TEX_U']) {
            assert.equal(valueFor(semantic), undefined, semantic);
        }
    });
});

describe('the wind', () => {
    /**
     * `Tree.fx` bends by `BendScale * m_bendVector.xyz * (Pos.z * Pos.z * m_bendVector.w)`, so with
     * nothing supplied the foliage of 174 sub-meshes stands dead still - or worse, leans over at
     * the header's own constant `{1, 0, 0, 0.01}`. The engine oscillates it:
     * `(cos(h) * sin(t), sin(h) * sin(t), 0, 0.002)` where `h` is the wind heading less a quarter
     * turn (`alo-viewer Render.cpp:300`).
     */
    it('sways the bend vector rather than leaving trees leaning', () => {
        const still = uniformValue({ kind: 'wind', of: 'bend' }, { ...FRAME, time: 0 });
        const blown = uniformValue({ kind: 'wind', of: 'bend' }, { ...FRAME, time: Math.PI / 2 });

        assert.ok(Array.isArray(still) && Array.isArray(blown));
        assert.deepEqual(still.slice(0, 3), [0, 0, 0], 'sin(0) is no bend at all');
        assert.ok(Math.hypot(...blown.slice(0, 3)) > 0.9, `${blown}`);
    });

    /** The fourth component is the height normalisation, and the engine pins it at 0.002. */
    it('keeps the height factor the engine uses, not the header default', () => {
        const bend = uniformValue({ kind: 'wind', of: 'bend' }, FRAME);

        assert.ok(Array.isArray(bend));
        assert.equal(bend[3], 0.002);
    });

    /** Alamo's wind blows across its XY ground plane, which is XZ once the model is Y-up. */
    it('blows along the ground rather than into the sky', () => {
        const bend = uniformValue({ kind: 'wind', of: 'bend' }, { ...FRAME, time: Math.PI / 2 });

        assert.ok(Array.isArray(bend));
        assert.equal(bend[1], 0);
    });

    /** Grass reads its own vector, with the speed in `w`. */
    it('supplies the grass shader its own wind', () => {
        const grass = uniformValue({ kind: 'wind', of: 'grass' }, { ...FRAME, time: Math.PI / 2 });

        assert.ok(Array.isArray(grass) && grass.length === 4, `${grass}`);
        assert.ok(grass[3] > 0, 'the length is stuffed in w');
    });

    /** The heading is the reader's now, not a constant - AloViewer exposes it and so do we. */
    it('blows the way the reader set it', () => {
        const east = uniformValue(
            { kind: 'wind', of: 'bend' },
            { ...FRAME, time: Math.PI / 2, wind: { heading: 0, speed: 1 } });
        const north = uniformValue(
            { kind: 'wind', of: 'bend' },
            { ...FRAME, time: Math.PI / 2, wind: { heading: 90, speed: 1 } });

        assert.ok(Array.isArray(east) && Array.isArray(north));
        assert.ok(Math.abs(east[0] - 1) < 1e-6, `${east}`);
        assert.ok(Math.abs(north[2] - -1) < 1e-6, `${north}`);
    });

    it('puts the wind speed in the grass vector`s w', () => {
        const strong = uniformValue(
            { kind: 'wind', of: 'grass' }, { ...FRAME, wind: { heading: 0, speed: 3 } });

        assert.ok(Array.isArray(strong));
        assert.equal(strong[3], 3);
    });
});

describe('the spherical-harmonic probe', () => {
    // The effects take their diffuse lighting from an SH probe, and the value shipped in the
    // header is a PLACEHOLDER environment: its constant terms are R 0.7379, G 0.4108, B 0.5165.
    // Leaving it in gave every translated model a mauve cast. The viewport supplies the rig's own
    // probe over the top, which is what `sphericalHarmonics` builds.
    it('supplies both sets rather than leaving the header placeholder', () => {
        for (const semantic of ['SPH_LIGHT_ALL', 'SPH_LIGHT_FILL']) {
            const value = valueFor(semantic);

            assert.notEqual(value, undefined, semantic);
            assert.equal((value as number[]).length, 48, semantic);
        }
    });
});

describe('engineLight', () => {
    const white = { colour: [1, 1, 1] as const, intensity: 0.5 };
    const vector: [number, number, number] = [0, 0.7, 0.7];

    it('gives the shader the engine value, not the one three needs', () => {
        // The engine reads Light0Diffuse as colour * alpha - a white light at 0.5 is a mid grey.
        // three's lights carry the same rig multiplied by PI, because MeshStandardMaterial applies
        // a Lambert BRDF of albedo/PI that Alamo's fixed-function shading does not. Reading the
        // three light back for the TRANSLATED shaders lit Game mode 3.14 times too hot, which blew
        // every surface facing the sun to flat white.
        const light = engineLight(white, [1, 1, 1], vector, vector);

        assert.deepEqual(light.diffuse, [0.5, 0.5, 0.5, 1]);
    });

    it('keeps a light its own colour', () => {
        const fill = { colour: [0.25, 0.25, 0.5] as const, intensity: 0.5 };

        assert.deepEqual(engineLight(fill, [1, 1, 1], vector, vector).diffuse,
            [0.125, 0.125, 0.25, 1]);
    });

    it('dims the highlight with the light that casts it', () => {
        // One global specular for the whole rig, as the engine keeps it, but a light turned down
        // should not go on highlighting at full strength.
        assert.deepEqual(engineLight(white, [1, 0.5, 0], vector, vector).specular,
            [0.5, 0.25, 0, 0]);
    });
});

const lit = (
    toLight: [number, number, number],
    colour: [number, number, number] = [1, 1, 1],
    intensity = 1,
): ProbeLight => ({ toLight, colour, intensity });

const UP: [number, number, number] = [0, 1, 0];
const DOWN: [number, number, number] = [0, -1, 0];
const SIDE: [number, number, number] = [1, 0, 0];

describe('sphericalHarmonics', () => {
    it('answers the ambient for every normal when nothing is lit', () => {
        // Per channel, because the engine's ambient is a colour and a tinted one is the difference
        // between a cool shadowed side and a grey one.
        const probe = sphericalHarmonics([], [0.2, 0.1, 0.05]);

        for (const normal of [UP, DOWN, SIDE]) {
            assert.deepEqual(probeDiffuse(probe, normal), [0.2, 0.1, 0.05]);
        }
    });

    it('is symmetric, so which way it is flattened cannot matter', () => {
        // The uniform is uploaded as a mat4 and the shader reads it with `mul(m, n4)`, which is
        // row-major in HLSL and column-major in GLSL. That question only has teeth for an
        // asymmetric matrix - this one never is, and the test says so rather than a comment.
        const probe = sphericalHarmonics([lit([0.3, 0.8, -0.5])], [0.1, 0.1, 0.1]);

        for (let channel = 0; channel < 3; channel++) {
            const at = (row: number, col: number) => probe[channel * 16 + row * 4 + col];

            for (let row = 0; row < 4; row++) {
                for (let col = row + 1; col < 4; col++) {
                    assert.equal(at(row, col), at(col, row), `element ${row},${col}`);
                }
            }
        }
    });

    it('is brightest on the face turned towards the light', () => {
        const probe = sphericalHarmonics([lit(UP)], [0, 0, 0]);

        const facing = probeDiffuse(probe, UP)[0];
        const away = probeDiffuse(probe, DOWN)[0];
        const edge = probeDiffuse(probe, SIDE)[0];

        assert.ok(facing > edge, `facing ${facing} should beat edge ${edge}`);
        assert.ok(edge > away, `edge ${edge} should beat away ${away}`);
    });

    it('reaches about the light own strength where the normal points at it', () => {
        // A nine-term probe is a band-limited cosine lobe, so it overshoots the delta light it
        // stands for by a few percent and never quite reaches zero behind it. Both are the
        // approximation, not a mistake - but the peak has to land ON the light's value, because
        // that is the whole scale of the picture.
        const probe = sphericalHarmonics([lit(UP, [1, 1, 1], 0.5)], [0, 0, 0]);
        const peak = probeDiffuse(probe, UP)[0];

        assert.ok(Math.abs(peak - 0.5) < 0.05, `peak was ${peak}`);
    });

    it('keeps the channels apart', () => {
        const probe = sphericalHarmonics([lit(UP, [1, 0, 0])], [0, 0, 0]);
        const [r, g, b] = probeDiffuse(probe, UP);

        assert.ok(r > 0.9, `red was ${r}`);
        assert.equal(g, 0);
        assert.equal(b, 0);
    });

    it('scales with the light intensity', () => {
        const full = probeDiffuse(sphericalHarmonics([lit(UP)], [0, 0, 0]), UP)[0];
        const half = probeDiffuse(sphericalHarmonics([lit(UP, [1, 1, 1], 0.5)], [0, 0, 0]), UP)[0];

        assert.ok(Math.abs(full / 2 - half) < 1e-9, `${full} halved is not ${half}`);
    });

    it('adds the lights and the ambient rather than choosing between them', () => {
        const withAmbient = probeDiffuse(sphericalHarmonics([lit(UP)], [0.1, 0.1, 0.1]), UP)[0];
        const without = probeDiffuse(sphericalHarmonics([lit(UP)], [0, 0, 0]), UP)[0];

        assert.ok(Math.abs(withAmbient - without - 0.1) < 1e-9);
    });

    it('sums the rig, so two lights from the same side beat one', () => {
        const one = probeDiffuse(sphericalHarmonics([lit(UP)], [0, 0, 0]), UP)[0];
        const two = probeDiffuse(sphericalHarmonics([lit(UP), lit(UP)], [0, 0, 0]), UP)[0];

        assert.ok(Math.abs(two - one * 2) < 1e-9);
    });

    it('ignores a light with no direction rather than emitting NaN', () => {
        const probe = sphericalHarmonics([lit([0, 0, 0])], [0.1, 0.1, 0.1]);

        assert.deepEqual(probeDiffuse(probe, UP), [0.1, 0.1, 0.1]);
    });

    it('matches the engine composition for a single light overhead', () => {
        // Hand-computed from the Stanford irradiance matrix, so this pins the constants rather
        // than restating the implementation: a unit white light straight up projects to
        // L00 0.282095, L1-1 0.488603, L20 -0.315392, L22 -0.546274 and nothing else.
        const probe = sphericalHarmonics([lit(UP)], [0, 0, 0]);
        const at = (row: number, col: number) => probe[row * 4 + col];

        assert.ok(Math.abs(at(0, 0) - -0.234376) < 1e-5, `_11 ${at(0, 0)}`);
        assert.ok(Math.abs(at(1, 1) - 0.234376) < 1e-5, `_22 ${at(1, 1)}`);
        assert.ok(Math.abs(at(2, 2) - -0.234376) < 1e-5, `_33 ${at(2, 2)}`);
        assert.ok(Math.abs(at(1, 3) - 0.25) < 1e-5, `_24 ${at(1, 3)}`);
        assert.ok(Math.abs(at(3, 3) - 0.328127) < 1e-5, `_44 ${at(3, 3)}`);
    });

    it('lights the shaded side of the default rig, which a flat probe never did', () => {
        // The regression this port exists for. Every mesh shader takes its ENTIRE diffuse term
        // from this probe - `MeshAlpha.fx:53` has no separate N.L - so a probe holding nothing but
        // the ambient rendered the whole corpus at a flat 0.1 grey, and the reader reported the
        // scene as far too dark. The engine's own rig is a 0.5 sun and two 0.5 blue fills.
        const sun = lit(UP, [1, 1, 1], 0.5);
        const fill = lit([0, -0.17, -0.98], [0.25, 0.25, 0.5], 0.5);
        const probe = sphericalHarmonics([sun, fill], [0.1, 0.1, 0.1]);

        assert.ok(probeDiffuse(probe, UP)[0] > 0.5, 'the lit side is still dark');
    });
});

describe('the fill probe', () => {
    it('is the same maths over the fills alone', () => {
        // `m_sphFill` is what the engine hands the shaders that want the sun excluded, and it is
        // the same call over lights 1 and 2. Reading `m_sphAll` for both would light a surface the
        // artist asked to keep out of the sun.
        const sun = lit(UP, [1, 1, 1], 4);
        const fill = lit(SIDE, [0.25, 0.25, 0.5], 0.5);

        const all = sphericalHarmonics([sun, fill], [0.1, 0.1, 0.1]);
        const fills = sphericalHarmonics([fill], [0.1, 0.1, 0.1]);

        assert.ok(probeDiffuse(all, UP)[0] > probeDiffuse(fills, UP)[0] + 3);
    });

    it('is what SPH_LIGHT_FILL reads, and SPH_LIGHT_ALL reads the other', () => {
        assert.deepEqual(valueFor('SPH_LIGHT_ALL'), FRAME.sphericalHarmonics);
        assert.deepEqual(valueFor('SPH_LIGHT_FILL'), FRAME.sphericalHarmonicsFill);
        assert.notDeepEqual(FRAME.sphericalHarmonics, FRAME.sphericalHarmonicsFill);
    });
});
