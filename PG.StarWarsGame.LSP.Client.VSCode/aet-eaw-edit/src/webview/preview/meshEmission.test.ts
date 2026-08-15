// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    emissionMultiplier, emissionPoint, offsetAlongNormal, vertexCount,
    type EmissionCursor, type EmissionMesh,
} from './meshEmission';

/** Two sub-meshes, so the walk has a boundary to roll over. */
const MESH: EmissionMesh = {
    subMeshes: [
        {
            positions: [0, 0, 0, 1, 0, 0, 0, 1, 0],
            normals: [0, 0, 1, 0, 0, 1, 0, 0, 1],
            indices: [0, 1, 2],
        },
        {
            positions: [5, 0, 0, 6, 0, 0],
            normals: [1, 0, 0, 1, 0, 0],
            indices: [],
        },
    ],
};

const cursor = (): EmissionCursor => ({ subMesh: 0, vertex: 0 });
const never = (): number => {
    throw new Error('EveryVertex must not consult the random source');
};

describe('vertexCount', () => {
    it('counts across every sub-mesh', () => {
        assert.equal(vertexCount(MESH), 5);
    });
});

describe('emissionMultiplier', () => {
    /**
     * The line that makes one system behave like many emitters: `CreatorPlugins.cpp:1040` scales
     * the spawn count by the vertex count for EveryVertex, so a tick lays one particle on each.
     */
    it('scales EveryVertex by the vertex count', () => {
        assert.equal(emissionMultiplier('EveryVertex', MESH), 5);
    });

    it('leaves every other mode alone', () => {
        assert.equal(emissionMultiplier('RandomVertex', MESH), 1);
        assert.equal(emissionMultiplier('RandomMesh', MESH), 1);
        assert.equal(emissionMultiplier('Disabled', MESH), 1);
    });

    it('does not multiply when there is no mesh to emit from', () => {
        assert.equal(emissionMultiplier('EveryVertex', null), 1);
    });
});

describe('emissionPoint', () => {
    describe('EveryVertex', () => {
        /**
         * Ordered, not random. The engine steps one vertex at a time and rolls into the next
         * sub-mesh, which is what spreads a burst evenly over the mesh instead of clumping it.
         */
        it('walks every vertex in order and wraps round', () => {
            const at = cursor();
            const seen: number[] = [];

            for (let i = 0; i < 6; i++) {
                seen.push(emissionPoint('EveryVertex', MESH, at, never)!.position.x);
            }

            assert.deepEqual(seen, [0, 1, 0, 5, 6, 0], 'the walk did not cover both sub-meshes');
        });

        it('rolls over into the next sub-mesh at the boundary', () => {
            const at = cursor();
            for (let i = 0; i < 3; i++) { emissionPoint('EveryVertex', MESH, at, never); }

            assert.deepEqual(at, { subMesh: 1, vertex: 0 });
        });

        it('recovers when the cursor is past the end of a mesh that changed', () => {
            const at: EmissionCursor = { subMesh: 9, vertex: 99 };

            assert.equal(emissionPoint('EveryVertex', MESH, at, never)!.position.x, 0);
        });
    });

    it('takes a whole vertex for RandomVertex', () => {
        // Second sub-mesh, second vertex.
        const point = emissionPoint('RandomVertex', MESH, cursor(), seq([0.9, 0.9]));

        assert.deepEqual(point!.position, { x: 6, y: 0, z: 0 });
    });

    it('blends across a face for RandomMesh', () => {
        // First sub-mesh, first face, weights that land inside the triangle.
        const point = emissionPoint('RandomMesh', MESH, cursor(), seq([0, 0, 0.5, 0.5]));

        assert.ok(point!.position.x >= 0 && point!.position.x <= 1);
        assert.ok(point!.position.y >= 0 && point!.position.y <= 1);
        assert.ok(point!.position.x + point!.position.y <= 1.0001, 'landed outside the triangle');
    });

    it('falls back to a vertex when a sub-mesh has no faces', () => {
        // The second sub-mesh carries no indices, so there is nothing else to sample.
        const point = emissionPoint('RandomMesh', MESH, cursor(), seq([0.9, 0]));

        assert.equal(point!.position.x, 5);
    });

    it('gives nothing back for a mesh with no vertices', () => {
        const empty: EmissionMesh = { subMeshes: [{ positions: [], normals: [], indices: [] }] };

        assert.equal(emissionPoint('RandomVertex', empty, cursor(), () => 0), null);
    });
});

describe('offsetAlongNormal', () => {
    /**
     * Without it every particle is born in the skin of the model and the near half of the cloud is
     * swallowed by the hull that emitted it.
     */
    it('lifts the point off the surface', () => {
        const lifted = offsetAlongNormal(
            { position: { x: 1, y: 2, z: 3 }, normal: { x: 0, y: 0, z: 1 } }, 0.5);

        assert.deepEqual(lifted, { x: 1, y: 2, z: 3.5 });
    });
});

/** A random source that hands back a fixed script, so a sample can be aimed at one vertex. */
function seq(values: readonly number[]): () => number {
    let i = 0;
    return () => values[Math.min(i++, values.length - 1)];
}
