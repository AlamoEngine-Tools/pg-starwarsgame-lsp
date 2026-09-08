// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Making HLSL's silent vector truncation explicit.
//
// `float3 L = m_light0Vector;` is legal HLSL when the light vector is a float4 - the last component
// is quietly dropped. GLSL rejects it outright, and this was the last thing standing between the
// translated effects and a clean compile.

import {
    collectFunctions, collectGlobals, collectStructs, componentCount, inferType, matchingParen,
    matrixDimension, narrowingSwizzle, splitTopLevel, type FunctionTable, type Scope, type StructTable,
} from './glslTypes';

/** Wraps an expression so it can take a swizzle, parenthesising only when it must. */
function narrow(expression: string, components: number): string {
    const text = expression.trim();
    const atomic = /^[A-Za-z_][A-Za-z0-9_.]*$/.test(text)
        || (text.startsWith('(') && matchingParen(text, 0) === text.length - 1)
        || /^[A-Za-z_][A-Za-z0-9_]*\s*\(.*\)$/.test(text);

    return `${atomic ? text : `(${text})`}${narrowingSwizzle(components)}`;
}

/**
 * Narrows one expression to a target type, if it is wider.
 *
 * Only vectors are touched, and only downwards. Widening is a different mistake - HLSL does not do
 * it silently either - and matrices are left alone entirely.
 */
export function narrowExpression(
    expression: string, target: string, scope: Scope, structs: StructTable,
    functions: FunctionTable = new Map(),
): string {
    const wanted = componentCount(target);
    if (wanted === 0) {
        return expression;
    }

    const actual = componentCount(inferType(expression, scope, structs, functions));

    // A scalar broadcasts in both languages, so it needs nothing.
    return actual > wanted && actual > 1 ? narrow(expression, wanted) : expression;
}

/**
 * Narrows the operands of every binary expression to the smaller of the two.
 *
 * HLSL computes `float4 - float3` in float3, dropping the fourth component of the left operand;
 * GLSL will not mix widths at all. The shipped lighting code does exactly this with the eye
 * position - and does it INSIDE a call, `normalize(m_eyePos - P)`, which is why this recurses
 * through call arguments and parentheses rather than looking only at the top level.
 */
