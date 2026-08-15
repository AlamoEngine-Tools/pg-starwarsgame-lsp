// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Fixtures written for these tests. Nothing is reproduced from Petroglyph's sources.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    attributeFor, collectSamplerTextures, collectSemantics, collectUniformDefaults,
    collectUniformTypes, fitToUniform, uniformSourceFor,
} from './semantics';

describe('collectSemantics', () => {
    it('reads the semantic off each annotated global', () => {
        const found = collectSemantics(`
float4x4 m_worldViewProj : WORLDVIEWPROJECTION;
float4 m_eyePos : EYE_POSITION;
float3 Colorization : COLORIZATION;
float4 NoSemanticHere;
`);

        assert.equal(found.get('m_worldViewProj'), 'WORLDVIEWPROJECTION');
        assert.equal(found.get('m_eyePos'), 'EYE_POSITION');
        assert.equal(found.get('Colorization'), 'COLORIZATION');
        assert.equal(found.has('NoSemanticHere'), false);
    });

    it('reads one on an array, which is how the light probes are declared', () => {
        assert.equal(
            collectSemantics('float4x4 m_sphAll[3] : SPH_LIGHT_ALL;').get('m_sphAll'),
            'SPH_LIGHT_ALL');
    });

    it('does not mistake a struct field for a global', () => {
        // Struct fields carry semantics too, and they are attributes rather than uniforms.
        const found = collectSemantics(`
struct VS_INPUT
{
    float4 Pos : POSITION;
};
`);

        assert.equal(found.has('Pos'), false);
    });
});

describe('attributeFor', () => {
    it('maps the vertex semantics onto the names the exported glTF uses', () => {
        assert.equal(attributeFor('POSITION'), 'position');
        assert.equal(attributeFor('NORMAL'), 'normal');
        assert.equal(attributeFor('TEXCOORD0'), 'uv');
        assert.equal(attributeFor('TEXCOORD1'), 'uv1');
        assert.equal(attributeFor('TANGENT0'), 'tangent');
        assert.equal(attributeFor('COLOR0'), 'color');
    });

    it('has no answer for a uniform semantic', () => {
        assert.equal(attributeFor('WORLDVIEWPROJECTION'), null);
    });
});

describe('uniformSourceFor', () => {
    it('names where each matrix comes from', () => {
        assert.deepEqual(uniformSourceFor('WORLDVIEWPROJECTION'), { kind: 'matrix', of: 'worldViewProjection' });
        assert.deepEqual(uniformSourceFor('WORLDVIEWINVERSE'), { kind: 'matrix', of: 'worldViewInverse' });
        assert.deepEqual(uniformSourceFor('WORLD'), { kind: 'matrix', of: 'world' });
    });

    it('distinguishes the eye position in world space from the one in object space', () => {
        // Both are supplied; a shader working in object space uses the second and would be lit from
        // the wrong side by the first.
        assert.deepEqual(uniformSourceFor('EYE_POSITION'), { kind: 'eye', space: 'world' });
        assert.deepEqual(uniformSourceFor('EYE_OBJ_POSITION'), { kind: 'eye', space: 'object' });
    });

    it('separates each light`s direction, colour and specular', () => {
        assert.deepEqual(
            uniformSourceFor('DIR_LIGHT_VEC_0'),
            { kind: 'light', index: 0, channel: 'vector', space: 'world' });
        assert.deepEqual(
            uniformSourceFor('DIR_LIGHT_OBJ_VEC_2'),
            { kind: 'light', index: 2, channel: 'vector', space: 'object' });
        assert.deepEqual(
            uniformSourceFor('DIR_LIGHT_DIFFUSE_1'),
            { kind: 'light', index: 1, channel: 'diffuse', space: 'world' });
        assert.deepEqual(
            uniformSourceFor('DIR_LIGHT_SPECULAR_0'),
            { kind: 'light', index: 0, channel: 'specular', space: 'world' });
    });

    it('knows the scalars the engine supplies per frame', () => {
        assert.deepEqual(uniformSourceFor('TIME'), { kind: 'time' });
        assert.deepEqual(uniformSourceFor('RESOLUTION_CONSTANTS'), { kind: 'resolution' });
    });

    it('treats the engine`s own textures as a named role', () => {
        // Fog of war and the cloud layer are engine state the preview has none of; the viewport
        // substitutes a neutral texture so the shader still runs.
        assert.deepEqual(uniformSourceFor('FOW_TEXTURE'), { kind: 'texture', role: 'fogOfWar' });
        assert.deepEqual(uniformSourceFor('SKY_CUBE_TEXTURE'), { kind: 'texture', role: 'skyCube' });
    });

    it('binds the bone palette, which every skinned effect asks for', () => {
        // Missed by the hand-written list below and found by sweeping the real corpus - all eleven
        // RSkin* effects declare it, and without a source every one of them would render unskinned.
        assert.deepEqual(uniformSourceFor('SKINMATRIXARRAY'), { kind: 'skinMatrices' });
    });

    it('admits when a semantic has no source, rather than inventing one', () => {
        // A wrong value renders something plausible and wrong, which is worse than a shader that
        // falls back to its archetype.
        assert.equal(uniformSourceFor('SOME_MOD_SPECIFIC_THING'), null);
    });

    it('covers every semantic the shipped headers declare', () => {
        // Measured off the corpus; a gap here is a uniform that would silently stay at zero.
        const declared = [
            'WORLD', 'WORLDINVERSE', 'WORLDVIEW', 'WORLDVIEWINVERSE', 'WORLDVIEWPROJECTION',
            'VIEW', 'VIEWINVERSE', 'VIEWPROJECTION', 'PROJECTION',
            'EYE_POSITION', 'EYE_OBJ_POSITION',
            'DIR_LIGHT_VEC_0', 'DIR_LIGHT_VEC_1', 'DIR_LIGHT_VEC_2',
            'DIR_LIGHT_OBJ_VEC_0', 'DIR_LIGHT_OBJ_VEC_1', 'DIR_LIGHT_OBJ_VEC_2',
            'DIR_LIGHT_DIFFUSE_0', 'DIR_LIGHT_DIFFUSE_1', 'DIR_LIGHT_DIFFUSE_2',
            'DIR_LIGHT_SPECULAR_0', 'GLOBAL_AMBIENT', 'LIGHT_SCALE',
            'SPH_LIGHT_ALL', 'SPH_LIGHT_FILL', 'SKINMATRIXARRAY',
            'TIME', 'RESOLUTION_CONSTANTS', 'FOG_VALS', 'DISTANCE_FADE_VALS',
            'SHADOW_EXTRUSION_DISTANCE', 'WIND_BEND_VECTOR', 'WIND_GRASS_PARAMS',
            'FOW_TEXTURE', 'FOW_TEX_U', 'FOW_TEX_V',
            'CLOUD_TEXTURE', 'CLOUD_TEX_U', 'CLOUD_TEX_V', 'SKY_CUBE_TEXTURE',
        ];

        const missing = declared.filter(semantic => uniformSourceFor(semantic) === null);

        assert.deepEqual(missing, []);
    });
});

