// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Assembling a compilable GLSL ES 3.00 vertex and fragment shader out of translated HLSL.
//
// Translating the arithmetic is only half the job. HLSL entry points take and return structs and
// carry their meaning in semantics; GLSL ES has `main()`, interface variables of basic types only,
// a declared fragment output, and `gl_Position`. This bridges the two.

import { translateHlslBody } from './hlsl';
import { bindField } from './attributes';
import {
    fillEmptyFunctions, fixBooleanConditions, fixReturnWidths, fixVectorTruncation, zeroFor,
    zeroStructCasts,
} from './truncation';

/** A struct declared in the source, and the fields it carries. */
export interface HlslStruct {
    name: string;
    fields: { type: string; name: string; semantic?: string }[];
}

/** What came out, and what stopped it if nothing did. */
export interface GlslProgram {
    source: string | null;
    /** Why translation was refused, in words. Null when it produced something. */
    refusal: string | null;
}

/** Finds every `struct Name { ... };` in the source. */
export function findStructs(source: string): HlslStruct[] {
    const structs: HlslStruct[] = [];

    for (const match of source.matchAll(/\bstruct\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{([^}]*)\}\s*;/g)) {
        const fields: HlslStruct['fields'] = [];

        for (const field of match[2].matchAll(
            /\b([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([A-Za-z_][A-Za-z0-9_]*))?\s*;/g)) {
            fields.push({ type: field[1], name: field[2], semantic: field[3] });
        }

        structs.push({ name: match[1], fields });
    }

    return structs;
}

/** The entry function's signature: what it returns, and the struct it takes. */
export interface EntrySignature {
    returnType: string;
    parameterType: string;
    parameterName: string;
    /** The semantic on the function itself, e.g. `COLOR`. */
    semantic?: string;
}

/** A `compile` handle: the function it names, and the constants it specialises it with. */
export interface ShaderHandle {
    entry: string;
    /** Compile-time arguments, e.g. the `1` in `compile ps_1_1 alpha_ps_main( 1 )`. */
    args: string[];
}

/**
 * Resolves what a technique's `PixelShader = (name)` actually refers to.
 *
 * A technique names a HANDLE, not a function: `PixelShader = (alpha_ps_main_bin);`, with the effect
 * separately declaring `pixelshader alpha_ps_main_bin = compile ps_1_1 alpha_ps_main( 1 );`. Taking
 * the handle for the function name finds nothing - every one of the 41 effects failed exactly that
 * way until a real GLSL compiler was asked.
 *
 * The arguments matter too: the same function is compiled twice with different constants, which is
 * how one shader serves both the fog-of-war case and the plain one.
 */
export function resolveShaderHandle(source: string, name: string): ShaderHandle {
    // Case-insensitive: FX keywords are, and the sources are not consistent - one file of the
    // forty-one writes `PixelShader` where the rest write `pixelshader`.
    const pattern = new RegExp(
        `\\b(?:pixelshader|vertexshader)\\s+${name}\\s*=\\s*compile\\s+`
        + '[A-Za-z_0-9]+\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\(([^)]*)\\)', 'i');

    const match = pattern.exec(source);
    if (match === null) {
        // Not a handle: some effects name the function directly.
        return { entry: name, args: [] };
    }

    return {
        entry: match[1],
        args: match[2].split(',').map(arg => arg.trim()).filter(arg => arg !== ''),
    };
}

export function findEntry(source: string, entryName: string): EntrySignature | null {
    const pattern = new RegExp(
        `\\b([A-Za-z_][A-Za-z0-9_]*)\\s+${entryName}\\s*\\(([^)]*)\\)`
        + '\\s*(?::\\s*([A-Za-z_][A-Za-z0-9_]*))?\\s*\\{');

    const match = pattern.exec(source);
    if (match === null) {
        return null;
    }

    // The first parameter is the struct; any others are `uniform` constants the compile call fixes.
    const first = match[2].split(',')[0]?.trim() ?? '';
    const parts = first.replace(/\buniform\b/, '').trim().split(/\s+/);

    if (parts.length < 2) {
        return null;
    }

    return {
        returnType: match[1],
        parameterType: parts[0],
        parameterName: parts[1],
        semantic: match[3],
    };
}

