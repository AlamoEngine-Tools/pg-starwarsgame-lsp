// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Reading an Alamo `.fx` effect's manifest: what it is, which techniques it offers, and the render
// state each pass sets.
//
// Deliberately NOT an HLSL parser. Everything the renderer needs to set up a material is declarative
// and sits outside the shader bodies - the `_ALAMO_*` annotations say what the effect is for, the
// technique annotations say which one to pick, and the passes spell their render state out. Parsing
// only that is a bounded job with a bounded failure mode: an effect this cannot read falls back to
// an archetype material, which is exactly what happens for a mod-authored shader anyway.

/** The `_ALAMO_*` block every effect opens with. */
export interface FxAnnotations {
    /** `Opaque`, `Transparent`, `Shadow`, and so on. */
    renderPhase?: string;
    /** How vertices are processed: `Mesh`, `RSkin`, `Particle`. */
    vertexProc?: string;
    /** The vertex layout the effect expects, e.g. `alD3dVertNU2U3U3`. */
    vertexType?: string;
    tangentSpace?: boolean;
    shadowVolume?: boolean;
}

/** A uniform or texture the effect declares, with its engine semantic when it has one. */
export interface FxParameter {
    type: string;
    name: string;
    /** `WORLDVIEWPROJECTION`, `DIR_LIGHT_OBJ_VEC_0`, ... - how the engine says what to supply. */
    semantic?: string;
}

/** One pass: its render state and the entry points it names. */
export interface FxPass {
    name: string;
    /** Render states exactly as written, keyed lower-case for lookup. */
    states: Record<string, string>;
    vertexShader?: string;
    pixelShader?: string;
}

export interface FxTechnique {
    name: string;
    /** `DX9`, `DX8` or `FIXEDFUNCTION`, from the technique's own annotation. */
    lod?: string;
    passes: FxPass[];
}

export interface FxEffect {
    annotations: FxAnnotations;
    /** Headers the effect pulls in. The parameters usually live in these, not in the `.fx`. */
    includes: string[];
    parameters: FxParameter[];
    techniques: FxTechnique[];
}

/**
 * Removes comments without destroying string literals.
 *
 * A plain regex sweep would eat the `//` inside any quoted path and leave the file unparseable.
 */
export function stripComments(text: string): string {
    let out = '';
    let i = 0;

    while (i < text.length) {
        const two = text.slice(i, i + 2);

        if (two === '//') {
            const end = text.indexOf('\n', i);
            i = end === -1 ? text.length : end;
            continue;
        }

        if (two === '/*') {
            const end = text.indexOf('*/', i + 2);
            i = end === -1 ? text.length : end + 2;
            // Replaced with a space so `a/*x*/b` does not become the identifier `ab`.
            out += ' ';
            continue;
        }

        if (text[i] === '"') {
            const end = text.indexOf('"', i + 1);
            const stop = end === -1 ? text.length : end + 1;
            out += text.slice(i, stop);
            i = stop;
            continue;
        }

        out += text[i];
        i++;
    }

    return out;
}

/** The body of a `{}` or `<>` block starting at `open`, and where it ends. */
function readBlock(
    text: string, open: number, opener: string, closer: string,
): { body: string; end: number } | null {
    if (text[open] !== opener) {
        return null;
    }

    let depth = 0;
    for (let i = open; i < text.length; i++) {
        if (text[i] === opener) {
            depth++;
        } else if (text[i] === closer) {
            depth--;
            if (depth === 0) {
                return { body: text.slice(open + 1, i), end: i + 1 };
            }
        }
    }

    return null;
}

/** Skips whitespace from `from`, returning the first non-space index. */
function skipSpace(text: string, from: number): number {
    let i = from;
    while (i < text.length && /\s/.test(text[i])) {
        i++;
    }
    return i;
}

