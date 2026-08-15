// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { uniformSourceFor, type AlamoMatrix } from './semantics';
import { engineLight, neutralHarmonics, uniformValue, type AlamoFrame } from './uniforms';

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
    sphericalHarmonics: neutralHarmonics([0.2, 0.2, 0.2]),
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
    // The effects read their ambient from an SH probe, and the value shipped in the header is a
    // PLACEHOLDER environment, not a neutral one: its constant terms are R 0.7379, G 0.4108,
    // B 0.5165. Leaving it in gave every translated model a mauve cast. In game the engine supplies
    // the real scene's harmonics; a model viewer has no scene, so it supplies a neutral one.
    it('supplies a neutral probe rather than leaving the header placeholder', () => {
        const value = valueFor('SPH_LIGHT_ALL');

        assert.notEqual(value, undefined);
        assert.equal((value as number[]).length, 48);
    });

    it('encodes a constant, so every normal receives the same ambient', () => {
        // `dot(n4, n4 * M)` collapses to M[3][3] when nothing else is set, whatever the normal is.
        const value = valueFor('SPH_LIGHT_FILL') as number[];

        for (let channel = 0; channel < 3; channel++) {
            const matrix = value.slice(channel * 16, channel * 16 + 16);

            assert.equal(matrix[15], FRAME.ambient[0], `channel ${channel} constant`);
            assert.deepEqual(matrix.slice(0, 15), new Array(15).fill(0),
                `channel ${channel} should carry nothing but the constant`);
        }
    });

    it('is grey, which is the whole point', () => {
        const value = valueFor('SPH_LIGHT_ALL') as number[];

        assert.equal(value[15], value[31]);
        assert.equal(value[31], value[47]);
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

describe('neutralHarmonics', () => {
    it('answers with the ambient the rig asks for, per channel', () => {
        // Per channel, because the engine's ambient is a colour and a tinted one is the difference
        // between a cool shadowed side and a grey one.
        const probe = neutralHarmonics([0.2, 0.1, 0.05]);

        assert.equal(probe[15], 0.2);
        assert.equal(probe[31], 0.1);
        assert.equal(probe[47], 0.05);
    });
});