/**
 * Removes the effect-framework scaffolding that GLSL has no idea about.
 *
 * An `.fx` file is not only shader code: it carries string annotations, technique and pass blocks,
 * `compile` declarations and semantics, none of which are a language GLSL speaks. Left in, the
 * compiler stops at the first `string` - which is exactly what it did, 27 times out of 29.
 */
export function stripFxScaffolding(source: string): string {
    let out = source;

    // `technique name < ... > { ... }` - brace-matched, because passes nest inside.
    out = removeBlocks(out, /\btechnique\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:<[^>]*>)?\s*/g);

    // Handle declarations; the entry function itself stays.
    out = out.replace(
        /\b(?:pixelshader|vertexshader)\s+[A-Za-z_0-9]+\s*=\s*compile\b[^;]*;/gi, '');

    // Only `#include`. Everything else the sources use - eight `#define`s and a handful of `#if`
    // blocks - is the same preprocessor GLSL ES has, so it is kept and left to do its job; removing
    // it took `GEOMETRY_MODS_ENABLED` out from under the one shader that reads it. The two defines
    // that expand to state-block syntax are only ever used inside a pass, which the technique
    // removal above has already taken away, so their bodies are never expanded.
    //
    // An `#include` should be gone by now. One still standing means the header did not resolve, and
    // leaving it in turns that into a plain compile error instead of a silently missing header.
    out = out.replace(/^[ \t]*#include[^\n]*$/gm, '');

    // State blocks, in both shapes the sources use: the one `SB_START`/`SB_END` expands to inside a
    // pass, and a named top-level declaration. Both hold render state, which chunk 17 reads from the
    // manifest - by here it is scaffolding.
    out = out.replace(
        /\b(?:StateBlock|stateblock\s+[A-Za-z_][A-Za-z0-9_]*)\s*=\s*stateblock_state\s*\{[^}]*\}\s*;?/g,
        '');

    // String annotations, at top level and inside declarations. Word-anchored, so an identifier
    // that merely ends in "string" is left alone.
    out = out.replace(/\bstring\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*"[^"]*"\s*;/g, '');

    // Any remaining `< ... >` annotation block attached to a declaration.
    out = out.replace(/<[^<>{}]*>\s*;/g, ';');

    // Semantics on a function signature: `) : COLOR {` becomes `) {`.
    out = out.replace(/\)\s*:\s*[A-Za-z_][A-Za-z0-9_]*\s*(\{)/g, ') $1');

    // Semantics on struct fields.
    out = out.replace(/\s*:\s*[A-Za-z_][A-Za-z0-9_]*\s*;/g, ';');

    // Semantics on function PARAMETERS, which end at a comma or the closing paren rather than a
    // semicolon: `vs_main(vec4 Pos : POSITION, vec2 Tex : TEXCOORD0)`.
    out = out.replace(/\s*:\s*[A-Za-z_][A-Za-z0-9_]*\s*([,)])/g, '$1');

    // `uniform` on a PARAMETER. In HLSL that marks a compile-time constant, which the technique
    // supplies in its `compile` call - `alpha_ps_main( 1 )`. Those arguments are already carried
    // through to the call site by `resolveShaderHandle`, so the parameter itself stays and only the
    // qualifier goes; GLSL rejects it in a signature. Nothing else here says `uniform`: the globals
    // do not get theirs until `declareUniforms`, which runs after this.
    out = out.replace(/([(,]\s*)uniform\s+/g, '$1');

    return out;
}

/**
 * Makes the vertex-packing helpers do nothing, because our attributes arrive already unpacked.
 *
 * The RSkin vertex formats store UVs and normals as SCALED SHORTS - `Unpack_UV` divides by 4096,
 * `Unpack_Normal` by 16384 - and the shaders call them on the way in. The ALO reader decodes those
 * fields when it builds the glTF, so by the time the attribute reaches this shader it is already a
 * real UV and a real unit normal. Leaving the division in place shrinks every UV to a 4096th of
 * itself: the whole mesh samples a single texel and draws as one flat colour, which is exactly how
 * the skinned models looked.
 *
 * The BODIES are rewritten rather than the call sites, so the translated source still reads the way
 * the original does and the reason lives in one place.
 */
