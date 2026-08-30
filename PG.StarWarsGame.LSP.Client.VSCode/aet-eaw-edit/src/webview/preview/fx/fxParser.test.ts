// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every fixture here is written for this test. The real effects are Petroglyph's and confidential;
// this repository is public, so nothing from them is reproduced - only the FORMAT is followed.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    hasProgrammableShader, parseFxManifest, selectTechnique, stripComments,
} from './fxParser';

const EFFECT = `
// A made-up effect, in the shape the real ones take.
string _ALAMO_RENDER_PHASE = "Opaque";
string _ALAMO_VERTEX_PROC = "Mesh";
string _ALAMO_VERTEX_TYPE = "alD3dVertNU2";
bool _ALAMO_TANGENT_SPACE = true;
bool _ALAMO_SHADOW_VOLUME = false;

#include "Example.fxh"

float4x4 m_worldViewProj : WORLDVIEWPROJECTION;
float3 m_lightDirection : DIR_LIGHT_OBJ_VEC_0;

struct VertexIn
{
    float4 Pos : POSITION;
    float2 Tex : TEXCOORD0;
};

technique editor_only
{
    pass p0
    {
        ZWriteEnable = true;
        VertexShader = (vs_main_bin);
        PixelShader  = (ps_main_bin);
    }
}

technique shaded
<
    string LOD="DX9";
>
{
    pass shaded_p0
    {
        ZWriteEnable = true;
        ZFunc = LESSEQUAL;
        AlphaBlendEnable = false;
        SrcBlend = SRCALPHA;
        DestBlend = INVSRCALPHA;
        CullMode = CCW;

        VertexShader = (shaded_vs_bin);
        PixelShader  = (shaded_ps_bin);
    }
}

technique simple
<
    string LOD="DX8";
>
{
    pass simple_p0
    {
        ZWriteEnable = false;
        VertexShader = (simple_vs_bin);
    }
}
`;

describe('stripComments', () => {
    it('removes line and block comments', () => {
        assert.equal(stripComments('a // gone\nb /* also gone */ c').replace(/\s+/g, ' ').trim(),
            'a b c');
    });

    it('leaves a quoted string alone, slashes and all', () => {
        // A naive sweep eats the // inside any quoted path and the file stops parsing.
        assert.match(stripComments('string s = "http://example/x.fxh";'), /http:\/\/example/);
    });

    it('does not weld two identifiers together where a comment was', () => {
        assert.equal(stripComments('a/*x*/b').trim(), 'a b');
    });

    it('survives an unterminated block comment', () => {
        assert.doesNotThrow(() => stripComments('a /* never closed'));
    });
});

describe('parseFxManifest', () => {
    const effect = parseFxManifest(EFFECT);

    it('reads the _ALAMO_ manifest', () => {
        assert.equal(effect.annotations.renderPhase, 'Opaque');
        assert.equal(effect.annotations.vertexProc, 'Mesh');
        assert.equal(effect.annotations.vertexType, 'alD3dVertNU2');
        assert.equal(effect.annotations.tangentSpace, true);
        assert.equal(effect.annotations.shadowVolume, false);
    });

    it('lists the headers it pulls in', () => {
        // The parameters live in these, so the translator has to follow them.
        assert.deepEqual(effect.includes, ['Example.fxh']);
    });

    it('picks up parameters with an engine semantic', () => {
        const wvp = effect.parameters.find(p => p.name === 'm_worldViewProj');

        assert.equal(wvp?.type, 'float4x4');
        assert.equal(wvp?.semantic, 'WORLDVIEWPROJECTION');
    });

    it('leaves struct fields out of the parameter list', () => {
        // POSITION on a struct member describes a vertex layout, not something to bind.
        assert.equal(effect.parameters.some(p => p.name === 'Pos'), false);
        assert.equal(effect.parameters.some(p => p.name === 'Tex'), false);
    });

    it('finds every technique, annotated or not', () => {
        assert.deepEqual(effect.techniques.map(t => t.name),
            ['editor_only', 'shaded', 'simple']);
    });

    it('reads the LOD annotation, and leaves it absent when there is none', () => {
        assert.equal(effect.techniques[0].lod, undefined);
        assert.equal(effect.techniques[1].lod, 'DX9');
        assert.equal(effect.techniques[2].lod, 'DX8');
    });

    it('reads a pass and its render state', () => {
        const pass = effect.techniques[1].passes[0];

        assert.equal(pass.name, 'shaded_p0');
        assert.equal(pass.states.zwriteenable, 'true');
        assert.equal(pass.states.zfunc, 'LESSEQUAL');
        assert.equal(pass.states.srcblend, 'SRCALPHA');
        assert.equal(pass.states.cullmode, 'CCW');
    });

    it('keeps the entry points out of the render state', () => {
        // They are parenthesised assignments in the same block, and treating them as state would
        // put a shader name where a blend factor belongs.
        const pass = effect.techniques[1].passes[0];

        assert.equal(pass.vertexShader, 'shaded_vs_bin');
        assert.equal(pass.pixelShader, 'shaded_ps_bin');
        assert.equal(pass.states.vertexshader, undefined);
    });

    it('leaves a missing pixel shader absent rather than empty', () => {
        assert.equal(effect.techniques[2].passes[0].pixelShader, undefined);
    });

    it('reads an effect that declares nothing at all without throwing', () => {
        const empty = parseFxManifest('');

        assert.deepEqual(empty.techniques, []);
        assert.deepEqual(empty.includes, []);
    });

    it('does not choke on an unclosed technique', () => {
        // A truncated or mid-edit file must cost that technique, not the whole preview.
        const broken = parseFxManifest('technique a { pass p0 { ZWriteEnable = true;');

        assert.deepEqual(broken.techniques, []);
    });
});

