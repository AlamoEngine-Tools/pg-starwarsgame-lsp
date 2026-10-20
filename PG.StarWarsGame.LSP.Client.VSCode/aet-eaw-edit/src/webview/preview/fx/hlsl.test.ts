// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Fixtures are written for these tests. The real effects are Petroglyph's and confidential; only
// the constructs they use are reproduced, in code of my own.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { splitArguments, translateHlslBody } from "./hlsl";

describe("splitArguments", () => {
    it("splits at the top level only", () => {
        assert.deepEqual(splitArguments("a, b"), ["a", "b"]);
        assert.deepEqual(splitArguments("f(a, b), c"), ["f(a, b)", "c"]);
    });

    it("is not fooled by indexing", () => {
        assert.deepEqual(splitArguments("m[0], v[1]"), ["m[0]", "v[1]"]);
    });

    it("returns nothing for an empty call", () => {
        assert.deepEqual(splitArguments(""), []);
        assert.deepEqual(splitArguments("   "), []);
    });
});

describe("translateHlslBody", () => {
    it("renames the vector and matrix types", () => {
        const glsl = translateHlslBody(
            "float4 a; float3 b; float3x3 m; half4 h;",
        );

        assert.match(glsl, /vec4 a;/);
        assert.match(glsl, /vec3 b;/);
        assert.match(glsl, /mat3 m;/);
        assert.match(glsl, /vec4 h;/);
    });

    it("does not rename an identifier that merely contains a type name", () => {
        assert.match(
            translateHlslBody("float4 float4Count;"),
            /vec4 float4Count;/,
        );
    });

    it("renames the sampling intrinsics", () => {
        assert.match(translateHlslBody("tex2D(S, uv)"), /texture\(S, uv\)/);
        assert.match(translateHlslBody("texCUBE(S, dir)"), /texture\(S, dir\)/);
    });

    it("strips the float suffix, which GLSL rejects outright", () => {
        const glsl = translateHlslBody("float x = 2.0f * 0.5f + 3f;");

        assert.doesNotMatch(glsl, /[0-9]f\b/);
        assert.match(glsl, /2\.0 \* 0\.5 \+ 3/);
    });

    it("turns saturate into a clamp", () => {
        assert.match(
            translateHlslBody("saturate(x)"),
            /clamp\(x, 0\.0, 1\.0\)/,
        );
    });

    it("turns lerp into mix", () => {
        assert.match(translateHlslBody("lerp(a, b, t)"), /mix\(a, b, t\)/);
    });

    it("reverses the operands of mul, because the two languages disagree on vectors", () => {
        // HLSL rows versus GLSL columns: mul(v, M) is M * v. Backwards, every transform in the
        // shader is transposed and the model comes out mirrored or inside out.
        assert.match(
            translateHlslBody("mul(In.Pos, m_worldViewProj)"),
            /\(m_worldViewProj \* In\.Pos\)/,
        );
    });

    it("handles a mul inside a mul", () => {
        const glsl = translateHlslBody("mul(mul(a, b), c)");

        assert.equal(glsl.replace(/\s+/g, ""), "(c*(b*a))");
    });

    it("handles saturate wrapped around another call", () => {
        assert.match(
            translateHlslBody("saturate(dot(n, l))"),
            /clamp\(dot\(n, l\), 0\.0, 1\.0\)/,
        );
    });

    it("floats an integer literal where GLSL ES will not convert it", () => {
        // ES 1.00 has no implicit int-to-float, so pow(x, 16) is a type error even though HLSL
        // accepts it - and the shipped code writes exactly that for specular exponents.
        assert.match(
            translateHlslBody("pow(ndoth, 16)"),
            /pow\(ndoth, 16\.0\)/,
        );
    });

    it("leaves integers alone where they are meant to be integers", () => {
        // Promoting these would break the loop and the index.
        const glsl = translateHlslBody(
            "for (int i = 0; i < 4; i++) { v = m[2]; }",
        );

        assert.match(glsl, /int i = 0/);
        assert.match(glsl, /i < 4/);
        assert.match(glsl, /m\[2\]/);
    });

    it("drops qualifiers GLSL has no use for", () => {
        assert.doesNotMatch(
            translateHlslBody("static const float k = 1.0;"),
            /\bstatic\b/,
        );
    });

    it("translates a body in the shape the real pixel shaders take", () => {
        const glsl = translateHlslBody(`
float4 example_ps_main(VS_OUTPUT In): COLOR
{
    float4 baseTexel = tex2D(BaseSampler, In.Tex0);
    float3 mixed = lerp(baseTexel.rgb, Colorization * baseTexel.rgb, baseTexel.a);
    float3 normal = 2.0f * (baseTexel.rgb - 0.5f);
    float ndotl = saturate(dot(normal, In.LightVector));
    float3 spec = Specular * pow(ndotl, 16);
    return float4(mixed + spec, In.Diff.a);
}
`);

        assert.match(
            glsl,
            /vec4 baseTexel = texture\(BaseSampler, In\.Tex0\);/,
        );
        assert.match(glsl, /mix\(baseTexel\.rgb/);
        assert.match(glsl, /2\.0 \* \(baseTexel\.rgb - 0\.5\)/);
        assert.match(
            glsl,
            /clamp\(dot\(normal, In\.LightVector\), 0\.0, 1\.0\)/,
        );
        assert.match(glsl, /pow\(ndotl, 16\.0\)/);
        assert.match(glsl, /return vec4\(mixed \+ spec, In\.Diff\.a\);/);
        assert.doesNotMatch(glsl, /\bfloat[234]\b/);
    });

    it("leaves a malformed call alone rather than mangling the rest", () => {
        // A truncated file should cost the translation, not produce plausible-looking nonsense.
        assert.doesNotThrow(() => translateHlslBody("mul(a, b"));
    });
});

describe("brace initialisers", () => {
    // HLSL initialises an aggregate with a brace list; GLSL ES has no such syntax and takes a
    // constructor. Every terrain effect opens with a row of `float4 TexU = { 0.01, 0.0, 0.0, 0.0 };`
    // and SceneBloom with `float BloomIteration = 0;`, an int literal GLSL will not widen.
    it("turns a vector's brace list into a constructor, with float literals", () => {
        const out = translateHlslBody("float4 TexU = { 0.01, 0.0,  0, 1 };\n");

        assert.match(out, /vec4 TexU = vec4\(0\.01, 0\.0, 0\.0, 1\.0\);/);
    });

    it("widens an integer literal initialising a float", () => {
        const out = translateHlslBody(
            "float BloomIteration = 0;\nint Count = 0;\n",
        );

        assert.match(out, /float BloomIteration = 0\.0;/);
        // Not an int: array indices and loop counters stay integers.
        assert.match(out, /int Count = 0;/);
    });

    it("builds a matrix from its brace list of rows, which GLSL reads as columns", () => {
        // `float4x4 m_texOffset = { float4(0.5, 0, 0, 0), ... };` - HLSL lists ROWS. The shaders
        // multiply row vectors (`mul(v, M)`), which the translator writes as `M * v` over the
        // transposed matrix - so HLSL's rows are exactly GLSL's columns, and the constructor
        // takes the list as it stands.
        const out = translateHlslBody(
            "float4x4 m_texOffset =\n{\n    float4(0.5, 0, 0, 0),\n    float4(0, -0.5, 0, 0),\n    float4(0, 0, 1, 0),\n    float4(0.5, 0.5, 0, 1)\n};\n",
        );

        assert.match(
            out,
            /mat4 m_texOffset = mat4\(vec4\(0\.5, 0, 0, 0\), vec4\(0, -0\.5, 0, 0\), vec4\(0, 0, 1, 0\), vec4\(0\.5, 0\.5, 0, 1\)\);/,
        );
    });

    it("chunks a flat brace list into an array constructor", () => {
        // The fog-of-war filter kernel: eight numbers for four float2 taps.
        const out = translateHlslBody(
            "const float2 FilterSamples4[4] =\n{\n   -1,  0,\n    1,  0,\n    0, -1,\n    0,  1\n};\n",
        );

        assert.match(
            out,
            /const vec2 FilterSamples4\[4\] = vec2\[4\]\(vec2\(-1\.0, 0\.0\), vec2\(1\.0, 0\.0\), vec2\(0\.0, -1\.0\), vec2\(0\.0, 1\.0\)\);/,
        );
    });
});

describe("integer vectors and D3DCOLORtoUBYTE4", () => {
    it("renames int4 to ivec4", () => {
        const out = translateHlslBody("int4 indices;\nint2 cell;\n");

        assert.match(out, /ivec4 indices;/);
        assert.match(out, /ivec2 cell;/);
    });

    it("expands D3DCOLORtoUBYTE4 as D3DX defines it", () => {
        // `int4(x.zyxw * 255.001953)` - the swizzle and the scale are the documented expansion,
        // and the extra thousandth keeps a packed 255 from truncating to 254.
        const out = translateHlslBody(
            "int4 indices = D3DCOLORtoUBYTE4(In.BlendIndices);\n",
        );

        assert.match(
            out,
            /ivec4 indices = ivec4\(\(In\.BlendIndices\)\.zyxw \* 255\.001953\);/,
        );
    });
});

describe("integer literals before a line comment", () => {
    it("leaves a preprocessor condition alone", () => {
        // `#if 0 // perspective divide` - the comment's slashes are not a division, and `#if 0.0`
        // is not a preprocessor expression at all. TerrainWater.fx fences a block this way.
        const out = translateHlslBody(
            "#if 0 // perspective divide in vertex shader\nx = 1;\n#endif\nint n = 2; // two\n",
        );

        assert.match(out, /#if 0 \/\/ perspective/);
        assert.match(out, /int n = 2; \/\/ two/);
    });

    it("leaves a for header's integers alone", () => {
        // The guard's `\b` had been committed as a literal backspace byte, so the header was
        // never recognised and `i < count / 2` would have become a float comparison.
        const out = translateHlslBody(
            "for (int i = 0; i < count / 2; i++) { x = x * 2; }\n",
        );

        assert.match(out, /for \(int i = 0; i < count \/ 2; i\+\+\)/);
    });
});

describe("swizzled literals", () => {
    it("turns a swizzle on a number into a constructor", () => {
        // `0.5.xx` is HLSL for a float2 of halves; GLSL reads it as a malformed number.
        const out = translateHlslBody(
            "Out.Tex0 = float2(0.5, -0.5) * In.Pos.xy + 0.5.xx;\nfloat3 one = 1.xxx;\n",
        );

        assert.match(out, /In\.Pos\.xy \+ vec2\(0\.5\);/);
        assert.match(out, /vec3 one = vec3\(1\.0\);/);
    });
});

describe("C-style casts", () => {
    it("broadcasts a scalar, which is how an accumulator is zeroed", () => {
        assert.equal(
            translateHlslBody("float3 sum = (float3)0;").trim(),
            "vec3 sum = vec3(0);",
        );
    });

    it("takes the whole postfix expression, not just the identifier", () => {
        // A cast binds looser than a subscript, so `(float3)m[2]` casts the ROW. Stopping at the
        // identifier produces `vec3(m)[2]`, which compiles nowhere and would mean something else.
        assert.equal(
            translateHlslBody(
                "float3 view = (float3)m_worldViewInv[2];",
            ).trim(),
            "vec3 view = vec3(m_worldViewInv[2]);",
        );
    });

    it("keeps a swizzle after a subscript inside the cast", () => {
        assert.equal(
            translateHlslBody("float2 uv = (float2)bones[i].xy;").trim(),
            "vec2 uv = vec2(bones[i].xy);",
        );
    });
});

describe("non-square matrices", () => {
    it("pads a float4x3 to a mat4, which three.js can actually upload", () => {
        // GLSL ES 3.00 has mat4x3, but three has no uniform setter for a non-square matrix at all -
        // its table stops at 0x8b62 and never mentions 4x3 - so declaring one throws on upload.
        assert.equal(
            translateHlslBody("float4x3 m_skinMatrixArray[26];").trim(),
            "mat4 m_skinMatrixArray[26];",
        );
    });

    it("leaves the square matrices alone", () => {
        assert.equal(translateHlslBody("float3x3 m;").trim(), "mat3 m;");
        assert.equal(translateHlslBody("float4x4 m;").trim(), "mat4 m;");
        assert.equal(translateHlslBody("float2x2 m;").trim(), "mat2 m;");
    });
});

describe("integer literals in arithmetic", () => {
    /**
     * `Nebula.fx` is the case: `In.Tex + m_time * UVScrollRate * 3` and `pow(z, 8.0) * 2 + 0.1`.
     * HLSL promotes the 3 and the 2; GLSL ES has no implicit int-to-float conversion at all, so both
     * are type errors and the whole effect fails to compile - which draws nothing.
     */
    it("promotes a literal multiplied by something", () => {
        assert.match(translateHlslBody("float2 t = uv * 3;"), /uv \* 3\.0/);
    });

    it("promotes a literal on the left of a multiply", () => {
        assert.match(translateHlslBody("float x = 2 * y;"), /2\.0 \* y/);
    });

    it("promotes around a divide as well", () => {
        assert.match(translateHlslBody("float x = y / 4;"), /y \/ 4\.0/);
    });

    it("leaves an array index alone", () => {
        // `bones[3.0]` is a type error in the other direction, and the subscript is genuinely an
        // integer.
        assert.match(translateHlslBody("float4 p = bones[3];"), /bones\[3\]/);
    });

    it("leaves a loop counter alone", () => {
        // `i * 2` inside an integer loop must stay integer. Only ADJACENCY to an operator is not
        // enough to know a literal is float, so the rule deliberately skips anything in a for
        // header and anything multiplied by a declared int.
        const out = translateHlslBody(
            "for (int i = 0; i < 4; i++) { total += i; }",
        );

        assert.match(out, /i < 4;/);
        assert.doesNotMatch(out, /4\.0/);
    });

    it("leaves a float literal as it is", () => {
        assert.match(translateHlslBody("float x = y * 3.5;"), /y \* 3\.5/);
    });
});