export function neutralisePacking(source: string): string {
    return source.replace(
        /(vec[234])\s+(Unpack_UV|Unpack_Normal)\s*\(\s*vec[234]\s+([A-Za-z_][A-Za-z0-9_]*)\s*\)\s*\{[^}]*\}/g,
        (_, type: string, name: string, parameter: string) =>
            `${type} ${name}(${type} ${parameter}) { return ${parameter}; }`);
}

/** Removes each match together with the brace block that follows it. */
function removeBlocks(text: string, header: RegExp): string {
    let out = '';
    let cursor = 0;

    header.lastIndex = 0;
    for (;;) {
        header.lastIndex = cursor;
        const match = header.exec(text);
        if (match === null) {
            return out + text.slice(cursor);
        }

        const open = text.indexOf('{', match.index + match[0].length - 1);
        if (open === -1) {
            return out + text.slice(cursor);
        }

        let depth = 0;
        let close = -1;
        for (let i = open; i < text.length; i++) {
            if (text[i] === '{') {
                depth++;
            } else if (text[i] === '}') {
                depth--;
                if (depth === 0) { close = i; break; }
            }
        }

        if (close === -1) {
            return out + text.slice(cursor);
        }

        out += text.slice(cursor, match.index);
        cursor = close + 1;
    }
}

/**
 * Turns HLSL's implicitly-uniform globals into declared GLSL uniforms.
 *
 * A top-level `float4x4 m_worldViewProj : WORLDVIEWPROJECTION;` is a uniform in HLSL simply by being
 * global. GLSL requires it said out loud. Samplers carry a `sampler_state` block that has no GLSL
 * equivalent and is dropped; the `texture` declarations they refer to are dropped with it, since the
 * sampler is what the shader actually reads.
 */
export function declareUniforms(source: string): string {
    let out = source;

    // `sampler Name = sampler_state { ... };` becomes a plain sampler uniform.
    out = out.replace(
        /\bsampler(2D|CUBE)?\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*sampler_state\s*\{[^}]*\}\s*;/g,
        (_, kind: string | undefined, name: string) =>
            `uniform ${kind === 'CUBE' ? 'samplerCube' : 'sampler2D'} ${name};`);

    // `texture Name;`, `texture Name < ... >;` and `texture Name : SEMANTIC;` - a texture OBJECT,
    // which GLSL has no counterpart for. The sampler that reads it is what becomes a uniform. The
    // engine's own three take the semantic form, and the sky cube carries BOTH a semantic and an
    // annotation - so the two are separately optional rather than alternatives. Left standing, each
    // one is a plain syntax error.
    out = out.replace(
        /\btexture\s+[A-Za-z_][A-Za-z0-9_]*\s*(?::\s*[A-Za-z_][A-Za-z0-9_]*\s*)?(?:<[^>]*>\s*)?;/g,
        '');

    return declareSemanticUniforms(out);
}

/**
 * Declares every global that carries an engine semantic.
 *
 * Written as a scan rather than one regex because the declarations are not one shape: many carry a
 * DEFAULT VALUE as well as a semantic, sometimes an array, sometimes spilling over several lines -
 * `vec4 m_lightAmbient : GLOBAL_AMBIENT = {0.2, 0.2, 0.2, 1.0};` and
 * `mat4 m_sphAll[3] : SPH_LIGHT_ALL = ...`. GLSL ES uniforms cannot be initialised at all, so the
 * default is dropped along with the semantic - but it is not thrown away: `collectUniformDefaults`
 * reads the same declarations and the material seeds each uniform with what the header said, which
 * is what anything the preview does not feed per frame ends up carrying.
 */
