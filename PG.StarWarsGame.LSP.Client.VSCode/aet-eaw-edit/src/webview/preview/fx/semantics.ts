// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What each `: SEMANTIC` annotation means, and where the value behind it comes from.
//
// An Alamo shader never binds a uniform by name. `AlamoEngine.fxh` declares the whole contract as
// semantics - `float4x4 m_worldViewProj : WORLDVIEWPROJECTION;` - and the engine fills them by
// semantic, so a mod may rename any of them freely. Translating the arithmetic is therefore only
// half the job: without this table the translated shader compiles and every uniform stays at zero.
//
// Deliberately free of three.js, so the whole mapping can be tested without a GPU. The viewport
// evaluates the sources named here; this only says what is wanted.

/** Where a uniform's value comes from. `null` means the semantic has no known source. */
export type UniformSource =
    | { kind: 'matrix'; of: AlamoMatrix }
    | { kind: 'eye'; space: Space }
    | { kind: 'light'; index: number; channel: LightChannel; space: Space }
    | { kind: 'ambient' }
    | { kind: 'lightScale' }
    | { kind: 'sphericalHarmonics'; set: 'all' | 'fill' }
    | { kind: 'skinMatrices' }
    | { kind: 'time' }
    | { kind: 'resolution' }
    | { kind: 'fog' }
    | { kind: 'distanceFade' }
    | { kind: 'shadowExtrusion' }
    | { kind: 'wind'; of: 'bend' | 'grass' }
    | { kind: 'texture'; role: TextureRole }
    | { kind: 'projectedTexcoord'; role: TextureRole; axis: 'u' | 'v' };

/**
 * The matrices, named as the engine names them.
 *
 * All nine are supplied rather than derived on the shader side, because that is the contract: a
 * shader asking for WORLDVIEWINVERSE gets it, and computing it from WORLDVIEW would be a different
 * value once a non-uniform scale is in play.
 */
export type AlamoMatrix =
    | 'world' | 'worldInverse' | 'worldView' | 'worldViewInverse' | 'worldViewProjection'
    | 'view' | 'viewInverse' | 'viewProjection' | 'projection';

/** Whether a vector is wanted in world space or in the object's own space. */
export type Space = 'world' | 'object';

/** Which part of a directional light. */
export type LightChannel = 'vector' | 'diffuse' | 'specular';

/** An engine-owned texture the preview has no real data for. */
export type TextureRole = 'fogOfWar' | 'cloud' | 'skyCube';

/**
 * The vertex semantics, mapped onto the attribute names the exported glTF carries.
 *
 * `TANGENT0` and `BINORMAL0` are the tangent basis; glTF stores only the tangent, with the binormal
 * reconstructed from it and the normal, which is what the exporter writes.
 */
const ATTRIBUTES: Record<string, string> = {
    POSITION: 'position',
    NORMAL: 'normal',
    TANGENT0: 'tangent',
    BINORMAL0: 'binormal',
    COLOR0: 'color',
    COLOR1: 'color1',
    TEXCOORD0: 'uv',
    TEXCOORD1: 'uv1',
    TEXCOORD2: 'uv2',
    TEXCOORD3: 'uv3',
    TEXCOORD4: 'uv4',
    TEXCOORD5: 'uv5',
    FOG: 'fog',
};

/** The glTF attribute a vertex semantic reads from, or null if it is not a vertex semantic. */
export function attributeFor(semantic: string): string | null {
    return ATTRIBUTES[semantic.toUpperCase()] ?? null;
}

const MATRICES: Record<string, AlamoMatrix> = {
    WORLD: 'world',
    WORLDINVERSE: 'worldInverse',
    WORLDVIEW: 'worldView',
    WORLDVIEWINVERSE: 'worldViewInverse',
    WORLDVIEWPROJECTION: 'worldViewProjection',
    VIEW: 'view',
    VIEWINVERSE: 'viewInverse',
    VIEWPROJECTION: 'viewProjection',
    PROJECTION: 'projection',
};

const SIMPLE: Record<string, UniformSource> = {
    EYE_POSITION: { kind: 'eye', space: 'world' },
    EYE_OBJ_POSITION: { kind: 'eye', space: 'object' },
    GLOBAL_AMBIENT: { kind: 'ambient' },
    LIGHT_SCALE: { kind: 'lightScale' },
    SKINMATRIXARRAY: { kind: 'skinMatrices' },
    SPH_LIGHT_ALL: { kind: 'sphericalHarmonics', set: 'all' },
    SPH_LIGHT_FILL: { kind: 'sphericalHarmonics', set: 'fill' },
    TIME: { kind: 'time' },
    RESOLUTION_CONSTANTS: { kind: 'resolution' },
    FOG_VALS: { kind: 'fog' },
    DISTANCE_FADE_VALS: { kind: 'distanceFade' },
    SHADOW_EXTRUSION_DISTANCE: { kind: 'shadowExtrusion' },
    WIND_BEND_VECTOR: { kind: 'wind', of: 'bend' },
    WIND_GRASS_PARAMS: { kind: 'wind', of: 'grass' },
    FOW_TEXTURE: { kind: 'texture', role: 'fogOfWar' },
    CLOUD_TEXTURE: { kind: 'texture', role: 'cloud' },
    SKY_CUBE_TEXTURE: { kind: 'texture', role: 'skyCube' },
    FOW_TEX_U: { kind: 'projectedTexcoord', role: 'fogOfWar', axis: 'u' },
    FOW_TEX_V: { kind: 'projectedTexcoord', role: 'fogOfWar', axis: 'v' },
    CLOUD_TEX_U: { kind: 'projectedTexcoord', role: 'cloud', axis: 'u' },
    CLOUD_TEX_V: { kind: 'projectedTexcoord', role: 'cloud', axis: 'v' },
};

