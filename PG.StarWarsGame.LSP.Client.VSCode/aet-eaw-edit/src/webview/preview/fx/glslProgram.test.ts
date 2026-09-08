// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Fixtures written for these tests, in the shape the real effects take. Nothing is reproduced from
// Petroglyph's sources.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    buildFragmentShader, buildVertexShader, declareUniforms, findEntry, findStructs,
    neutralisePacking, resolveShaderHandle, stripFxScaffolding,
} from './glslProgram';

const SHADER = `
float4x4 m_worldViewProj : WORLDVIEWPROJECTION;
float3 Colorization : COLORIZATION;

texture BaseTexture
<
    string Name = "default.tga";
>;

sampler BaseSampler = sampler_state
{
    Texture = <BaseTexture>;
    MinFilter = Linear;
};

struct VS_OUTPUT
{
    float4 Pos : POSITION;
    float2 Tex0 : TEXCOORD0;
    float4 Diff : COLOR0;
};

float4 example_ps_main(VS_OUTPUT In) : COLOR
{
    float4 baseTexel = tex2D(BaseSampler, In.Tex0);
    return float4(baseTexel.rgb * Colorization, In.Diff.a);
}
`;

describe('findStructs', () => {
    it('reads a struct and its fields with their semantics', () => {
        const struct = findStructs(SHADER)[0];

        assert.equal(struct.name, 'VS_OUTPUT');
        assert.deepEqual(struct.fields.map(f => f.name), ['Pos', 'Tex0', 'Diff']);
        assert.equal(struct.fields[1].type, 'float2');
        assert.equal(struct.fields[1].semantic, 'TEXCOORD0');
    });
});

describe('findEntry', () => {
    it('reads the entry point signature', () => {
        const entry = findEntry(SHADER, 'example_ps_main');

        assert.equal(entry?.returnType, 'float4');
        assert.equal(entry?.parameterType, 'VS_OUTPUT');
        assert.equal(entry?.parameterName, 'In');
        assert.equal(entry?.semantic, 'COLOR');
    });

    it('returns null for a name that is not there', () => {
        assert.equal(findEntry(SHADER, 'absent_main'), null);
    });
});

describe('declareUniforms', () => {
    it('says out loud what HLSL leaves implicit', () => {
        // A global is a uniform in HLSL simply by being global; GLSL wants the qualifier.
        const out = declareUniforms(SHADER);

        assert.match(out, /uniform float4x4 m_worldViewProj;/);
        assert.doesNotMatch(out, /: WORLDVIEWPROJECTION/);
    });

    it('turns a sampler_state block into a plain sampler uniform', () => {
        const out = declareUniforms(SHADER);

        assert.match(out, /uniform sampler2D BaseSampler;/);
        assert.doesNotMatch(out, /sampler_state/);
    });

    it('drops the texture declaration the sampler refers to', () => {
        // GLSL has no counterpart; the sampler is what the shader reads.
        assert.doesNotMatch(declareUniforms(SHADER), /\btexture\s+BaseTexture/);
    });
});

