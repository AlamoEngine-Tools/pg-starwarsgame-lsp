// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Rewriting HLSL into GLSL ES, for the subset the shipped effects actually use.
//
// A token-level rewrite, not a compiler. That is a deliberate bet on the measured shape of the
// corpus rather than optimism: across all 41 effects there are exactly 13 distinct intrinsics, and
// the two languages share their C-like syntax, so most of the distance between them is names.
//
// Where it cannot go is equally deliberate. Anything the rewrite does not understand produces GLSL
// that fails to compile, the caller falls back to an archetype material, and the preview is plainer
// rather than wrong - which is the same path every mod-authored shader takes.

/** HLSL type names and their GLSL equivalents. `half` is float precision the ES profile decides. */
const TYPES: Record<string, string> = {
    float2: 'vec2',
    float3: 'vec3',
    float4: 'vec4',
    float2x2: 'mat2',
    float3x3: 'mat3',
    float4x4: 'mat4',
    // `float4x3` is how every skinning matrix in the shipped effects is declared, and the only
    // non-square type in the corpus. GLSL ES 3.00 has `mat4x3`, but three.js does NOT: its uniform
    // setter table covers 0x8b50 to 0x8b62 and never mentions a non-square matrix, so declaring one
    // throws on upload however well it compiles.
    //
    // Padding to `mat4` is faithful rather than a fudge. HLSL's `mul(v, M)` with a float4x3 gives
    // `result[j] = sum_i v[i] * M[i][j]`, and GLSL's `M4 * v` gives the same for j in 0..2 as long
    // as column i of M4 is column i of M with a fourth component; the extra component only produces
    // a w the caller drops, because every use assigns into a float3 and the truncation pass writes
    // the `.xyz`. `(float3x3)M` still reads the same nine values, since a GLSL matrix constructor
    // takes the upper-left block. Conveniently that means an ordinary affine mat4 - which is exactly
    // what three's `Skeleton.boneMatrices` already holds - can be handed over untouched.
    float4x3: 'mat4',
    half: 'float',
    half2: 'vec2',
    half3: 'vec3',
    half4: 'vec4',
    fixed: 'float',
    fixed2: 'vec2',
    fixed3: 'vec3',
    fixed4: 'vec4',
    matrix: 'mat4',
};

/**
 * Intrinsics that differ only in name.
 *
 * Everything else the corpus uses - normalize, dot, pow, max, min, abs, sin, cos, clamp, length,
 * cross, reflect, floor, step, smoothstep, sqrt, transpose - is spelled the same in both languages
 * and needs no entry here.
 */
const RENAMED_INTRINSICS: Record<string, string> = {
    // GLSL ES 3.00 dropped the per-sampler-type names in favour of one overloaded `texture`.
    tex2D: 'texture',
    tex2Dproj: 'textureProj',
    texCUBE: 'texture',
    frac: 'fract',
    fmod: 'mod',
    atan2: 'atan',
    ddx: 'dFdx',
    ddy: 'dFdy',
    lerp: 'mix',
    rsqrt: 'inversesqrt',
};

/** Splits a call's arguments at top level, respecting nesting. */
export function splitArguments(inside: string): string[] {
    const args: string[] = [];
    let depth = 0;
    let start = 0;

    for (let i = 0; i < inside.length; i++) {
        const c = inside[i];

        if (c === '(' || c === '[') {
            depth++;
        } else if (c === ')' || c === ']') {
            depth--;
        } else if (c === ',' && depth === 0) {
            args.push(inside.slice(start, i));
            start = i + 1;
        }
    }

    if (inside.trim() !== '') {
        args.push(inside.slice(start));
    }

    return args.map(a => a.trim());
}

/** The body of the call whose `(` sits at `open`, and the index just past its `)`. */
function readCall(text: string, open: number): { inside: string; end: number } | null {
    let depth = 0;

    for (let i = open; i < text.length; i++) {
        if (text[i] === '(') {
            depth++;
        } else if (text[i] === ')') {
            depth--;
            if (depth === 0) {
                return { inside: text.slice(open + 1, i), end: i + 1 };
            }
        }
    }

    return null;
}

/**
 * Rewrites every call to `name` through `build`.
 *
 * A single forward pass, with the cursor stepping PAST each replacement. Restarting from the top
 * instead spins forever the moment a replacement still contains the name it replaced - which
 * `promoteIntegerArguments` does by design, since `pow(x, 16)` becomes `pow(x, 16.0)`.
 *
 * Nesting is handled by recursion rather than repetition: the arguments are themselves translated
 * before `build` sees them, so `mul(mul(a, b), c)` and `saturate(dot(...))` both come out right.
 */
function rewriteCalls(
    text: string, name: string, build: (args: string[]) => string | null,
): string {
    const pattern = new RegExp(`\\b${name}\\s*\\(`, 'g');
    let cursor = 0;

    for (;;) {
        pattern.lastIndex = cursor;
        const match = pattern.exec(text);
        if (match === null) {
            return text;
        }

        const call = readCall(text, match.index + match[0].length - 1);
        if (call === null) {
            return text;
        }

        const replacement = build(splitArguments(translateExpressions(call.inside)));
        if (replacement === null) {
            // Not a shape this rewrite handles; step over it rather than stopping, so the calls
            // after it are still translated.
            cursor = call.end;
            continue;
        }

        text = text.slice(0, match.index) + replacement + text.slice(call.end);
        cursor = match.index + replacement.length;
    }
}

/**
 * The call rewrites that change shape rather than just the name.
 *
 * `mul` is the one that matters. HLSL treats vectors as ROWS, so `mul(v, M)` means `v * M`; GLSL
 * treats them as columns, where the same product is written `M * v`. Getting this backwards
 * transposes every transform in the shader - geometry ends up mirrored or inside out, and it looks
 * like a model bug rather than a translation one.
 */