/**
 * `DIR_LIGHT_<CHANNEL>_<n>`, with an optional `OBJ_` marking the object-space form.
 *
 * The engine ships three directional lights, and a shader working in object space asks for
 * `DIR_LIGHT_OBJ_VEC_0` rather than transforming the world-space one itself.
 */
const DIRECTIONAL = /^DIR_LIGHT_(OBJ_)?(VEC|DIFFUSE|SPECULAR)_([0-9]+)$/;

const CHANNELS: Record<string, LightChannel> = {
    VEC: 'vector',
    DIFFUSE: 'diffuse',
    SPECULAR: 'specular',
};

/**
 * What feeds the uniform carrying this semantic, or null when nothing here does.
 *
 * Null is a real answer and the safe one. A mod may annotate its own uniform with a semantic the
 * engine never had; guessing a value for it would render something plausible and wrong, where
 * falling back to the archetype material renders something plainer and honest.
 */
export function uniformSourceFor(semantic: string): UniformSource | null {
    const name = semantic.toUpperCase();

    const matrix = MATRICES[name];
    if (matrix !== undefined) {
        return { kind: 'matrix', of: matrix };
    }

    const directional = DIRECTIONAL.exec(name);
    if (directional !== null) {
        return {
            kind: 'light',
            index: Number(directional[3]),
            channel: CHANNELS[directional[2]],
            space: directional[1] === undefined ? 'world' : 'object',
        };
    }

    return SIMPLE[name] ?? null;
}

/**
 * The declared type of every uniform in a translated shader.
 *
 * Needed because a value has to FIT the uniform it is going into. The ALO carries `Diffuse` as four
 * components while the effect declares `float3 Diffuse`, and `uniform3fv` with a 4-length array is
 * INVALID_VALUE - WebGL requires a multiple of the uniform's own width. The call is dropped, the
 * uniform stays at zero, and the shader renders black with no error anywhere in sight.
 */
export function collectUniformTypes(source: string): Map<string, string> {
    const types = new Map<string, string>();

    for (const match of source.matchAll(
        /^\s*uniform\s+([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(\[[^\]]*\])?\s*;/gm)) {
        // The array suffix is KEPT. It was matched and thrown away, which left nothing to
        // distinguish `mat4 m_sphFill[3]` from `mat4 m_world` - so `fitToUniform` had to guess from
        // the value's length instead, and the guess is wrong whenever the value divides evenly by a
        // narrower uniform's width. See its own comment.
        types.set(match[2], match[1] + (match[3] ?? ''));
    }

    return types;
}

/**
 * How many elements a declaration asks for: a count, or null when it will not say.
 *
 * Three answers, and the third is the one that matters. No suffix is a single value. `[12]` is
 * twelve. **`[MAX_BONES]` is an array of unknown length** - the shipped skinning header declares
 * `float4x3 m_skinMatrixArray[MAX_BONES]` and the define never resolves to a literal, so there is
 * no count to fit to and the caller's value has to be passed through untouched. Reading that as
 * "not an array" trimmed a four-bone skin palette to its first matrix, and every RSkin model
 * rendered nothing at all.
 */
function arrayLengthOf(type: string | undefined): number | null {
    const suffix = /\[([^\]]*)\]\s*$/.exec(type ?? '');

    if (suffix === null) {
        return 1;
    }

    return /^\s*\d+\s*$/.test(suffix[1]) ? Math.max(1, Number(suffix[1])) : null;
}

/** How many components one uniform of this type takes. Zero for anything not a number. */
function widthOf(type: string | undefined): number {
    switch ((type ?? '').replace(/\[[^\]]*\]\s*$/, '')) {
        case 'float':
        case 'int':
        case 'bool':
            return 1;
        case 'vec2':
            return 2;
        case 'vec3':
            return 3;
        case 'vec4':
            return 4;
        case 'mat2':
            return 4;
        case 'mat3':
            return 9;
        case 'mat4':
            return 16;
        default:
            return 0;
    }
}

/**
 * Fits a value to the uniform it is destined for.
 *
 * Trims what is too wide and pads what is too narrow, because GL rejects BOTH - and rejects them
 * silently as far as the picture is concerned. An array uniform is left whole: its length is
 * already a multiple of the element width, which is exactly what the setter wants.
 */