describe('techniques with no programmable shader', () => {
    // Measured across the real effects: seven of them have a DX8 technique that sets render state
    // and nothing else, and their FIXEDFUNCTION sibling writes `VertexShader = NULL;` outright.
    // There is nothing to translate in either, and the caller has to be able to tell.
    const STATE_ONLY = `
technique t0
<
    string LOD="DX8";
>
{
    pass p0
    {
        ZWriteEnable = false;
        SrcBlend = ONE;
        DestBlend = ONE;
    }
}

technique t1
<
    string LOD="FIXEDFUNCTION";
>
{
    pass p0
    {
        ZWriteEnable = false;
        VertexShader = NULL;
        PixelShader = NULL;
    }
}
`;

    it('reads a pass that sets state and names no shader', () => {
        const effect = parseFxManifest(STATE_ONLY);
        const pass = effect.techniques[0].passes[0];

        assert.equal(pass.vertexShader, undefined);
        assert.equal(Object.keys(pass.states).length, 3);
    });

    it('treats an explicit NULL as no shader, not as an entry point called NULL', () => {
        // Otherwise the translator goes looking for a function named NULL.
        const pass = parseFxManifest(STATE_ONLY).techniques[1].passes[0];

        assert.equal(pass.vertexShader, undefined);
        assert.equal(pass.pixelShader, undefined);
    });

    it('says whether a technique has anything to translate', () => {
        const effect = parseFxManifest(STATE_ONLY);

        assert.equal(hasProgrammableShader(effect.techniques[0]), false);
        assert.equal(hasProgrammableShader(parseFxManifest(EFFECT).techniques[1]), true);
    });
});

describe('selectTechnique', () => {
    const effect = parseFxManifest(EFFECT);

    it('prefers DX9, which is the one with the real shading', () => {
        assert.equal(selectTechnique(effect)?.name, 'shaded');
    });

    it('falls back through the ladder when DX9 is absent', () => {
        const withoutDx9 = {
            ...effect,
            techniques: effect.techniques.filter(t => t.lod !== 'DX9'),
        };

        assert.equal(selectTechnique(withoutDx9)?.name, 'simple');
    });

    it('takes an unannotated technique only as a last resort', () => {
        // In the shipped effects those are editor-only passes like max_viewport.
        const onlyPlain = { ...effect, techniques: [effect.techniques[0]] };

        assert.equal(selectTechnique(onlyPlain)?.name, 'editor_only');
    });

    it('returns null when there is nothing to draw with', () => {
        assert.equal(selectTechnique({ ...effect, techniques: [] }), null);
    });

    it('knows the LOD spellings the real effects actually use', () => {
        // Two more turned up in the shipped set than the three the plan listed: `FF`, and an
        // ATI-specific `DX8ATI`. Unknown to the ladder, they would have fallen through to the
        // unannotated last resort - an editor-only pass - which is worse than either.
        const exotic = parseFxManifest(`
technique ati
<
    string LOD="DX8ATI";
>
{
    pass p0 { ZWriteEnable = true; VertexShader = (a_bin); }
}

technique ff
<
    string LOD="FF";
>
{
    pass p0 { ZWriteEnable = true; }
}
`);

        assert.equal(selectTechnique(exotic)?.name, 'ati');
    });

    it('prefers plain DX8 to the vendor-specific variant', () => {
        const both = parseFxManifest(`
technique ati
<
    string LOD="DX8ATI";
>
{
    pass p0 { VertexShader = (a_bin); }
}

technique plain
<
    string LOD="DX8";
>
{
    pass p0 { VertexShader = (b_bin); }
}
`);

        assert.equal(selectTechnique(both)?.name, 'plain');
    });
});