function parseAnnotations(text: string): FxAnnotations {
    const annotations: FxAnnotations = {};

    const strings = /\b(?:string|bool)\s+(_ALAMO_[A-Z_]+)\s*=\s*("([^"]*)"|true|false)\s*;/g;

    for (const match of text.matchAll(strings)) {
        const value = match[3] ?? match[2];

        switch (match[1]) {
            case '_ALAMO_RENDER_PHASE':
                annotations.renderPhase = value;
                break;
            case '_ALAMO_VERTEX_PROC':
                annotations.vertexProc = value;
                break;
            case '_ALAMO_VERTEX_TYPE':
                annotations.vertexType = value;
                break;
            case '_ALAMO_TANGENT_SPACE':
                annotations.tangentSpace = value === 'true';
                break;
            case '_ALAMO_SHADOW_VOLUME':
                annotations.shadowVolume = value === 'true';
                break;
            default:
                // Another annotation this build does not model. Ignored rather than refused: the
                // point of reading the manifest is to use what is understood.
                break;
        }
    }

    return annotations;
}

function parseIncludes(text: string): string[] {
    return [...text.matchAll(/#include\s+"([^"]+)"/g)].map(match => match[1]);
}

/**
 * Declarations carrying an engine semantic.
 *
 * Only the ones with a `: SEMANTIC` are interesting: those are what the engine supplies, and so what
 * the translated shader has to be handed. Struct fields are excluded - `POSITION` on a struct member
 * describes a vertex layout, not something to bind.
 */
function parseParameters(text: string): FxParameter[] {
    const withoutStructs = text.replace(/\bstruct\b[^{]*\{[^}]*\}\s*;/g, ' ');

    const declarations =
        /\b([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*([A-Za-z_][A-Za-z0-9_]*)\s*;/g;

    return [...withoutStructs.matchAll(declarations)]
        .filter(match => match[1] !== 'return')
        .map(match => ({ type: match[1], name: match[2], semantic: match[3] }));
}

/**
 * A shader assignment, taken whole rather than as a `key = value` pair.
 *
 * The right-hand side is not one token: the shipped effects write
 * `VertexShader = compile vs_1_1 vs_main();`, which is a keyword, a profile, and a call. Reading it
 * with the render-state pattern matched `compile`, went looking for the semicolon, found more
 * identifiers instead and abandoned the whole assignment - so the pass came out with no shader at
 * all. Everything downstream then behaved correctly on a wrong fact: `hasProgrammableShader` said
 * false, the effect was reported as "render state only", and it stayed on its archetype material.
 * That silently cost SEVEN of the 41 shipped effects their translation, including all three
 * additive ones - which is what a light mesh laid over a hull draws with.
 */
const SHADER_ASSIGNMENT = /\b(vertexshader|pixelshader)\s*=\s*([^;]+);/gi;

/** The entry point a shader assignment names, or undefined for an explicit NULL. */
function entryPoint(rightHandSide: string): string | undefined {
    const value = rightHandSide.trim();

    // A fixed-function technique writes NULL outright; that is "no shader", not a function called
    // NULL for the translator to go looking for.
    if (/^null$/i.test(value)) {
        return undefined;
    }

    // Three forms in the wild, all of them real:
    //   VertexShader = compile vs_1_1 vs_main();   the shipped effects, overwhelmingly
    //   VertexShader = (vs_main_bin);              a precompiled binary, parenthesised
    //   VertexShader = vs_main;                    bare
    // Any arguments are left behind: nothing downstream could pass them on.
    const bare = value.replace(/^\(\s*/, '');

    return /^compile\s+[A-Za-z0-9_]+\s+([A-Za-z_][A-Za-z0-9_]*)/i.exec(bare)?.[1]
        ?? /^([A-Za-z_][A-Za-z0-9_]*)/.exec(bare)?.[1];
}

function parsePass(name: string, body: string): FxPass {
    const pass: FxPass = { name, states: {} };

    for (const match of body.matchAll(SHADER_ASSIGNMENT)) {
        if (match[1].toLowerCase() === 'vertexshader') {
            pass.vertexShader = entryPoint(match[2]);
        } else {
            pass.pixelShader = entryPoint(match[2]);
        }
    }

    for (const match of body.matchAll(
        /\b([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\(?\s*([A-Za-z0-9_.]+)\s*\)?\s*;/g)) {
        const key = match[1].toLowerCase();

        // The shader assignments are read above. A bare `VertexShader = NULL;` matches this pattern
        // too, and letting it through would file a shader name where a blend factor belongs.
        if (key === 'vertexshader' || key === 'pixelshader') {
            continue;
        }

        pass.states[key] = match[2];
    }

    return pass;
}

function parseTechniques(text: string): FxTechnique[] {
    const techniques: FxTechnique[] = [];
    const header = /\btechnique\s+([A-Za-z_][A-Za-z0-9_]*)/g;

    for (const match of text.matchAll(header)) {
        const name = match[1];
        let cursor = skipSpace(text, match.index + match[0].length);
        let lod: string | undefined;

        // Annotations come in angle brackets between the name and the body.
        if (text[cursor] === '<') {
            const annotation = readBlock(text, cursor, '<', '>');
            if (annotation === null) {
                continue;
            }

            lod = /\bLOD\s*=\s*"([^"]*)"/.exec(annotation.body)?.[1];
            cursor = skipSpace(text, annotation.end);
        }

        const body = readBlock(text, cursor, '{', '}');
        if (body === null) {
            continue;
        }

        const passes: FxPass[] = [];
        const passHeader = /\bpass\s+([A-Za-z_][A-Za-z0-9_]*)/g;

        for (const passMatch of body.body.matchAll(passHeader)) {
            const passBody = readBlock(
                body.body, skipSpace(body.body, passMatch.index + passMatch[0].length), '{', '}');

            if (passBody !== null) {
                passes.push(parsePass(passMatch[1], passBody.body));
            }
        }

        techniques.push({ name, lod, passes });
    }

    return techniques;
}

/** Reads an effect's manifest out of its `.fx` text. */
export function parseFxManifest(source: string): FxEffect {
    const text = stripComments(source);

    return {
        annotations: parseAnnotations(text),
        includes: parseIncludes(text),
        parameters: parseParameters(text),
        techniques: parseTechniques(text),
    };
}

/**
 * Which technique to prefer, best first.
 *
 * Measured across the shipped effects rather than assumed: only 6 techniques in 41 files are `DX9`,
 * against 42 `DX8` and 35 `FIXEDFUNCTION` - so the fallback is the common path, not the exception.
 * Two spellings turned up that the plan did not list, `FF` and an ATI-specific `DX8ATI`; unknown to
 * this ladder they would have fallen through to the unannotated last resort, which is usually an
 * editor-only pass and worse than either. Plain `DX8` is preferred over the vendor variant.
 */
export const TECHNIQUE_PREFERENCE: readonly string[] =
    ['DX9', 'DX8', 'DX8ATI', 'FIXEDFUNCTION', 'FF'];

/** Whether a technique has any shader to translate, as opposed to render state alone. */
export function hasProgrammableShader(technique: FxTechnique): boolean {
    return technique.passes.some(pass => pass.vertexShader !== undefined);
}

/**
 * The technique to draw with.
 *
 * A technique with no LOD annotation is a last resort - in the shipped effects those are editor-only,
 * like `max_viewport`.
 */
export function selectTechnique(
    effect: FxEffect, preferred: readonly string[] = TECHNIQUE_PREFERENCE,
): FxTechnique | null {
    for (const lod of preferred) {
        const match = effect.techniques.find(
            technique => technique.lod?.toUpperCase() === lod.toUpperCase());

        if (match !== undefined) {
            return match;
        }
    }

    return effect.techniques.find(technique => technique.lod === undefined) ?? null;
}