describe('collectUniformDefaults', () => {
    it('reads a default initialiser, dropping the HLSL float suffix', () => {
        const defaults = collectUniformDefaults(
            'float4 m_lightAmbient : GLOBAL_AMBIENT = {0.2f, 0.2f, 0.2f, 1.0f};');

        assert.deepEqual(defaults.get('m_lightAmbient'), [0.2, 0.2, 0.2, 1.0]);
    });

    it('handles a negative and a bare integer', () => {
        const defaults = collectUniformDefaults(
            'float2 m_distanceFadeVals : DISTANCE_FADE_VALS = { -0.0001,1000.0f };');

        assert.deepEqual(defaults.get('m_distanceFadeVals'), [-0.0001, 1000.0]);
    });

    it('flattens an array of matrices spread over many lines', () => {
        const defaults = collectUniformDefaults(`
float4x4 m_sph[2] : SPH_LIGHT_ALL =
{
    {
    1.0, 2.0, 3.0, 4.0,
    5.0, 6.0, 7.0, 8.0,
    9.0, 10.0, 11.0, 12.0,
    13.0, 14.0, 15.0, 16.0
    },
    {
    17.0, 18.0, 19.0, 20.0,
    21.0, 22.0, 23.0, 24.0,
    25.0, 26.0, 27.0, 28.0,
    29.0, 30.0, 31.0, 32.0
    }
};
`);

        assert.equal(defaults.get('m_sph')?.length, 32);
        assert.equal(defaults.get('m_sph')?.[0], 1.0);
        assert.equal(defaults.get('m_sph')?.[31], 32.0);
    });

    it('has nothing for a uniform declared without one', () => {
        assert.equal(
            collectUniformDefaults('float m_time : TIME;').has('m_time'),
            false);
    });

    it('does not read an initialiser off a local', () => {
        // Indented, inside a function - not a uniform at all.
        const defaults = collectUniformDefaults(
            'void f()\n{\n    float4 local = {1.0, 2.0, 3.0, 4.0};\n}\n');

        assert.equal(defaults.has('local'), false);
    });
});

describe('collectSamplerTextures', () => {
    it('links a sampler to the texture parameter it reads', () => {
        // The sub-mesh's parameters are keyed by the TEXTURE name, not the sampler name, so without
        // this link a translated shader has samplers with nothing bound to them. The shipped
        // sources write the reference in PARENTHESES - 41 of the 42 do - and vary the keyword's
        // capitalisation, which an angle-bracket fixture written from memory quietly missed.
        const links = collectSamplerTextures(`
sampler BaseSampler = sampler_state
{
    Texture   = (BaseTexture);
    MinFilter = LINEAR;
    AddressU  = WRAP;
};
`);

        assert.equal(links.get('BaseSampler'), 'BaseTexture');
    });

    it('reads the lower-cased keyword and the angle-bracket form as well', () => {
        assert.equal(
            collectSamplerTextures(
                'sampler S = sampler_state { texture = (BaseTexture); };').get('S'),
            'BaseTexture');
        assert.equal(
            collectSamplerTextures(
                'sampler S = sampler_state { Texture = <BaseTexture>; };').get('S'),
            'BaseTexture');
    });

    it('reads several, which every bump shader declares', () => {
        const links = collectSamplerTextures(`
sampler BaseSampler = sampler_state { Texture = (BaseTexture); };
sampler NormalSampler = sampler_state { Texture = (NormalTexture); };
sampler GlossSampler = sampler_state { Texture = (GlossTexture); };
`);

        assert.deepEqual([...links], [
            ['BaseSampler', 'BaseTexture'],
            ['NormalSampler', 'NormalTexture'],
            ['GlossSampler', 'GlossTexture'],
        ]);
    });

    it('ignores a sampler that names no texture', () => {
        assert.equal(
            collectSamplerTextures('sampler S = sampler_state { MinFilter = Linear; };').size, 0);
    });
});