function declareSemanticUniforms(source: string): string {
    // Either form marks a global: a `: SEMANTIC`, or a `< ... >` annotation block. Requiring one
    // of the two is what keeps ordinary local variables out - they have neither, and turning a
    // local into a uniform would silently change what the shader computes.
    // Unindented only. A global in these headers starts at column 0, and requiring that is what
    // keeps locals out - `if (a < b ...)` inside a function body matched the annotation alternative
    // and turned a local into a uniform, which GLSL rejects as "only allowed at global scope".
    const declaration = new RegExp(
        '^()((?:float|vec|mat|int|bool|ivec|bvec)[0-9x]*)[ \\t]+'
        + '([A-Za-z_][A-Za-z0-9_]*)(\\s*\\[[^\\]]*\\])?\\s*'
        + '(?::\\s*[A-Za-z_][A-Za-z0-9_]*|<[^>]*>)',
        'gm');

    let out = '';
    let cursor = 0;

    for (;;) {
        declaration.lastIndex = cursor;
        const match = declaration.exec(source);
        if (match === null) {
            return out + source.slice(cursor);
        }

        // Everything up to the terminating semicolon belongs to this declaration, initialiser
        // included - which is why this cannot be a single replace.
        const semicolon = source.indexOf(';', match.index + match[0].length);
        if (semicolon === -1) {
            return out + source.slice(cursor);
        }

        const [, indent, type, name, subscript] = match;

        out += source.slice(cursor, match.index);
        out += `${indent}uniform ${type} ${name}${subscript ?? ''};`;
        cursor = semicolon + 1;
    }
}

/** The name of the fragment output. Prefixed so it cannot collide with anything in a shader. */
const FRAGMENT_OUTPUT = 'aet_fragColour';

/**
 * The uniform carrying the pass's `AlphaRef`, as a 0..1 threshold.
 *
 * Named here rather than spelled out at each use because the material sets it by this name and the
 * shader reads it by this name, and a mismatch is a silent no-op: the uniform simply never binds.
 */
export const ALPHA_TEST_UNIFORM = 'aet_alphaTest';

/** A threshold no fragment can fall to or below, which is how "no alpha test" is expressed. */
export const ALPHA_TEST_OFF = -1;

/**
 * `highp`, in BOTH stages.
 *
 * `mediump` was the obvious thing to write and it silently broke every shader. A fragment shader
 * has no default float precision and must declare one, but a VERTEX shader defaults to highp - so
 * declaring mediump there DOWNGRADES it, and a world-view-projection multiply at fp16 puts the
 * geometry nowhere. It cost a long hunt precisely because nothing fails: every draw call issues,
 * GL reports no error, and the screen is simply empty. World coordinates reach thousands of units
 * on a capital ship, so the fragment side takes highp too rather than lose world-space lighting.
 */
const PRECISION = 'precision highp float;\nprecision highp int;';

/** A struct field carried across the stage boundary, with its type already in GLSL spelling. */
interface StageField {
    name: string;
    glslType: string;
    semantic?: string;
}

/**
 * Everything both stages need out of the source, or the reason neither can be built.
 *
 * The two builders differ only in scaffolding: the same handle resolution, the same entry lookup,
 * the same pass pipeline, the same struct flattening.
 */
interface TranslationUnit {
    handle: ShaderHandle;
    entry: EntrySignature;
    struct: HlslStruct;
    fields: StageField[];
    body: string;
}

/**
 * Non-square types with no faithful stand-in.
 *
 * three.js has no uniform setter for any non-square matrix, so `mat4x3` and friends throw on upload
 * whatever GLSL ES 3.00 allows. `float4x3` is padded to `mat4` instead - see the note in `hlsl.ts`,
 * where that is shown to compute the same thing - and it is the only one the shipped effects use.
 * The rest have no such padding, so a shader using one keeps its archetype.
 */
const UNSUPPORTED_TYPES = ['float3x4', 'float4x2', 'float2x4', 'float3x2', 'float2x3'];

