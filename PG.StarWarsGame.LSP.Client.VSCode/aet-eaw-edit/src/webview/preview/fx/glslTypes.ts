// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Working out what type a GLSL expression has, so HLSL's silent truncation can be made explicit.
//
// HLSL lets a float4 flow into a float3 and quietly drops the last component. GLSL refuses, so the
// translated code has to say `.xyz` where HLSL said nothing. Knowing where to put it means knowing
// what every sub-expression is - which is why this is a small type inferencer rather than another
// pattern match.

/** The types the translated subset deals in. `null` means "not known", which is never forced. */
export type GlslType =
    | 'float' | 'vec2' | 'vec3' | 'vec4'
    | 'mat2' | 'mat3' | 'mat4'
    | 'int' | 'bool'
    | 'sampler2D' | 'samplerCube'
    | string;

/** How many components a vector type carries. Non-vectors answer 0. */
export function componentCount(type: GlslType | null): number {
    switch (type) {
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
        default:
            return 0;
    }
}

const VECTOR_BY_COUNT: Record<number, GlslType> = { 1: 'float', 2: 'vec2', 3: 'vec3', 4: 'vec4' };

/** The swizzle that narrows a vector to `count` components. */
export function narrowingSwizzle(count: number): string {
    return `.${'xyzw'.slice(0, count)}`;
}

/** A struct and the type of each field. */
export type StructTable = Map<string, Map<string, GlslType>>;

/** Names in scope and what they are. */
export type Scope = Map<string, GlslType>;

/** A function's declared signature. */
export interface FunctionSignature {
    returnType: GlslType;
    /** Parameter types in order, so an argument can be narrowed to the one it is passed as. */
    parameters: GlslType[];
}

/** Every function declared in the unit, by name. */
export type FunctionTable = Map<string, FunctionSignature>;

/**
 * Intrinsics whose result type does not simply follow their first argument.
 *
 * Everything absent from here - normalize, abs, min, max, mix, clamp, pow, floor - returns the type
 * of its first argument, which is the GLSL rule too.
 */
const INTRINSIC_RESULTS: Record<string, GlslType> = {
    dot: 'float',
    length: 'float',
    distance: 'float',
    texture: 'vec4',
    textureProj: 'vec4',
};

