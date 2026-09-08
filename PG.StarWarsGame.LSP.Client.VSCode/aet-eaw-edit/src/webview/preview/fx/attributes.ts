// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Feeding an Alamo vertex entry from the buffers three.js actually binds.
//
// The entry takes a struct of its own - `float4 Pos : POSITION; float3 Normal : NORMAL;` - while the
// geometry arrives as the glTF attributes our exporter wrote, under the names three gives them. A
// vertex shader that declares `in vec4 a_Pos` binds to nothing at all and renders an empty screen,
// so the generated shader declares the REAL attribute names and adapts each into the field.
//
// The (semantic, field type) pairs are a closed set, measured across every vertex entry in the
// shipped effects: POSITION as float4; NORMAL as float3 and float4; TEXCOORD0 as float2 and float3;
// TEXCOORD1 as float2; TANGENT0 and BINORMAL0 as float3; COLOR0 as float4.

import { attributeFor } from './semantics';

/** An attribute the generated shader must declare, with the type the exporter writes it as. */
export interface AttributeBinding {
    name: string;
    glslType: string;
}

/**
 * What each attribute is exported as, from `ModelGlbExporter`.
 *
 * `VertexColor1Texture2` gives COLOR_0, TEXCOORD_0 and TEXCOORD_1; tangents come only from the
 * `U3U3` vertex formats; skinned sub-meshes add JOINTS_0 and WEIGHTS_0.
 */
const ATTRIBUTE_TYPES: Record<string, string> = {
    position: 'vec3',
    normal: 'vec3',
    tangent: 'vec4',
    color: 'vec4',
    uv: 'vec2',
    uv1: 'vec2',
    uv2: 'vec2',
    uv3: 'vec2',
    skinIndex: 'vec4',
    skinWeight: 'vec4',
};

/** One input field: where its value comes from, and what the shader must declare to get it. */
export interface FieldBinding {
    /** The GLSL expression assigned into the struct field. */
    expression: string;
    /** Attributes this expression reads. */
    reads: AttributeBinding[];
}

function attribute(name: string): AttributeBinding {
    return { name, glslType: ATTRIBUTE_TYPES[name] ?? 'vec4' };
}

/**
 * How to fill one input field from the geometry.
 *
 * `skinned` says whether the shader does its own bone lookup - it decides only what lands in
 * `Normal.w`. The Alamo vertex format packs the bone index there, and the exporter split it out
 * into JOINTS_0, so for a skinned effect this puts it back. Everything else reads a zero, which is
 * what an unskinned mesh should see.
 */
export function bindField(semantic: string | undefined, fieldType: string, skinned: boolean): FieldBinding | null {
    const name = semantic?.toUpperCase();
    if (name === undefined) {
        return null;
    }

    const source = attributeFor(name);
    if (source === null) {
        return null;
    }

    if (name === 'BINORMAL0') {
        // glTF stores no binormal. The spec's own reconstruction: the handedness sign rides in the
        // tangent's w, which is why the tangent is a vec4.
        return {
            expression: fitTo('cross(normal, tangent.xyz) * tangent.w', 'vec3', fieldType),
            reads: [attribute('normal'), attribute('tangent')],
        };
    }

    if (name === 'NORMAL' && componentsOf(fieldType) === 4) {
        const w = skinned ? 'skinIndex.x' : '0.0';

        return {
            expression: `vec4(normal, ${w})`,
            reads: skinned ? [attribute('normal'), attribute('skinIndex')] : [attribute('normal')],
        };
    }

    if (name === 'TANGENT0') {
        return {
            expression: fitTo('tangent.xyz', 'vec3', fieldType),
            reads: [attribute('tangent')],
        };
    }

    const bound = attribute(source);
    return {
        // A position is a point and gets a 1 in w; everything else widens with zeroes.
        expression: fitTo(bound.name, bound.glslType, fieldType, name === 'POSITION' ? '1.0' : '0.0'),
        reads: [bound],
    };
}

/** Widens or narrows `expression` from `have` components to what `want` declares. */
function fitTo(expression: string, have: string, want: string, pad = '0.0'): string {
    const from = componentsOf(have);
    const to = componentsOf(want);

    if (from === to || to === 0) {
        return expression;
    }
    if (from > to) {
        return `${expression}.${'xyzw'.slice(0, to)}`;
    }

    return `${want}(${expression}${`, ${pad}`.repeat(to - from)})`;
}

function componentsOf(type: string): number {
    switch (type) {
        case 'float':
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