describe('parseFxManifest, a shader compiled inline', () => {
    /**
     * The form the shipped effects overwhelmingly use, and the one that was being missed entirely.
     *
     * `VertexShader = compile vs_1_1 vs_main();` is not `key = value;` - the right-hand side is
     * three tokens and a pair of parentheses. The state regex matched `compile`, went looking for a
     * semicolon and found more identifiers, so the whole assignment fell through: the pass came out
     * with no shader, `hasProgrammableShader` said false, and the effect was reported as "render
     * state only" and left on its archetype material. Measured across `shader-sources/`, that took
     * SEVEN of the 41 effects out - among them all three additive ones, which is what a light mesh
     * laid over a hull draws with.
     */
    const COMPILED = `
technique t0
<
    string LOD="DX8";
>
{
    pass t0_p0
    {
        ZWriteEnable = FALSE;
        SrcBlend = ONE;

        // shaders
        VertexShader = compile vs_1_1 vs_main();
        PixelShader  = compile ps_1_1 additive_ps_main();
    }
}
`;

    it('reads the entry point out of a compile assignment', () => {
        const pass = parseFxManifest(COMPILED).techniques[0].passes[0];

        assert.equal(pass.vertexShader, 'vs_main');
        assert.equal(pass.pixelShader, 'additive_ps_main');
    });

    it('does not leave the profile or the keyword behind as render state', () => {
        // `compile`, `vs_1_1` and the entry name are not blend factors.
        const pass = parseFxManifest(COMPILED).techniques[0].passes[0];

        assert.equal(pass.states.vertexshader, undefined);
        assert.equal(pass.states.pixelshader, undefined);
        assert.equal(pass.states.compile, undefined);
    });

    it('still reads the render state around it', () => {
        const pass = parseFxManifest(COMPILED).techniques[0].passes[0];

        assert.equal(pass.states.zwriteenable, 'FALSE');
        assert.equal(pass.states.srcblend, 'ONE');
    });

    it('reports the technique as programmable', () => {
        const technique = parseFxManifest(COMPILED).techniques[0];

        assert.equal(hasProgrammableShader(technique), true);
    });

    it('takes an entry point with arguments', () => {
        const pass = parseFxManifest(`
technique t0 { pass p { VertexShader = compile vs_2_0 vs_main(true, 3); } }
`).techniques[0].passes[0];

        assert.equal(pass.vertexShader, 'vs_main');
    });

    it('still treats a compiled NULL as no shader', () => {
        const pass = parseFxManifest(`
technique t0 { pass p { VertexShader = NULL; PixelShader = NULL; } }
`).techniques[0].passes[0];

        assert.equal(pass.vertexShader, undefined);
        assert.equal(pass.pixelShader, undefined);
    });

    // A fixed-function pass says what it draws with through INDEXED texture-stage state, and the
    // state reader only ever matched bare identifiers - so `ColorOp[0]` matched nothing at all and
    // every stage op was dropped on the floor. That is what left MODULATE2X unimplemented.
    it('records an indexed texture-stage state', () => {
        const pass = parseFxManifest(`
technique t0 { pass p {
    ColorOp[0]=MODULATE2X;
    ColorArg1[0]=TEXTURE;
    AlphaOp[0]=MODULATE;
    ColorOp[1]=DISABLE;
} }
`).techniques[0].passes[0];

        assert.equal(pass.states['colorop[0]'], 'MODULATE2X');
        assert.equal(pass.states['colorarg1[0]'], 'TEXTURE');
        assert.equal(pass.states['alphaop[0]'], 'MODULATE');
        assert.equal(pass.states['colorop[1]'], 'DISABLE');
    });

    it('keeps reading unindexed state beside the indexed kind', () => {
        const pass = parseFxManifest(`
technique t0 { pass p { ZWriteEnable=false; ColorOp[0]=MODULATE2X; CullMode=NONE; } }
`).techniques[0].passes[0];

        assert.equal(pass.states.zwriteenable, 'false');
        assert.equal(pass.states.cullmode, 'NONE');
        assert.equal(pass.states['colorop[0]'], 'MODULATE2X');
    });
});