/** Reads every `struct Name { type field; ... };` into a lookup. */
export function collectStructs(source: string): StructTable {
    const structs: StructTable = new Map();

    for (const match of source.matchAll(/\bstruct\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{([^}]*)\}\s*;/g)) {
        const fields = new Map<string, GlslType>();

        for (const field of match[2].matchAll(
            /\b([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*;/g)) {
            fields.set(field[2], field[1]);
        }

        structs.set(match[1], fields);
    }

    return structs;
}

/**
 * Statement keywords that a `<word> <word>;` pattern would otherwise read as a declaration.
 *
 * `return Out;` is the one that mattered: it parses as "a variable `Out` of type `return`", which
 * overwrote the correct type picked up two lines earlier and silently disabled every narrowing in
 * the function. Every shipped shader ends that way.
 */
const STATEMENT_KEYWORDS = new Set([
    'return', 'discard', 'if', 'else', 'for', 'while', 'do', 'break', 'continue', 'struct',
]);

/** Reads the declared type of every global, whether or not it is a uniform. */
export function collectGlobals(source: string): Scope {
    const globals: Scope = new Map();

    for (const match of source.matchAll(
        /^[ \t]*(?:uniform\s+|varying\s+|const\s+)*([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\[[^\]]*\])?\s*[=;]/gm)) {
        if (!STATEMENT_KEYWORDS.has(match[1])) {
            globals.set(match[2], match[1]);
        }
    }

    return globals;
}

/**
 * Reads the declared signature of every function in the unit.
 *
 * Worth knowing rather than guessing, on both halves. `Compute_Fog(float3)` returns a float, so the
 * "a call has the type of its first argument" rule - right for the componentwise intrinsics -
 * answered float3 and hung a `.x` on a scalar. The parameter types matter because HLSL truncates an
 * ARGUMENT as silently as it truncates an assignment: the tangent-space helpers take float3 while
 * every caller holds float4s. Includes are flattened by now, so the whole unit is here to read.
 */
export function collectFunctions(source: string): FunctionTable {
    const functions: FunctionTable = new Map();

    for (const match of source.matchAll(
        /\b([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*\{/g)) {
        if (STATEMENT_KEYWORDS.has(match[1])) {
            continue;
        }

        const parameters = splitTopLevel(match[3])
            .map(parameter => parameter.replace(/\b(?:uniform|const|in|out|inout)\b/g, '').trim())
            .filter(parameter => parameter !== '')
            .map(parameter => parameter.split(/\s+/)[0]);

        functions.set(match[2], { returnType: match[1], parameters });
    }

    return functions;
}

/** A swizzle like `.xyz` or `.rgb`, and how many components it selects. */
function swizzleLength(member: string): number | null {
    return /^[xyzwrgbastpq]{1,4}$/.test(member) ? member.length : null;
}

/**
 * The type of one expression.
 *
 * Deliberately incomplete: anything it cannot work out returns null, and a null type is never
 * narrowed. Guessing here would insert a swizzle that changes what the shader computes, which is
 * worse than leaving a shader untranslated.
 */
export function inferType(
    expression: string, scope: Scope, structs: StructTable, functions: FunctionTable = new Map(),
): GlslType | null {
    const text = expression.trim();

    if (text === '') {
        return null;
    }

    // Parenthesised: unwrap only when the parens enclose the whole expression.
    if (text.startsWith('(') && matchingParen(text, 0) === text.length - 1) {
        return inferType(text.slice(1, -1), scope, structs, functions);
    }

    // Binary operators, lowest precedence first, scanned right to left at depth zero.
    for (const operators of [['+', '-'], ['*', '/']]) {
        const split = splitAtOperator(text, operators);
        if (split !== null) {
            return combine(
                inferType(split.left, scope, structs, functions),
                inferType(split.right, scope, structs, functions),
                operators.includes('*'));
        }
    }

    // A call, or a constructor.
    const call = /^([A-Za-z_][A-Za-z0-9_]*)\s*\(/.exec(text);
    if (call !== null && matchingParen(text, call[0].length - 1) === text.length - 1) {
        const name = call[1];

        if (componentCount(name) > 0 || name.startsWith('mat')) {
            return name;
        }
        if (name in INTRINSIC_RESULTS) {
            return INTRINSIC_RESULTS[name];
        }
        // A function declared in this unit: read its return type rather than guessing at one.
        const declared = functions.get(name);
        if (declared !== undefined) {
            return declared.returnType;
        }

        const args = text.slice(call[0].length, -1);
        const first = splitTopLevel(args)[0];

        // What is left is a built-in this file does not list - the componentwise intrinsics, which
        // in GLSL do follow their first argument.
        return first === undefined ? null : inferType(first, scope, structs, functions);
    }

    // Member access: a struct field, or a swizzle.
    const member = /^(.*)\.([A-Za-z_][A-Za-z0-9_]*)$/.exec(text);
    if (member !== null) {
        const baseType = inferType(member[1], scope, structs, functions);
        const fields = baseType === null ? undefined : structs.get(baseType);

        if (fields?.has(member[2]) === true) {
            return fields.get(member[2])!;
        }

        const length = swizzleLength(member[2]);
        return length === null ? null : VECTOR_BY_COUNT[length];
    }

    if (/^[0-9]+\.[0-9]*$|^\.[0-9]+$|^[0-9]+\.$/.test(text)) {
        return 'float';
    }
    if (/^[0-9]+$/.test(text)) {
        return 'int';
    }

    return scope.get(text) ?? null;
}

/** The side length of a square matrix type. Everything else answers 0. */
export function matrixDimension(type: GlslType | null): number {
    switch (type) {
        case 'mat2':
            return 2;
        case 'mat3':
            return 3;
        case 'mat4':
            return 4;
        default:
            return 0;
    }
}

/**
 * How two operand types combine. HLSL narrows to the smaller; a scalar broadcasts.
 *
 * `multiplicative` distinguishes `*` and `/` from `+` and `-`, which matters only for matrices: a
 * matrix TIMES a vector is a vector, while a matrix plus one is not an expression at all.
 */
function combine(
    left: GlslType | null, right: GlslType | null, multiplicative: boolean,
): GlslType | null {
    const a = componentCount(left);
    const b = componentCount(right);
    const leftMatrix = matrixDimension(left);
    const rightMatrix = matrixDimension(right);

    // Matrix cases first, because a matrix has no component count and would otherwise read as
    // unknown - which is how `vec3 world_pos = mul(In.Pos, m_world);` went unnarrowed in nearly
    // every shipped pixel shader.
    if (leftMatrix > 0 && rightMatrix > 0) {
        return left;
    }
    if (leftMatrix > 0 && b === 1) {
        return left;
    }
    if (rightMatrix > 0 && a === 1) {
        return right;
    }
    if (multiplicative && leftMatrix > 0 && b > 1) {
        return VECTOR_BY_COUNT[leftMatrix];
    }
    if (multiplicative && rightMatrix > 0 && a > 1) {
        return VECTOR_BY_COUNT[rightMatrix];
    }

    if (a === 0 || b === 0) {
        // Something unknown is involved; not a case this narrows.
        return left ?? right;
    }

    if (a === 1) {
        return right;
    }
    if (b === 1) {
        return left;
    }

    return VECTOR_BY_COUNT[Math.min(a, b)];
}

/** The index of the `)` matching the `(` at `open`, or -1. */
export function matchingParen(text: string, open: number): number {
    let depth = 0;

    for (let i = open; i < text.length; i++) {
        if (text[i] === '(') {
            depth++;
        } else if (text[i] === ')') {
            depth--;
            if (depth === 0) {
                return i;
            }
        }
    }

    return -1;
}

/** Splits at the rightmost top-level occurrence of one of `operators`. */
function splitAtOperator(
    text: string, operators: readonly string[],
): { left: string; right: string } | null {
    let depth = 0;

    for (let i = text.length - 1; i >= 0; i--) {
        const c = text[i];

        if (c === ')' || c === ']') {
            depth++;
        } else if (c === '(' || c === '[') {
            depth--;
        } else if (depth === 0 && operators.includes(c)) {
            // Not an operator if there is nothing to its left - that is a sign, not a subtraction.
            const left = text.slice(0, i).trim();
            if (left === '' || /[-+*/(,]$/.test(left)) {
                continue;
            }

            return { left, right: text.slice(i + 1) };
        }
    }

    return null;
}

/** Splits a comma-separated list at top level. */
export function splitTopLevel(text: string): string[] {
    const parts: string[] = [];
    let depth = 0;
    let start = 0;

    for (let i = 0; i < text.length; i++) {
        const c = text[i];

        if (c === '(' || c === '[') {
            depth++;
        } else if (c === ')' || c === ']') {
            depth--;
        } else if (c === ',' && depth === 0) {
            parts.push(text.slice(start, i).trim());
            start = i + 1;
        }
    }

    if (text.trim() !== '') {
        parts.push(text.slice(start).trim());
    }

    return parts;
}