function balanceWidths(
    expression: string, scope: Scope, structs: StructTable, functions: FunctionTable,
): string {
    const text = expression.trim();

    if (text === '') {
        return expression;
    }

    // Parenthesised as a whole: balance what is inside.
    if (text.startsWith('(') && matchingParen(text, 0) === text.length - 1) {
        return `(${balanceWidths(text.slice(1, -1), scope, structs, functions)})`;
    }

    // A binary expression at this level: balance each side, then match their widths.
    const split = splitBinary(text);
    if (split !== null) {
        const left = balanceWidths(split.left, scope, structs, functions);
        const right = balanceWidths(split.right, scope, structs, functions);

        const leftWidth = componentCount(inferType(left, scope, structs, functions));
        const rightWidth = componentCount(inferType(right, scope, structs, functions));

        if (leftWidth > 1 && rightWidth > 1 && leftWidth !== rightWidth) {
            const target = wide(Math.min(leftWidth, rightWidth));

            return `${narrowExpression(left, target, scope, structs, functions)} `
                + `${split.operator} `
                + `${narrowExpression(right, target, scope, structs, functions)}`;
        }

        // A vector against a MATRIX, which has no component count of its own: `mul(v, m)` with a
        // float4 and a float3x3 is a truncation HLSL performs for you. Every skinning shader does
        // it, rotating the light vector by the bone's 3x3.
        const leftMatrix = matrixDimension(inferType(left, scope, structs, functions));
        const rightMatrix = matrixDimension(inferType(right, scope, structs, functions));

        if (leftMatrix > 0 && rightWidth > leftMatrix) {
            return `${left} ${split.operator} `
                + narrowExpression(right, wide(leftMatrix), scope, structs, functions);
        }
        if (rightMatrix > 0 && leftWidth > rightMatrix) {
            return `${narrowExpression(left, wide(rightMatrix), scope, structs, functions)} `
                + `${split.operator} ${right}`;
        }

        return `${left} ${split.operator} ${right}`;
    }

    // A call: balance each argument, then narrow it to the parameter it is passed as. HLSL drops
    // components at a call boundary as quietly as it does at an assignment - the tangent-space
    // helpers are declared in float3 and every caller hands them a float4.
    const call = /^([A-Za-z_][A-Za-z0-9_]*)\s*\(/.exec(text);
    if (call !== null && matchingParen(text, call[0].length - 1) === text.length - 1) {
        const parameters = functions.get(call[1])?.parameters;

        const args = splitTopLevel(text.slice(call[0].length, -1))
            .map(arg => balanceWidths(arg, scope, structs, functions))
            .map((arg, index) => {
                const parameter = parameters?.[index];

                return parameter === undefined
                    ? arg
                    : narrowExpression(arg, parameter, scope, structs, functions);
            });

        return `${call[1]}(${args.join(', ')})`;
    }

    return text;
}

/** The rightmost top-level binary operator, and the two sides of it. */
function splitBinary(
    text: string,
): { left: string; right: string; operator: string } | null {
    for (const operators of ['+-', '*/']) {
        let depth = 0;

        for (let i = text.length - 1; i >= 0; i--) {
            const c = text[i];

            if (c === ')' || c === ']') {
                depth++;
            } else if (c === '(' || c === '[') {
                depth--;
            } else if (depth === 0 && operators.includes(c)) {
                const left = text.slice(0, i).trim();

                // Nothing to the left, or another operator, means this is a sign rather than a
                // binary operator.
                if (left === '' || /[-+*/(,]$/.test(left)) {
                    continue;
                }

                return { left, right: text.slice(i + 1).trim(), operator: c };
            }
        }
    }

    return null;
}

function wide(components: number): string {
    return components === 1 ? 'float' : `vec${components}`;
}

// Non-square spellings first, so the alternation does not match `mat4` inside `mat4x3`.
const BUILTIN_TYPES = [
    'mat2x3', 'mat2x4', 'mat3x2', 'mat3x4', 'mat4x2', 'mat4x3',
    'float', 'vec2', 'vec3', 'vec4', 'mat2', 'mat3', 'mat4', 'int', 'bool',
];

/**
 * Matches a local declaration of any type in play - built in or a struct declared in this unit.
 *
 * Naming the types rather than accepting any identifier is what keeps `return Out;` from reading as
 * a declaration.
 */
function localDeclarations(structs: StructTable): RegExp {
    const types = [...BUILTIN_TYPES, ...structs.keys()].join('|');

    return new RegExp(`\\b(${types})\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*[=;]`, 'g');
}

/**
 * Rewrites a translation unit so no assignment relies on implicit truncation.
 *
 * Function by function, each with its OWN scope. A single flat scope looked sufficient and was not:
 * both entry points name their parameter `In`, so the vertex shader's `VS_INPUT_MESH In` overwrote
 * the pixel shader's `VS_OUTPUT In`, every `In.Spec` became untypeable, and every truncation on one
 * was silently skipped. Same name, different struct, in the same file.
 *
 * Anything whose type cannot be worked out is left exactly as it was - a wrong swizzle changes what
 * the shader computes, which is worse than a shader that does not translate.
 */
/**
 * Makes a scalar conversion explicit.
 *
 * HLSL converts freely between float and int; GLSL converts neither way on its own. Every skinning
 * shader opens with `int index = In.Normal.w;`, reading the bone index out of a float channel - and
 * `int()` truncates toward zero exactly as HLSL's implicit conversion does.
 */
function convertScalar(
    expression: string, target: string, scope: Scope, structs: StructTable,
    functions: FunctionTable,
): string {
    if (target !== 'int' && target !== 'float') {
        return expression;
    }

    const actual = inferType(expression, scope, structs, functions);
    const convertible = actual === 'int' || actual === 'float';

    return convertible && actual !== target ? `${target}(${expression})` : expression;
}

/**
 * The names visible inside one function: the globals, its parameters, and its locals.
 *
 * Locals include the STRUCT-typed ones. Leaving those out was not a small gap: every pixel shader
 * fills a `VS_OUTPUT Out;` declared here, so without it `Out.Diff` had no type and the whole
 * statement was passed over, truncation and all.
 */
function scopeFor(
    header: FunctionHeader, body: string, globals: Scope, structs: StructTable,
): Scope {
    const scope: Scope = new Map(globals);

    for (const parameter of splitTopLevel(header.parameters)) {
        const parts = parameter.replace(/\buniform\b/, '').trim().split(/\s+/);
        if (parts.length >= 2) {
            scope.set(parts[1], parts[0]);
        }
    }

    for (const declaration of body.matchAll(localDeclarations(structs))) {
        scope.set(declaration[2], declaration[1]);
    }

    return scope;
}

export function fixVectorTruncation(source: string): string {
    const structs = collectStructs(source);
    const globals = collectGlobals(source);
    const functions = collectFunctions(source);

    return eachFunction(source, (header, body) => {
        const scope = scopeFor(header, body, globals, structs);

        // Plain and compound assignment alike. `obj_pos -= extrusion * light_vec;` never matched
        // while the operator was spelled as a bare `=`, so nothing on its right-hand side was
        // balanced. The `(?!=)` keeps `==` out of it.
        return body.replace(
            /^([ \t]+)(?:((?:float|vec[234]|mat[234](?:x[234])?|int|bool)\s+)?([A-Za-z_][A-Za-z0-9_.]*)\s*([-+*/]?=)(?!=)\s*)([^;]+);/gm,
            (whole, indent: string, declaredType: string | undefined, target: string,
                operator: string, rhs: string) => {
                const type = declaredType?.trim() ?? inferType(target, scope, structs, functions);
                if (type === null) {
                    return whole;
                }

                const balanced = balanceWidths(rhs.trim(), scope, structs, functions);
                const narrowed = narrowExpression(balanced, type, scope, structs, functions);
                const fixed = convertScalar(narrowed, type, scope, structs, functions);

                return `${indent}${declaredType ?? ''}${target} ${operator} ${fixed};`;
            });
    });
}

/**
 * Turns a numeric `if` condition into the boolean GLSL insists on.
 *
 * HLSL takes any nonzero number as true, and the techniques lean on it: a pass compiles
 * `alpha_ps_main( 1 )` and the shader body then writes `if (DO_FOW)`. GLSL ES has no such
 * conversion. Only conditions that are known to be a number are touched - a bool is already right,
 * and anything untypeable is left alone rather than guessed at.
 */
export function fixBooleanConditions(source: string): string {
    const structs = collectStructs(source);
    const globals = collectGlobals(source);
    const functions = collectFunctions(source);

    return eachFunction(source, (header, body) => {
        const scope = scopeFor(header, body, globals, structs);
        let out = '';
        let cursor = 0;

        const keyword = /\b(if|while)\s*\(/g;

        for (;;) {
            keyword.lastIndex = cursor;
            const match = keyword.exec(body);
            if (match === null) {
                return out + body.slice(cursor);
            }

            const open = match.index + match[0].length - 1;
            const close = matchingParen(body, open);
            if (close === -1) {
                return out + body.slice(cursor);
            }

            const condition = body.slice(open + 1, close);
            const type = inferType(condition, scope, structs, functions);

            out += body.slice(cursor, open + 1);
            out += type === 'int' || type === 'float'
                ? `${condition} != ${type === 'int' ? '0' : '0.0'}`
                : condition;
            cursor = close;
        }
    });
}

/**
 * Turns HLSL's `(StructName)0` into a GLSL struct constructor.
 *
 * HLSL zero-initialises a whole struct by casting a scalar to it, which is how every vertex shader
 * here starts: `VS_OUTPUT Out = (VS_OUTPUT)0;`. GLSL has no such cast, but it does have struct
 * constructors taking every field in order - so this is a faithful rewrite rather than a guess.
 */
export function zeroStructCasts(source: string): string {
    const structs = collectStructs(source);
    let out = source;

    for (const [name, fields] of structs) {
        const zeroes = [...fields.values()].map(zeroFor).join(', ');

        // `(Name)0` and `(Name)(0)`, with any spacing.
        out = out.replace(
            new RegExp(`\\(\\s*${name}\\s*\\)\\s*\\(?\\s*0(?:\\.0*)?\\s*\\)?`, 'g'),
            `${name}(${zeroes})`);
    }

    return out;
}

/** A zero of the given type, spelled the way GLSL wants it. */
export function zeroFor(type: string): string {
    if (type === 'int') {
        return '0';
    }
    if (type === 'bool') {
        return 'false';
    }
    if (type === 'float') {
        return '0.0';
    }

    return `${type}(0.0)`;
}

/** A function header as parsed out of the source text, distinct from the typed signature. */
interface FunctionHeader {
    returnType: string;
    name: string;
    parameters: string;
}

/**
 * Applies `rewrite` to each function body in turn, leaving everything between them alone.
 *
 * Brace-matched, so a body containing nested blocks stays intact.
 */
function eachFunction(
    source: string,
    rewrite: (header: FunctionHeader, body: string) => string,
): string {
    const header =
        /\b([A-Za-z_][A-Za-z0-9_]*)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*\{/g;

    let out = '';
    let cursor = 0;

    for (;;) {
        header.lastIndex = cursor;
        const match = header.exec(source);
        if (match === null) {
            return out + source.slice(cursor);
        }

        const open = match.index + match[0].length - 1;
        const close = matchingBrace(source, open);
        if (close === -1) {
            return out + source.slice(cursor);
        }

        const parsed: FunctionHeader = {
            returnType: match[1],
            name: match[2],
            parameters: match[3],
        };

        out += source.slice(cursor, open + 1);
        out += rewrite(parsed, source.slice(open + 1, close));
        cursor = close;
    }
}

/**
 * Narrows every `return` to the width its function declares.
 *
 * The same silent truncation, in the other direction: `float4 f() { return someFloat3Expression; }`
 * is fine in HLSL and a type error in GLSL. Functions are found by brace matching so a return knows
 * which signature it belongs to - the shipped headers declare several helpers per file, and a
 * single flat sweep would narrow each to whichever signature happened to be nearest.
 */
export function fixReturnWidths(source: string): string {
    const structs = collectStructs(source);
    const scope = collectGlobals(source);

    for (const match of source.matchAll(
        /\b(float|vec2|vec3|vec4|int|bool)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*\{/g)) {
        scope.set(match[2], match[1]);
    }

    const signature =
        /\b(float|vec2|vec3|vec4|int|bool)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)\s*\{/g;

    let out = '';
    let cursor = 0;

    for (;;) {
        signature.lastIndex = cursor;
        const match = signature.exec(source);
        if (match === null) {
            return out + source.slice(cursor);
        }

        const open = match.index + match[0].length - 1;
        const close = matchingBrace(source, open);
        if (close === -1) {
            return out + source.slice(cursor);
        }

        // Parameters count as locals, or a return using one cannot be typed.
        const local: Scope = new Map(scope);
        for (const parameter of splitTopLevel(match[3])) {
            const parts = parameter.replace(/\buniform\b/, '').trim().split(/\s+/);
            if (parts.length >= 2) {
                local.set(parts[1], parts[0]);
            }
        }

        for (const declaration of source.slice(open, close).matchAll(
            /\b(float|vec2|vec3|vec4|mat2|mat3|mat4|int|bool)\s+([A-Za-z_][A-Za-z0-9_]*)\s*[=;]/g)) {
            local.set(declaration[2], declaration[1]);
        }

        const body = source.slice(open, close).replace(
            /\breturn\s+([^;]+);/g,
            (whole, expression: string) =>
                `return ${narrowExpression(expression.trim(), match[1], local, structs)};`);

        out += source.slice(cursor, open) + body;
        cursor = close;
    }
}

/** The index of the `}` matching the `{` at `open`, or -1. */
function matchingBrace(text: string, open: number): number {
    let depth = 0;

    for (let i = open; i < text.length; i++) {
        if (text[i] === '{') {
            depth++;
        } else if (text[i] === '}') {
            depth--;
            if (depth === 0) {
                return i;
            }
        }
    }

    return -1;
}

/**
 * Gives an empty non-void function something to return.
 *
 * Not a translation problem: the shipped headers really do contain
 * `float Compute_DOF_Alpha_WS(float3 world_pos) { }` - depth-of-field stubs that were disabled and
 * left behind. HLSL compiles that; GLSL will not. Zero is the only honest filling, and it matches
 * what the disabled effect contributes.
 */
export function fillEmptyFunctions(source: string): string {
    const signature =
        /\b(float|vec2|vec3|vec4|mat2|mat3|mat4|int|bool)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\([^)]*\)\s*\{/g;

    let out = '';
    let cursor = 0;

    for (;;) {
        signature.lastIndex = cursor;
        const match = signature.exec(source);
        if (match === null) {
            return out + source.slice(cursor);
        }

        const open = match.index + match[0].length - 1;
        const close = matchingBrace(source, open);
        if (close === -1) {
            return out + source.slice(cursor);
        }

        const body = source.slice(open + 1, close);
        const empty = body.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '').trim() === '';

        out += source.slice(cursor, open + 1);
        out += empty ? `\n    return ${match[1]}(0.0);\n` : body;
        cursor = close;
    }
}

/** Narrows every argument of a call to the parameter widths a signature declares. */
export function narrowCallArguments(
    call: string, parameterTypes: readonly string[], scope: Scope, structs: StructTable,
): string {
    const open = call.indexOf('(');
    if (open === -1 || matchingParen(call, open) !== call.length - 1) {
        return call;
    }

    const args = splitTopLevel(call.slice(open + 1, -1));
    if (args.length !== parameterTypes.length) {
        return call;
    }

    const narrowed = args.map(
        (arg, i) => narrowExpression(arg, parameterTypes[i], scope, structs));

    return `${call.slice(0, open)}(${narrowed.join(', ')})`;
}
