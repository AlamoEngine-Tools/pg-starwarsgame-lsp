// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { collectFunctions, collectGlobals, collectStructs, inferType } from './glslTypes';
import { fixBooleanConditions, fixVectorTruncation, narrowExpression } from './truncation';

const SOURCE = `
uniform vec4 m_light0Vector;
uniform vec4 m_eyePos;
uniform vec3 Colorization;
uniform sampler2D BaseSampler;

struct VS_OUTPUT
{
    vec4 Pos;
    vec2 Tex0;
    vec4 Diff;
};
`;

const scope = collectGlobals(SOURCE);
const structs = collectStructs(SOURCE);

describe('inferType', () => {
    it('reads a global', () => {
        assert.equal(inferType('m_light0Vector', scope, structs), 'vec4');
    });

    it('reads a swizzle by its length', () => {
        assert.equal(inferType('m_light0Vector.xyz', scope, structs), 'vec3');
        assert.equal(inferType('m_light0Vector.rg', scope, structs), 'vec2');
        assert.equal(inferType('m_light0Vector.x', scope, structs), 'float');
    });

    it('knows a constructor by its name', () => {
        assert.equal(inferType('vec3(1.0, 2.0, 3.0)', scope, structs), 'vec3');
    });

    it('knows the intrinsics that do not follow their argument', () => {
        assert.equal(inferType('dot(a, b)', scope, structs), 'float');
        assert.equal(inferType('texture(BaseSampler, uv)', scope, structs), 'vec4');
        assert.equal(inferType('length(m_eyePos)', scope, structs), 'float');
    });

    it('follows the first argument for everything else', () => {
        assert.equal(inferType('normalize(Colorization)', scope, structs), 'vec3');
    });

    it('narrows a mixed-width binary expression, the way HLSL does', () => {
        assert.equal(inferType('m_eyePos - Colorization', scope, structs), 'vec3');
    });

    it('lets a scalar broadcast without narrowing', () => {
        assert.equal(inferType('m_eyePos * 2.0', scope, structs), 'vec4');
    });

    it('admits when it does not know', () => {
        // Never forced: a guessed type puts a swizzle somewhere that changes the arithmetic.
        assert.equal(inferType('mystery_function(x)', scope, structs), null);
    });
});

describe('narrowExpression', () => {
    it('adds the swizzle HLSL left implicit', () => {
        assert.equal(narrowExpression('m_light0Vector', 'vec3', scope, structs),
            'm_light0Vector.xyz');
    });

    it('leaves a matching width alone', () => {
        assert.equal(narrowExpression('Colorization', 'vec3', scope, structs), 'Colorization');
    });

    it('never widens', () => {
        assert.equal(narrowExpression('Colorization', 'vec4', scope, structs), 'Colorization');
    });

    it('leaves a scalar alone, because it broadcasts', () => {
        assert.equal(narrowExpression('2.0', 'vec3', scope, structs), '2.0');
    });

    it('parenthesises where a swizzle would otherwise bind wrongly', () => {
        const out = narrowExpression('m_eyePos * 2.0', 'vec3', scope, structs);

        assert.equal(out, '(m_eyePos * 2.0).xyz');
    });

    it('does not double up parentheses on a call', () => {
        assert.equal(narrowExpression('texture(BaseSampler, uv)', 'vec3', scope, structs),
            'texture(BaseSampler, uv).xyz');
    });

    it('leaves an expression it cannot type exactly as it was', () => {
        assert.equal(narrowExpression('mystery(x)', 'vec3', scope, structs), 'mystery(x)');
    });
});

describe('fixVectorTruncation', () => {
    it('fixes the assignment that blocked every translated effect', () => {
        // `vec3 L = m_light0Vector;` - legal HLSL, rejected by GLSL.
        const fixed = fixVectorTruncation(`${SOURCE}
void main()
{
    vec3 L = m_light0Vector;
}
`);

        assert.match(fixed, /vec3 L = m_light0Vector\.xyz;/);
    });

    it('narrows both sides of a mixed-width subtraction', () => {
        // HLSL computes float4 - float3 in float3; GLSL will not mix widths at all.
        const fixed = fixVectorTruncation(`${SOURCE}
void main()
{
    vec3 P = Colorization;
    vec3 E = normalize(m_eyePos - P);
}
`);

        assert.match(fixed, /m_eyePos\.xyz - P/);
    });

    it('leaves a correct assignment untouched', () => {
        const fixed = fixVectorTruncation(`${SOURCE}
void main()
{
    vec4 c = m_eyePos;
}
`);

        assert.match(fixed, /vec4 c = m_eyePos;/);
    });

    it('leaves a global initialiser alone', () => {
        // Those became uniforms, which cannot be initialised at all.
        assert.match(fixVectorTruncation('uniform vec4 x;\n'), /uniform vec4 x;/);
    });

    it('handles an assignment to a struct member', () => {
        const fixed = fixVectorTruncation(`${SOURCE}
void main()
{
    VS_OUTPUT Out;
    Out.Tex0 = m_eyePos;
}
`);

        assert.match(fixed, /Out\.Tex0 = m_eyePos\.xy;/);
    });
});