describe('collectUniformTypes', () => {
    // A uniform's DECLARED width is what a value has to fit. The ALO carries `Diffuse` as four
    // components while the effect declares `float3 Diffuse` - and `uniform3fv` with a 4-length
    // array is INVALID_VALUE, so the call is dropped and the uniform silently stays zero. That is
    // what made every translated model render black.
    it('reads the declared type of each uniform', () => {
        const types = collectUniformTypes(
            'uniform vec3 Diffuse;\nuniform vec4 Colorization;\nuniform float Shininess;\n');

        assert.equal(types.get('Diffuse'), 'vec3');
        assert.equal(types.get('Colorization'), 'vec4');
        assert.equal(types.get('Shininess'), 'float');
    });

    it('reads an array uniform by its element type', () => {
        assert.equal(collectUniformTypes('uniform mat4 m_sphFill[3];').get('m_sphFill'), 'mat4');
    });

    it('reads samplers, so they are never treated as numbers', () => {
        assert.equal(
            collectUniformTypes('uniform sampler2D BaseSampler;').get('BaseSampler'), 'sampler2D');
    });

    it('ignores a plain global, which is not a uniform', () => {
        assert.equal(collectUniformTypes('vec3 notAUniform;').has('notAUniform'), false);
    });
});

describe('fitToUniform', () => {
    it('truncates a four-component parameter down to a vec3 uniform', () => {
        assert.deepEqual(fitToUniform([1, 1, 1, 0], 'vec3'), [1, 1, 1]);
    });

    it('leaves a matching width alone', () => {
        assert.deepEqual(fitToUniform([1, 0, 0, 1], 'vec4'), [1, 0, 0, 1]);
    });

    it('unwraps a single value for a scalar uniform', () => {
        assert.equal(fitToUniform([32], 'float'), 32);
        assert.equal(fitToUniform([1, 2, 3], 'float'), 1);
    });

    it('pads a short value rather than sending a length GL will reject', () => {
        // A too-short array is refused by uniform4fv just as firmly as a too-long one.
        assert.deepEqual(fitToUniform([0.5, 0.25], 'vec4'), [0.5, 0.25, 0, 0]);
    });

    it('leaves an array uniform whole, since its length is a multiple of the element', () => {
        const three = new Array(48).fill(1);

        assert.equal((fitToUniform(three, 'mat4') as number[]).length, 48);
    });

    it('leaves a value alone when the type is unknown', () => {
        assert.deepEqual(fitToUniform([1, 2, 3, 4], undefined), [1, 2, 3, 4]);
    });
});

describe('collectUniformDefaults, on annotated material parameters', () => {
    // The material parameters carry an ANNOTATION between the name and the `=`, not a semantic:
    // `float3 Diffuse < string UIName="Diffuse"; > = {1,1,1};`. Allowing only a semantic there meant
    // Diffuse, Specular, Emissive and Colorization were never seeded - and `Diffuse` multiplies the
    // whole diffuse term, so every translated model rendered BLACK.
    it('reads a default that sits behind an annotation block', () => {
        const defaults = collectUniformDefaults(
            'float3 Diffuse < string UIName="Diffuse"; string UIType = "ColorSwatch"; > '
            + '= {1.0f, 1.0f, 1.0f };\n');

        assert.deepEqual(defaults.get('Diffuse'), [1, 1, 1]);
    });

    it('does not mistake numbers inside the annotation for the value', () => {
        const defaults = collectUniformDefaults(
            'float Shininess < string UIName="Shininess"; float UIMin = 2.0; > = 32.0;\n');

        assert.deepEqual(defaults.get('Shininess'), [32]);
    });

    it('still reads one behind a semantic', () => {
        assert.deepEqual(
            collectUniformDefaults('float4 m_lightScale : LIGHT_SCALE = { 1.0f, 1.0f, 1.0f, 1.0f };')
                .get('m_lightScale'),
            [1, 1, 1, 1]);
    });

    it('reads one carrying both, in the order the sources write them', () => {
        assert.deepEqual(
            collectUniformDefaults('float4 X : SOME_SEM < string N="x"; > = {1.0, 2.0, 3.0, 4.0};')
                .get('X'),
            [1, 2, 3, 4]);
    });
});