describe('buildFragmentShader', () => {
    const built = buildFragmentShader(SHADER, 'example_ps_main');

    it('produces a shader', () => {
        assert.equal(built.refusal, null);
        assert.ok(built.source !== null);
    });

    it('opens with the version, then a HIGHP precision declaration', () => {
        // Not mediump. A vertex shader defaults to highp, so declaring mediump downgrades it, and a
        // world-view-projection multiply at fp16 puts the geometry somewhere off screen - with
        // every draw call still issuing and GL reporting no error at all.
        assert.match(built.source!, /^#version 300 es\nprecision highp float;/);
    });

    it('flattens the input struct into one input per field', () => {
        // An `in VS_OUTPUT` looks natural and will not compile: an interface variable must be of
        // basic types only.
        assert.match(built.source!, /in vec2 v_Tex0;/);
        assert.match(built.source!, /in vec4 v_Diff;/);
        assert.doesNotMatch(built.source!, /in VS_OUTPUT/);

        // Not the POSITION field. D3D9 ps_2_0 cannot read POSITION at all, and measured across the
        // corpus not one of the thirty pixel entries does - so declaring it would only burn an
        // interpolator and, worse, demand the vertex side write a varying nothing consumes.
        assert.doesNotMatch(built.source!, /in vec4 v_Pos;/);
    });

    /**
     * A `RawShaderMaterial` gets none of three's built-in chunks, so `material.alphaTest` is inert
     * on a translated effect: the discard has to be in the shader. Alamo's alpha-cut foliage lives
     * or dies on this - `Tree.fx` sets `AlphaRef = 0x80` and its palm fronds are a leaf texture on
     * a rectangle, so without the test the tree is a stack of solid green cards.
     */
    it('carries an alpha test the material can switch on', () => {
        assert.match(built.source!, /uniform float aet_alphaTest;/);

        // `Greater`, which is what every alpha-tested effect in the corpus asks for, so the test
        // discards at and below the reference.
        assert.match(built.source!, /if \(aet_fragColour\.a <= aet_alphaTest\)/);
    });

    it('rebuilds the struct in main and calls the entry point', () => {
        assert.match(built.source!, /VS_OUTPUT In;/);
        assert.match(built.source!, /In\.Tex0 = v_Tex0;/);
        assert.match(built.source!, /aet_fragColour = example_ps_main\(In\);/);
    });

    it('carries the translated arithmetic through', () => {
        assert.match(built.source!, /texture\(BaseSampler, In\.Tex0\)/);
        assert.doesNotMatch(built.source!, /\bfloat4\b/);
    });

    it('refuses, with a reason, when the entry point is not there', () => {
        const missing = buildFragmentShader(SHADER, 'nope_main');

        assert.equal(missing.source, null);
        assert.match(missing.refusal!, /nope_main/);
    });

    it('refuses when the entry takes something that is not a declared struct', () => {
        // Better to fall back to an archetype than to emit a shader referring to a type that was
        // never declared.
        const odd = buildFragmentShader(
            'float4 f(SomethingElse In) : COLOR { return In.x; }', 'f');

        assert.equal(odd.source, null);
        assert.match(odd.refusal!, /SomethingElse/);
    });
});

describe('stripFxScaffolding', () => {
    it('removes a named state block, which is effect scaffolding and not shader code', () => {
        // Two shipped effects declare one at top level, next to the samplers.
        const stripped = stripFxScaffolding(`
stateblock ExampleStates = stateblock_state
{
    ZWriteEnable = true;
    SrcBlend = ONE;
};

float4 f() { return float4(0, 0, 0, 1); }
`);

        assert.ok(!/stateblock/.test(stripped), stripped);
        assert.ok(/float4 f\(\)/.test(stripped), stripped);
    });

    it('drops the `uniform` qualifier from a parameter, keeping the parameter', () => {
        // HLSL lets a function take a compile-time constant this way; the technique supplies the
        // value in its `compile` call. GLSL has no such qualifier, but the parameter is still real.
        const stripped = stripFxScaffolding(
            'float4 ps(VS_OUTPUT In, uniform int DO_FOW) : COLOR\n{\n    return In.Diff;\n}\n');

        assert.ok(!/uniform/.test(stripped), stripped);
        assert.ok(/ps\(VS_OUTPUT In, int DO_FOW\)/.test(stripped), stripped);
    });
});

describe('resolveShaderHandle, on casing', () => {
    it('reads a handle however the declaration is capitalised', () => {
        // FX keywords are case-insensitive and the sources are not consistent: one file of the
        // forty-one writes `PixelShader` where the rest write `pixelshader`, and that one alone was
        // left resolving the handle name as though it were the function.
        const handle = resolveShaderHandle(
            'PixelShader ps_main_gloss_bin = compile ps_1_1 ps_main_gloss();', 'ps_main_gloss_bin');

        assert.equal(handle.entry, 'ps_main_gloss');
    });

    it('strips a handle declaration however it is capitalised', () => {
        const stripped = stripFxScaffolding(
            'PixelShader ps_bin = compile ps_1_1 ps_main();\nfloat4 f() { return ps_bin; }');

        assert.ok(!/compile/.test(stripped), stripped);
    });
});

describe('the GLSL ES 3.00 target', () => {
    // three.js r185 asks for a `webgl2` context and throws if it cannot have one, so the shaders
    // always run against GLSL ES 3.00. Targeting 1.00 meant refusing every skinned effect over
    // `float4x3`, a limitation that lifted in 3.00 and never applied here.
    const built = buildFragmentShader(SHADER, 'example_ps_main');

    it('declares the version on the very first line', () => {
        assert.ok(built.source?.startsWith('#version 300 es\n'), built.source ?? built.refusal ?? '');
    });

    it('takes its inputs as `in`, not `varying`', () => {
        assert.ok(/\bin vec2 v_Tex0;/.test(built.source ?? ''), built.source ?? '');
        assert.ok(!/\bvarying\b/.test(built.source ?? ''), built.source ?? '');
    });

    it('writes to a declared output rather than gl_FragColor', () => {
        assert.ok(!/gl_FragColor/.test(built.source ?? ''), built.source ?? '');
        assert.ok(/\bout vec4 [A-Za-z_]+;/.test(built.source ?? ''), built.source ?? '');
    });

    it('pads a non-square skinning matrix instead of refusing it', () => {
        const skinned = SHADER.replace(
            'float3 Colorization : COLORIZATION;',
            'float4x3 m_skinMatrixArray[26] : SKIN_MATRIX_ARRAY;\nfloat3 Colorization;');

        const program = buildFragmentShader(skinned, 'example_ps_main');

        assert.equal(program.refusal, null);
        assert.ok(/mat4 m_skinMatrixArray/.test(program.source ?? ''), program.source ?? '');
    });

    it('samples with `texture`, since the old names are gone in 3.00', () => {
        assert.ok(/\btexture\(/.test(built.source ?? ''), built.source ?? '');
        assert.ok(!/\btexture2D\(/.test(built.source ?? ''), built.source ?? '');
    });
});

const VERTEX_SHADER = `
float4x4 m_worldViewProj : WORLDVIEWPROJECTION;

struct VS_INPUT
{
    float4 Pos : POSITION;
    float2 Tex : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 Pos : POSITION;
    float2 Tex0 : TEXCOORD0;
};

VS_OUTPUT example_vs_main(VS_INPUT In)
{
    VS_OUTPUT Out = (VS_OUTPUT)0;
    Out.Pos = mul(In.Pos, m_worldViewProj);
    Out.Tex0 = In.Tex;
    return Out;
}

vertexshader example_vs_main_bin = compile vs_1_1 example_vs_main();
`;

describe('buildVertexShader', () => {
    const built = buildVertexShader(VERTEX_SHADER, 'example_vs_main_bin');

    it('resolves the handle the same way the pixel side does', () => {
        assert.equal(built.refusal, null);
    });

    it('declares the attributes three actually binds, not the HLSL field names', () => {
        // `in vec4 a_Pos` binds to nothing and draws an empty screen.
        assert.match(built.source!, /in vec3 position;/);
        assert.match(built.source!, /in vec2 uv;/);
        assert.doesNotMatch(built.source!, /a_Pos/);
    });

    it('adapts each attribute into the type its field declares', () => {
        assert.match(built.source!, /In\.Pos = vec4\(position, 1\.0\);/);
        assert.match(built.source!, /In\.Tex = uv;/);
    });

    it('turns the output struct into outputs matching the fragment shader`s inputs', () => {
        // The fragment side declares `in vec2 v_Tex0;`, so the names have to line up exactly.
        assert.match(built.source!, /out vec2 v_Tex0;/);
    });

    it('drives gl_Position from the field carrying the POSITION semantic', () => {
        assert.match(built.source!, /gl_Position = Out\.Pos;/);
    });

    it('does not also declare the position as an output, since gl_Position carries it', () => {
        assert.doesNotMatch(built.source!, /out vec4 v_Pos;/);
    });

    it('calls the entry point with the assembled input struct', () => {
        assert.match(built.source!, /VS_OUTPUT Out = example_vs_main\(In\);/);
        assert.match(built.source!, /VS_INPUT In = VS_INPUT\(vec4\(0\.0\), vec2\(0\.0\)\);/);
    });
});

describe('stripFxScaffolding, on the preprocessor', () => {
    it('keeps a #define, because GLSL ES has the same preprocessor', () => {
        // Grass.fx switches its geometry modifications on with one, and stripping the definition
        // left the identifier that used it undeclared.
        const stripped = stripFxScaffolding(
            '#define GEOMETRY_MODS_ENABLED 1\nfloat4 f() { return float4(GEOMETRY_MODS_ENABLED); }');

        assert.ok(/#define GEOMETRY_MODS_ENABLED 1/.test(stripped), stripped);
    });

    it('keeps conditional compilation, which the shipped headers rely on', () => {
        const stripped = stripFxScaffolding('#if (ALAMO_STATE_BLOCKS)\n#else\n#endif\n');

        assert.ok(/#if \(ALAMO_STATE_BLOCKS\)/.test(stripped), stripped);
        assert.ok(/#endif/.test(stripped), stripped);
    });

    it('still removes an #include, which by here should already have been flattened', () => {
        // One left standing means the header was not found, and a stale directive would be read as
        // a missing file rather than as the resolution failure it is.
        assert.ok(!/#include/.test(stripFxScaffolding('#include "AlamoEngine.fxh"\n')));
    });
});

describe('the pass order', () => {
    // `stripFxScaffolding` removes the `: SEMANTIC` annotations, and `declareUniforms` finds its
    // globals BY those annotations - so running the strip first leaves every engine global as a
    // plain mutable global, which GLSL zero-initialises. The shader still compiles and still links;
    // it just multiplies every position by a zero matrix and draws nothing at all.
    it('declares the engine globals as uniforms, not as zero-initialised globals', () => {
        const vertex = buildVertexShader(VERTEX_SHADER, 'example_vs_main_bin');

        assert.match(vertex.source!, /uniform mat4 m_worldViewProj;/);
        assert.doesNotMatch(vertex.source!, /^mat4 m_worldViewProj;/m);
    });

    it('does the same on the fragment side', () => {
        const fragment = buildFragmentShader(SHADER, 'example_ps_main');

        assert.match(fragment.source!, /uniform vec3 Colorization;/);
        assert.match(fragment.source!, /uniform sampler2D BaseSampler;/);
    });
});

describe('declareUniforms, on engine textures', () => {
    it('drops a texture object that carries a semantic', () => {
        // `texture m_FOWTexture : FOW_TEXTURE;` - a texture OBJECT, which GLSL has no counterpart
        // for; the sampler that reads it is what becomes a uniform. Left in, it is a syntax error.
        const out = declareUniforms('texture m_FOWTexture : FOW_TEXTURE;\n');

        assert.doesNotMatch(out, /\btexture\s+m_FOWTexture/);
    });

    it('drops one with an annotation block too', () => {
        assert.doesNotMatch(
            declareUniforms('texture BaseTexture\n<\n    string Name = "d.tga";\n>;\n'),
            /\btexture\s+BaseTexture/);
    });
});

describe('declareUniforms, on a texture with both a semantic and an annotation', () => {
    it('drops it, since the two are separately optional rather than alternatives', () => {
        // The sky cube is declared with both, and a regex offering one OR the other left it behind.
        const out = declareUniforms(
            'texture m_skyCubeTexture : SKY_CUBE_TEXTURE < string Type = "Cube"; >;\n');

        assert.doesNotMatch(out, /\btexture\s+m_skyCubeTexture/);
    });
});

describe('the vertex packing helpers', () => {
    // The RSkin vertex formats store UVs and normals as SCALED SHORTS, and the shaders divide them
    // back down - `Unpack_UV` by 4096, `Unpack_Normal` by 16384. Our exporter decodes them when it
    // reads the ALO, so the attribute arriving here is already unpacked and the division is applied
    // to a correct value: every vertex ends up sampling a single texel and the model draws flat.
    it('neutralises Unpack_UV, because the exporter already decoded the UVs', () => {
        const out = neutralisePacking(
            'vec2 Unpack_UV(vec2 uv)\n{\n\treturn uv * (1.0 / 4096.0);\n}\n');

        assert.match(out, /vec2 Unpack_UV\(vec2 uv\)\s*\{\s*return uv;\s*\}/);
        assert.doesNotMatch(out, /4096/);
    });

    it('neutralises Unpack_Normal too', () => {
        const out = neutralisePacking(
            'vec3 Unpack_Normal(vec3 norm)\n{\n\treturn norm * (1.0 / 16384.0);\n}\n');

        assert.match(out, /return norm;/);
        assert.doesNotMatch(out, /16384/);
    });

    it('leaves the call sites alone, so the shader still reads the same', () => {
        const out = neutralisePacking(
            'vec2 Unpack_UV(vec2 uv)\n{\n\treturn uv * (1.0 / 4096.0);\n}\n'
            + 'void f() { Out.Tex0 = Unpack_UV(In.Tex0); }\n');

        assert.match(out, /Out\.Tex0 = Unpack_UV\(In\.Tex0\);/);
    });

    it('leaves everything else untouched', () => {
        const source = 'vec3 Compute_Fog(vec3 p)\n{\n\treturn p * 0.5;\n}\n';

        assert.equal(neutralisePacking(source), source);
    });
});
