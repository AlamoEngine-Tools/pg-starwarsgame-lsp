// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { bindField } from './attributes';

describe('bindField', () => {
    it('widens a vec3 position into the float4 every entry declares', () => {
        // A point, so the fourth component is 1.
        assert.equal(bindField('POSITION', 'vec4', false)?.expression, 'vec4(position, 1.0)');
    });

    it('passes a normal straight through when the field is a float3', () => {
        assert.equal(bindField('NORMAL', 'vec3', false)?.expression, 'normal');
    });

    it('puts the bone index back into normal.w for a skinned effect', () => {
        // The Alamo vertex format packs it there; the exporter split it out into JOINTS_0.
        const bound = bindField('NORMAL', 'vec4', true);

        assert.equal(bound?.expression, 'vec4(normal, skinIndex.x)');
        assert.deepEqual(bound?.reads.map(r => r.name), ['normal', 'skinIndex']);
    });

    it('leaves normal.w at zero when the effect does no skinning', () => {
        const bound = bindField('NORMAL', 'vec4', false);

        assert.equal(bound?.expression, 'vec4(normal, 0.0)');
        assert.deepEqual(bound?.reads.map(r => r.name), ['normal']);
    });

    it('reconstructs the binormal, which glTF does not store', () => {
        const bound = bindField('BINORMAL0', 'vec3', false);

        assert.equal(bound?.expression, 'cross(normal, tangent.xyz) * tangent.w');
        assert.deepEqual(bound?.reads.map(r => r.name), ['normal', 'tangent']);
    });

    it('narrows the vec4 tangent to the float3 the effects declare', () => {
        assert.equal(bindField('TANGENT0', 'vec3', false)?.expression, 'tangent.xyz');
    });

    it('maps the texture coordinate sets onto three`s names', () => {
        assert.equal(bindField('TEXCOORD0', 'vec2', false)?.expression, 'uv');
        assert.equal(bindField('TEXCOORD1', 'vec2', false)?.expression, 'uv1');
    });

    it('widens a texture coordinate when the field is a float3', () => {
        // Two effects declare TEXCOORD0 that way.
        assert.equal(bindField('TEXCOORD0', 'vec3', false)?.expression, 'vec3(uv, 0.0)');
    });

    it('reads the vertex colour, which the exporter does write', () => {
        assert.equal(bindField('COLOR0', 'vec4', false)?.expression, 'color');
    });

    it('has no binding for a field with no semantic, or an unknown one', () => {
        assert.equal(bindField(undefined, 'vec4', false), null);
        assert.equal(bindField('SOMETHING_ELSE', 'vec4', false), null);
    });
});