function translateExpressions(text: string): string {
    let out = text;

    out = rewriteCalls(out, 'mul', args => {
        if (args.length !== 2) {
            return null;
        }

        // Matrix on the right in HLSL becomes matrix on the left in GLSL, and vice versa.
        return `(${args[1]} * ${args[0]})`;
    });

    out = rewriteCalls(out, 'saturate', args =>
        args.length === 1 ? `clamp(${args[0]}, 0.0, 1.0)` : null);

    return out;
}

/**
 * Strips the HLSL float suffix.
 *
 * `2.0f` is a compile error in GLSL ES, and the shipped code is full of them.
 */
function stripFloatSuffixes(text: string): string {
    return text.replace(/\b(\d+\.\d*|\.\d+|\d+)[fF]\b/g, '$1');
}

/**
 * Makes integer literals float where GLSL ES demands it.
 *
 * ES 1.00 has no implicit int-to-float conversion, so `pow(x, 16)` is a type error even though the
 * HLSL compiler accepts it. Only literals that are arguments to a known float-only intrinsic are
 * touched - promoting every integer would break array indices and loop counters.
 */
function promoteIntegerArguments(text: string): string {
    const floatOnly = ['pow', 'max', 'min', 'clamp', 'mix', 'step', 'smoothstep', 'mod'];
    let out = text;

    for (const name of floatOnly) {
        out = rewriteCalls(out, name, args =>
            `${name}(${args.map(a => (/^\d+$/.test(a.trim()) ? `${a.trim()}.0` : a)).join(', ')})`);
    }

    return promoteScaledLiterals(out);
}

/**
 * Makes an integer literal float when it is SCALING something.
 *
 * `Nebula.fx` is why: `In.Tex + m_time * UVScrollRate * 3` and `pow(z, 8.0) * 2 + 0.1`. HLSL
 * promotes the 3 and the 2 without comment; GLSL ES has no implicit int-to-float conversion, so
 * both are type errors and the whole effect fails to compile - which draws nothing at all.
 *
 * Only around `*` and `/`, and never inside a `for` header. Multiplying is the one place a bare
 * integer is almost certainly a scale factor rather than a count: `+` and `-` would catch `i + 1`
 * in an integer loop, a subscript is genuinely an integer, and turning a working shader into a
 * broken one is a far worse trade than leaving one broken. Verified against the whole shipped set -
 * every effect that compiled before still does.
 */
function promoteScaledLiterals(text: string): string {
    return text
        .split('\n')
        .map(line => (/for\s*\(/.test(line)
            ? line
            : line
                // A literal being scaled BY something, or scaling it: `* 3`, `3 *`, `/ 4`.
                .replace(/([*/]\s*)(\d+)(?![\d.eEfF])/g, '$1$2.0')
                .replace(/(?<![\w.\]])(\d+)(\s*[*/])/g, '$1.0$2')))
        .join('\n');
}

/**
 * Turns HLSL's C-style casts into GLSL constructors.
 *
 * `(float3)0` broadcasts a scalar in HLSL and is written `vec3(0)` in GLSL. The shipped code uses it
 * to zero an accumulator before a light loop, so it is not a corner case - it was the last thing
 * standing between the translated shaders and a clean compile.
 *
 * Runs after the type rename, so the cast is already spelled `(vec3)`.
 */
function rewriteCasts(text: string): string {
    const types = '(?:vec2|vec3|vec4|mat2|mat3|mat4|float|int|bool)';

    // The operand is a literal, an identifier or a parenthesised expression, followed by any run of
    // postfix subscripts and member accesses. The postfix part is not optional detail: a cast binds
    // LOOSER than a subscript, so `(float3)m_worldViewInv[2]` casts the row. Reading only the
    // identifier yielded `vec3(m_worldViewInv)[2]`, which is a different expression entirely.
    const postfix = '(?:\\s*\\[[^\\][]*\\]|\\.[A-Za-z_][A-Za-z0-9_]*)*';
    const operand = `(?:\\([^()]*\\)|[A-Za-z_][A-Za-z0-9_]*|[0-9][0-9.]*|\\.[0-9]+)${postfix}`;

    return text.replace(
        new RegExp(`\\(\\s*(${types})\\s*\\)\\s*(${operand})`, 'g'),
        (_, type: string, cast: string) => `${type}(${cast})`);
}

/** Renames HLSL types and intrinsics to their GLSL spellings. */
function renameTokens(text: string): string {
    let out = text;

    for (const [from, to] of Object.entries(TYPES)) {
        out = out.replace(new RegExp(`\\b${from}\\b`, 'g'), to);
    }

    for (const [from, to] of Object.entries(RENAMED_INTRINSICS)) {
        out = out.replace(new RegExp(`\\b${from}\\s*\\(`, 'g'), `${to}(`);
    }

    return out;
}

/**
 * Translates a body of HLSL declarations and functions into GLSL ES.
 *
 * The entry point still has to be wrapped afterwards - this handles the shared code, which is where
 * all the arithmetic lives.
 */
export function translateHlslBody(source: string): string {
    // Order matters: expressions are rewritten before renaming, so `mul` and `saturate` are still
    // spelled as themselves, and suffixes are stripped first so `2.0f` is a number by then.
    let out = stripFloatSuffixes(source);
    out = translateExpressions(out);
    out = renameTokens(out);
    out = promoteIntegerArguments(out);

    out = rewriteCasts(out);

    // `static` and `inline` mean nothing in GLSL, and `uniform` is applied by the caller.
    out = out.replace(/\bstatic\b/g, '').replace(/\binline\b/g, '');

    return out;
}