// A matrix multiply is how nearly every pixel shader here gets its world position, and the result
// lands in a float3. Without a rule for it the inferencer answers "matrix", so nothing narrows.
const MATRIX_SOURCE = `
uniform mat4 m_world;
uniform mat3 m_worldRot;

struct VS_OUTPUT
{
    vec4 Pos;
    vec3 Normal;
};

VS_OUTPUT In;
`;

const matrixScope = collectGlobals(MATRIX_SOURCE);
const matrixStructs = collectStructs(MATRIX_SOURCE);
const matrixType = (expression: string): string | null =>
    inferType(expression, matrixScope, matrixStructs);

describe('inferType, with matrices', () => {
    it('collapses a matrix times a vector to a vector of the matrix dimension', () => {
        assert.equal(matrixType('m_world * In.Pos'), 'vec4');
        assert.equal(matrixType('m_worldRot * In.Normal'), 'vec3');
    });

    it('collapses it the other way round too', () => {
        // `mul(v, m)` translates to `m * v`, but a source may write either.
        assert.equal(matrixType('In.Normal * m_worldRot'), 'vec3');
    });

    it('keeps the matrix when the other operand is a scalar', () => {
        assert.equal(matrixType('m_world * 2.0'), 'mat4');
        assert.equal(matrixType('2.0 * m_world'), 'mat4');
    });

    it('keeps a matrix times a matrix a matrix', () => {
        assert.equal(matrixType('m_world * m_world'), 'mat4');
    });

    it('narrows the assignment the shipped shaders actually make', () => {
        const fixed = fixVectorTruncation(MATRIX_SOURCE + `
vec4 ps_main(VS_OUTPUT In)
{
    vec3 world_pos = (m_world * In.Pos);
    return vec4(world_pos, 1.0);
}
`);

        assert.ok(/vec3 world_pos = \(m_world \* In\.Pos\)\.xyz;/.test(fixed), fixed);
    });
});

describe('fixVectorTruncation, on struct-typed locals', () => {
    // Every pixel shader here fills an output struct declared inside the function. If that local is
    // not in scope, `Out.Tex0` has no type and the whole statement is passed over untouched - so the
    // truncation on its right-hand side never gets fixed.
    const OUTPUT_SOURCE = `
uniform vec4 UVOffset;

struct VS_INPUT
{
    vec2 Tex;
};

struct VS_OUTPUT
{
    vec2 Tex0;
    vec4 Diff;
};

VS_OUTPUT vs_main(VS_INPUT In)
{
    VS_OUTPUT Out;
    Out.Tex0 = In.Tex + UVOffset;
    return Out;
}
`;

    it('narrows an assignment to a field of a local struct', () => {
        const fixed = fixVectorTruncation(OUTPUT_SOURCE);

        assert.ok(/Out\.Tex0 = In\.Tex \+ UVOffset\.xy;/.test(fixed), fixed);
    });
});

describe('fixVectorTruncation, with the unit`s own helpers', () => {
    // `Compute_Fog(float3)` returns a float. Guessing that a call has the type of its first argument
    // - true for the componentwise intrinsics - made this a vec3 and hung a `.x` on a scalar.
    const HELPER_SOURCE = `
struct VS_OUTPUT
{
    vec4 Pos;
    float Fog;
};

float Compute_Fog(vec3 world_pos)
{
    return world_pos.z;
}

VS_OUTPUT vs_main()
{
    VS_OUTPUT Out;
    Out.Fog = Compute_Fog(Out.Pos.xyz);
    return Out;
}
`;

    it('reads a declared return type instead of guessing at one', () => {
        assert.equal(
            inferType('Compute_Fog(p)', new Map(), new Map(), collectFunctions(HELPER_SOURCE)),
            'float');
    });

    it('leaves a scalar-returning call alone', () => {
        const fixed = fixVectorTruncation(HELPER_SOURCE);

        assert.ok(/Out\.Fog = Compute_Fog\(Out\.Pos\.xyz\);/.test(fixed), fixed);
    });
});