function translateUnit(hlsl: string, handleName: string): TranslationUnit | GlslProgram {
    const unsupported = UNSUPPORTED_TYPES.find(type => new RegExp(`\\b${type}\\b`).test(hlsl));
    if (unsupported !== undefined) {
        return {
            source: null,
            refusal: `Uses ${unsupported}, and three.js cannot upload a non-square matrix uniform.`,
        };
    }

    const handle = resolveShaderHandle(hlsl, handleName);

    const entry = findEntry(hlsl, handle.entry);
    if (entry === null) {
        return { source: null, refusal: `No entry point named '${handle.entry}'.` };
    }

    const struct = findStructs(hlsl).find(s => s.name === entry.parameterType);
    if (struct === undefined) {
        return {
            source: null,
            refusal: `The entry point takes '${entry.parameterType}', which is not a struct `
                + 'declared here.',
        };
    }

    // Order matters. The type-driven passes come last, because they need the GLSL type names and
    // the uniform declarations to be in place before they can work out how wide anything is.
    const passes = [
        // declareUniforms BEFORE stripFxScaffolding: it finds the engine globals by their
        // `: SEMANTIC` annotation, and the strip is what removes those. The other way round every
        // one of them stayed a plain global - zero-initialised, so every shader multiplied its
        // position by a zero matrix and drew nothing, while compiling and linking perfectly.
        translateHlslBody, declareUniforms, stripFxScaffolding, neutralisePacking,
        zeroStructCasts, fixVectorTruncation, fixReturnWidths, fixBooleanConditions,
        fillEmptyFunctions,
    ];

    return {
        handle,
        entry,
        struct,
        fields: struct.fields.map(field => ({
            name: field.name,
            semantic: field.semantic,
            glslType: translateHlslBody(field.type).trim(),
        })),
        body: passes.reduce((text, pass) => pass(text), hlsl),
    };
}

function isRefusal(unit: TranslationUnit | GlslProgram): unit is GlslProgram {
    return 'refusal' in unit;
}

/** The field that drives `gl_Position` - and travels in it rather than as a varying. */
function isPosition(field: StageField): boolean {
    return field.semantic?.toUpperCase() === 'POSITION';
}

/**
 * Builds a fragment shader around a translated body.
 *
 * Targets GLSL ES 3.00, which is what actually runs: three.js asks for a `webgl2` context and
 * throws if it cannot have one. Targeting 1.00 meant refusing all eleven skinned effects over
 * `float4x3`, a limitation lifted in 3.00 - so the refusal was for a constraint never in force.
 *
 * The input struct is flattened into one `in` per field, because an interface variable must be a
 * basic type or a block - `in VS_OUTPUT` will not compile however natural it looks.
 */
export function buildFragmentShader(hlsl: string, handleName: string): GlslProgram {
    const unit = translateUnit(hlsl, handleName);
    if (isRefusal(unit)) {
        return unit;
    }

    const { handle, entry, struct, fields, body } = unit;

    // Everything except the POSITION field, which the vertex stage delivers through `gl_Position`
    // rather than as a varying. Measured across the corpus: all thirty pixel entries take a struct
    // carrying one and not one of them reads it, which is what D3D9 requires - ps_2_0 cannot read
    // POSITION at all. Declaring it here would leave the linker looking for a varying that the
    // vertex shader has no business writing.
    const carried = fields.filter(field => !isPosition(field));

    const inputs = carried
        .map(field => `in ${field.glslType} v_${field.name};`)
        .join('\n');

    const assignments = carried
        .map(field => `    ${entry.parameterName}.${field.name} = v_${field.name};`)
        .join('\n');

    const source = [
        // The version directive must be the very first line, ahead of even a comment.
        '#version 300 es',
        PRECISION,
        '',
        inputs,
        `out vec4 ${FRAGMENT_OUTPUT};`,
        '',
        // The alpha test, which is render STATE rather than anything the pixel shader says - but a
        // RawShaderMaterial gets none of three's built-in chunks, so `material.alphaTest` is inert
        // here and the discard has to live in the shader. Fed by `applyStoredShaderState`, and left
        // at a threshold nothing can fall below when the effect declares no test: an additive glow
        // is meant to draw at zero alpha, and discarding on it would put the effect out.
        `uniform float ${ALPHA_TEST_UNIFORM};`,
        '',
        body,
        '',
        'void main()',
        '{',
        `    ${struct.name} ${entry.parameterName};`,
        assignments,
        `    ${FRAGMENT_OUTPUT} = ${handle.entry}(`
            + [entry.parameterName, ...handle.args].join(', ') + ');',
        '',
        // `AlphaFunc = Greater` in every alpha-tested effect the game ships, so the fragment
        // survives only above the reference.
        `    if (${FRAGMENT_OUTPUT}.a <= ${ALPHA_TEST_UNIFORM}) { discard; }`,
        '}',
        '',
    ].join('\n');

    return { source, refusal: null };
}