export function fitToUniform(
    value: readonly number[], type: string | undefined,
): number | number[] {
    const width = widthOf(type);
    if (width === 0) {
        return [...value];
    }

    // An ARRAY takes every element the declaration asks for; a scalar array still needs its list.
    const count = arrayLengthOf(type);

    // An array that will not say how long it is gets the value exactly as handed over.
    if (count === null) {
        return [...value];
    }

    const wanted = width * count;

    if (wanted === 1) {
        return value[0] ?? 0;
    }

    // Trimmed and padded to exactly what the declaration wants, rather than guessed at from the
    // value's own length. The guess was `value.length % width === 0`, and it read the Star
    // Destroyer light strip's four-float `UVScrollRate` - every ALO vector parameter is stored as
    // four floats - as an array of two `vec2`s, which GL refuses outright: "Only array uniforms may
    // have count > 1". A refused upload leaves the uniform at zero with nothing said about it.
    const fitted = value.slice(0, wanted);
    while (fitted.length < wanted) {
        fitted.push(0);
    }

    return fitted;
}

/**
 * Which texture each sampler reads, by the names the effect declares them with.
 *
 * `sampler BaseSampler = sampler_state { Texture = <BaseTexture>; ... };` - and a sub-mesh's shader
 * parameters are keyed by the TEXTURE name, `BaseTexture`, not the sampler's. Without the link a
 * translated shader ends up with samplers and no way to know which of the mesh's textures each one
 * wants, which draws the model in whatever happens to be bound to texture unit zero.
 */
export function collectSamplerTextures(source: string): Map<string, string> {
    const links = new Map<string, string>();

    for (const match of source.matchAll(
        /\bsampler(?:2D|CUBE)?\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*sampler_state\s*\{([^}]*)\}/g)) {
        // Parentheses in 41 of the 42 shipped declarations, angle brackets in the other one, and
        // the keyword's capitalisation varies too.
        const texture =
            /\btexture\s*=\s*[<(]\s*([A-Za-z_][A-Za-z0-9_]*)\s*[>)]/i.exec(match[2]);

        if (texture !== null) {
            links.set(match[1], texture[1]);
        }
    }

    return links;
}

/**
 * The default value each uniform is declared with, flattened into a list of numbers.
 *
 * `AlamoEngine.fxh` initialises most of the contract - `m_fogVals = { -0.005, 200.0 }`, three real
 * spherical-harmonic probe matrices, a three-light rig. GLSL ES uniforms cannot be initialised at
 * all, so those values would otherwise be dropped and every uniform the preview does not feed would
 * sit at zero. Nothing invented here beats what the header already says.
 *
 * Numbers come out in declaration order. For the matrices that matters in principle - an HLSL
 * initialiser list is row-major and a GLSL constructor is column-major - but the only initialised
 * matrices here are the SH probes, which are quadratic forms and therefore symmetric, so the two
 * readings coincide.
 */
export function collectUniformDefaults(source: string): Map<string, number[]> {
    const defaults = new Map<string, number[]>();

    // Unindented only, matching how a global is written in these headers; the `=` may be followed by
    // a brace block running over many lines, so the value is read to its terminating semicolon.
    // A declaration may carry a semantic, an ANNOTATION BLOCK, or both, in that order, before the
    // `=`. The material parameters use the annotation form - `float3 Diffuse < string UIName=...; >
    // = {1,1,1};` - and allowing only a semantic there left Diffuse, Specular, Emissive and
    // Colorization with no value at all. `Diffuse` multiplies the entire diffuse term, so every
    // translated model rendered black.
    const declaration =
        /^(?:uniform\s+|const\s+)*[A-Za-z_][A-Za-z0-9_]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\[[^\]]*\])?\s*(?::\s*[A-Za-z_][A-Za-z0-9_]*\s*)?(?:<[^>]*>\s*)?=/gm;

    for (const match of source.matchAll(declaration)) {
        const end = source.indexOf(';', match.index + match[0].length);
        if (end === -1) {
            continue;
        }

        const numbers = [...source.slice(match.index + match[0].length, end)
            .matchAll(/-?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][-+]?\d+)?/g)]
            .map(number => Number(number[0]));

        if (numbers.length > 0) {
            defaults.set(match[1], numbers);
        }
    }

    return defaults;
}

/**
 * Every annotated GLOBAL in the source, by the name it was declared with.
 *
 * Struct fields carry semantics too and are skipped: those are attributes and varyings, handled by
 * the stage assembly rather than bound as uniforms. They are told apart by position - a global sits
 * at the start of a line, a field is indented inside a `struct` block - which is how the shipped
 * sources are written throughout.
 */
export function collectSemantics(source: string): Map<string, string> {
    const semantics = new Map<string, string>();

    const withoutStructs = source.replace(/\bstruct\s+[A-Za-z_][A-Za-z0-9_]*\s*\{[^}]*\}\s*;/g, '');

    for (const match of withoutStructs.matchAll(
        /^[ \t]*(?:uniform\s+|const\s+)*[A-Za-z_][A-Za-z0-9_]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\[[^\]]*\])?\s*:\s*([A-Za-z_][A-Za-z0-9_]*)/gm)) {
        semantics.set(match[1], match[2]);
    }

    return semantics;
}