describe('fixVectorTruncation, at a call site', () => {
    // HLSL truncates an ARGUMENT to its parameter just as silently as it truncates an assignment,
    // and the tangent-space helpers are declared in float3 while every caller holds float4s.
    const CALL_SOURCE = `
uniform vec4 m_eyePosObj;
uniform vec4 m_light0ObjVector;

struct VS_OUTPUT
{
    vec4 Pos;
    vec3 Half;
};

vec3 Compute_Half(vec3 in_pos, vec3 in_eye, vec3 in_light, mat3 in_to_tangent)
{
    return normalize(in_eye - in_pos) + in_light;
}

VS_OUTPUT vs_main()
{
    VS_OUTPUT Out;
    mat3 to_tangent;
    Out.Half = Compute_Half(Out.Pos, m_eyePosObj, m_light0ObjVector, to_tangent);
    return Out;
}
`;

    it('narrows each argument to its declared parameter type', () => {
        const fixed = fixVectorTruncation(CALL_SOURCE);

        assert.ok(
            /Compute_Half\(Out\.Pos\.xyz, m_eyePosObj\.xyz, m_light0ObjVector\.xyz, to_tangent\)/
                .test(fixed),
            fixed);
    });
});

describe('fixVectorTruncation, on compound assignment', () => {
    // `obj_pos -= extrusion * light_vec;` never matched the statement rewriter, so nothing on its
    // right-hand side was ever balanced.
    it('narrows the right-hand side of a `-=` too', () => {
        const fixed = fixVectorTruncation(`
uniform vec4 m_extrusion;

void vs_main()
{
    vec3 obj_pos;
    vec3 obj_light_vec;
    obj_pos -= m_extrusion * obj_light_vec;
}
`);

        assert.ok(/obj_pos -= m_extrusion\.xyz \* obj_light_vec;/.test(fixed), fixed);
    });
});

describe('fixBooleanConditions', () => {
    // HLSL takes any nonzero number as true. GLSL ES wants a bool and nothing else, and the
    // compile-time switches the techniques pass in - `alpha_ps_main(1)` - arrive as ints.
    const SWITCH_SOURCE = `
uniform vec4 Colour;

void vs_main(int DO_GEOMETRY_MODS, float amount, bool enabled)
{
    if (DO_GEOMETRY_MODS)
    {
        Colour.x = 1.0;
    }
    if (amount)
    {
        Colour.y = 1.0;
    }
    if (enabled)
    {
        Colour.z = 1.0;
    }
}
`;

    it('compares an integer condition against zero', () => {
        assert.ok(/if \(DO_GEOMETRY_MODS != 0\)/.test(fixBooleanConditions(SWITCH_SOURCE)));
    });

    it('compares a float condition against a float zero', () => {
        assert.ok(/if \(amount != 0\.0\)/.test(fixBooleanConditions(SWITCH_SOURCE)));
    });

    it('leaves a condition that is already a bool alone', () => {
        assert.ok(/if \(enabled\)/.test(fixBooleanConditions(SWITCH_SOURCE)));
    });

    it('leaves a comparison alone', () => {
        const source = 'void f(float a)\n{\n    if (a > 0.5)\n    {\n    }\n}\n';

        assert.ok(/if \(a > 0\.5\)/.test(fixBooleanConditions(source)));
    });
});

describe('fixVectorTruncation, on the skinning shaders', () => {
    // Both of these are implicit in HLSL and rejected outright by GLSL, and every one of the eleven
    // RSkin* effects opens with them.
    const SKIN_SOURCE = `
uniform vec4 m_light0Vector;
uniform mat4x3 m_skinMatrixArray[26];

struct VS_INPUT
{
    vec4 Pos;
    vec4 Normal;
};

void vs_main(VS_INPUT In)
{
    int index = In.Normal.w;
    mat3 skin_rotation;
    vec3 obj_light_vec = skin_rotation * m_light0Vector;
}
`;

    it('converts a float into an int explicitly', () => {
        // `int index = In.Normal.w;` - HLSL truncates toward zero, and so does GLSL's int().
        const fixed = fixVectorTruncation(SKIN_SOURCE);

        assert.ok(/int index = int\(In\.Normal\.w\);/.test(fixed), fixed);
    });

    it('narrows a vector against the matrix it multiplies', () => {
        // A mat3 times a float4 is a `mul` HLSL truncates for you.
        const fixed = fixVectorTruncation(SKIN_SOURCE);

        assert.ok(/skin_rotation \* m_light0Vector\.xyz;/.test(fixed), fixed);
    });

    it('leaves a matching scalar assignment alone', () => {
        const fixed = fixVectorTruncation(`
void f()
{
    float a = 1.0;
    int b = 2;
}
`);

        assert.ok(/float a = 1\.0;/.test(fixed), fixed);
        assert.ok(/int b = 2;/.test(fixed), fixed);
    });
});