/**
 * Builds a vertex shader around a translated body.
 *
 * Mirror image of the fragment side. The entry's parameter struct becomes attributes and its RETURN
 * struct becomes the outputs - which have to be named exactly as the fragment shader names its
 * inputs, `v_<field>`, or the two stages will not link.
 *
 * The field carrying the POSITION semantic drives `gl_Position` and is NOT also declared as an
 * output: the clip-space position travels in the built-in, and re-declaring it as `v_Pos` would
 * leave the fragment shader reading an interpolated clip position that nothing writes usefully.
 */
export function buildVertexShader(hlsl: string, handleName: string): GlslProgram {
    const unit = translateUnit(hlsl, handleName);
    if (isRefusal(unit)) {
        return unit;
    }

    const { handle, entry, struct, fields, body } = unit;

    const outputStruct = findStructs(hlsl).find(s => s.name === entry.returnType);
    if (outputStruct === undefined) {
        return {
            source: null,
            refusal: `The entry point returns '${entry.returnType}', which is not a struct `
                + 'declared here.',
        };
    }

    const outputs: StageField[] = outputStruct.fields.map(field => ({
        name: field.name,
        semantic: field.semantic,
        glslType: translateHlslBody(field.type).trim(),
    }));

    const position = outputs.find(isPosition);
    if (position === undefined) {
        return {
            source: null,
            refusal: `'${entry.returnType}' declares no POSITION field, so there is nothing to `
                + 'drive gl_Position with.',
        };
    }

    const carried = outputs.filter(field => !isPosition(field));

    // The input struct is filled from the attributes three actually binds, under the names it gives
    // them. Declaring `a_Pos` instead would bind to nothing and draw an empty screen.
    const skinned = /\bm_skinMatrixArray\b|\bSKINMATRIXARRAY\b/.test(hlsl);
    const attributes = new Map<string, string>();
    const fills: string[] = [];

    for (const field of fields) {
        const bound = bindField(field.semantic, field.glslType, skinned);
        if (bound === null) {
            // No attribute carries this - a per-vertex fog value, say, which the engine computes.
            // The field keeps its zero rather than the shader failing to build.
            continue;
        }

        for (const read of bound.reads) {
            attributes.set(read.name, read.glslType);
        }
        fills.push(`    ${entry.parameterName}.${field.name} = ${bound.expression};`);
    }

    const source = [
        '#version 300 es',
        PRECISION,
        '',
        [...attributes].map(([name, type]) => `in ${type} ${name};`).join('\n'),
        carried.map(field => `out ${field.glslType} v_${field.name};`).join('\n'),
        '',
        body,
        '',
        'void main()',
        '{',
        // Zero-initialised, so a field no attribute feeds is a defined zero rather than garbage.
        `    ${struct.name} ${entry.parameterName} = ${struct.name}(`
            + fields.map(field => zeroFor(field.glslType)).join(', ') + ');',
        fills.join('\n'),
        '',
        `    ${entry.returnType} Out = ${handle.entry}(`
            + [entry.parameterName, ...handle.args].join(', ') + ');',
        carried.map(field => `    v_${field.name} = Out.${field.name};`).join('\n'),
        `    gl_Position = Out.${position.name};`,
        '}',
        '',
    ].join('\n');

    return { source, refusal: null };
}
