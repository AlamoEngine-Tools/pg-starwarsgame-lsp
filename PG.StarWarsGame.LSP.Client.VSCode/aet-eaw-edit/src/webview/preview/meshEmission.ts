// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Emitting from a MESH rather than from a shape volume.
//
// An emitter's `emitFromMesh` says where its particles are born, and only one of the four values is
// the shape volume every emitter here used to assume. The other three read the geometry the system
// is attached to, which is what turns ONE particle system into many emission points - a Nebulon-B's
// engine wash coming off every vertex of the engine mesh rather than a single puff at its origin.
//
// Ported from `CreatorPlugins.cpp:520-590` (`MeshCreatorBase::InitializeParticle`) and the rate
// multiplier at `:1040`. Kept free of three.js so the walk order and the barycentric sampling can
// be tested directly.

import type { AlamoEmitFromMesh, AlamoVector3 } from '../../protocol/modelPreview';

/** A source of positions and normals to emit from. Sub-meshes are kept apart, as the engine does. */
export interface EmissionMesh {
    /** One entry per sub-mesh: flat xyz triples, three floats per vertex. */
    subMeshes: readonly {
        positions: Float32Array | readonly number[];
        normals: Float32Array | readonly number[];
        /** Triangle indices. Only needed for surface emission. */
        indices: Uint32Array | readonly number[];
    }[];
}

/** Where the walk through `EveryVertex` has got to. The engine keeps exactly this pair. */
export interface EmissionCursor {
    subMesh: number;
    vertex: number;
}

/** A birth position and the surface normal there, both in the mesh's own space. */
export interface EmissionPoint {
    position: AlamoVector3;
    normal: AlamoVector3;
}

/** How many vertices a mesh offers, across every sub-mesh. */
export function vertexCount(mesh: EmissionMesh): number {
    return mesh.subMeshes.reduce((total, sub) => total + sub.positions.length / 3, 0);
}

/**
 * How many particles a mesh-emitting emitter asks for, given what it asked for per tick.
 *
 * `EveryVertex` means EVERY vertex, every time: the engine multiplies the spawn count by the mesh's
 * vertex count (`CreatorPlugins.cpp:1040`) so one tick lays down one particle on each. That single
 * line is most of why a system attached this way looks nothing like the same system attached to a
 * bone.
 */
export function emissionMultiplier(mode: AlamoEmitFromMesh, mesh: EmissionMesh | null): number {
    if (mode !== 'EveryVertex' || mesh === null) {
        return 1;
    }

    return Math.max(1, vertexCount(mesh));
}

function vertexAt(
    mesh: EmissionMesh, subMesh: number, vertex: number,
): EmissionPoint {
    const sub = mesh.subMeshes[subMesh];
    const at = vertex * 3;

    return {
        position: { x: sub.positions[at], y: sub.positions[at + 1], z: sub.positions[at + 2] },
        normal: { x: sub.normals[at] ?? 0, y: sub.normals[at + 1] ?? 0, z: sub.normals[at + 2] ?? 0 },
    };
}

/**
 * Picks the next birth point, advancing the cursor when the mode walks the mesh in order.
 *
 * `EveryVertex` is deliberately NOT random: the engine steps one vertex at a time and rolls over
 * into the next sub-mesh, so a burst covers the mesh evenly instead of clumping. The cursor is
 * mutated in place, which is what lets the walk continue across ticks.
 */
export function emissionPoint(
    mode: AlamoEmitFromMesh,
    mesh: EmissionMesh,
    cursor: EmissionCursor,
    random: () => number,
): EmissionPoint | null {
    const usable = mesh.subMeshes.filter(sub => sub.positions.length >= 3);
    if (usable.length === 0) {
        return null;
    }

    if (mode === 'EveryVertex') {
        // Guard the cursor against a mesh that changed under it, so a reload cannot index off the
        // end of a shorter sub-mesh.
        if (cursor.subMesh >= mesh.subMeshes.length
            || cursor.vertex * 3 >= mesh.subMeshes[cursor.subMesh].positions.length) {
            cursor.subMesh = 0;
            cursor.vertex = 0;
        }

        const point = vertexAt(mesh, cursor.subMesh, cursor.vertex);

        cursor.vertex += 1;
        if (cursor.vertex * 3 >= mesh.subMeshes[cursor.subMesh].positions.length) {
            cursor.vertex = 0;
            cursor.subMesh = (cursor.subMesh + 1) % mesh.subMeshes.length;
        }

        return point;
    }

    const subMesh = Math.min(
        mesh.subMeshes.length - 1, Math.floor(random() * mesh.subMeshes.length));
    const sub = mesh.subMeshes[subMesh];
    const vertices = sub.positions.length / 3;

    if (vertices === 0) {
        return null;
    }

    if (mode === 'RandomVertex') {
        return vertexAt(mesh, subMesh, Math.min(vertices - 1, Math.floor(random() * vertices)));
    }

    // RandomMesh: a random point on a random FACE, by barycentric weights. Falls back to a vertex
    // when the sub-mesh carries no indices, which is the only thing left to sample.
    const faces = Math.floor(sub.indices.length / 3);
    if (faces === 0) {
        return vertexAt(mesh, subMesh, Math.min(vertices - 1, Math.floor(random() * vertices)));
    }

    const face = Math.min(faces - 1, Math.floor(random() * faces)) * 3;
    const corners = [0, 1, 2].map(
        i => vertexAt(mesh, subMesh, sub.indices[face + i]));

    const w1 = random();
    const w2 = random() * (1 - w1);
    const w3 = 1 - w1 - w2;
    const weights = [w1, w2, w3];

    const blend = (pick: (p: EmissionPoint) => AlamoVector3): AlamoVector3 => ({
        x: corners.reduce((sum, c, i) => sum + pick(c).x * weights[i], 0),
        y: corners.reduce((sum, c, i) => sum + pick(c).y * weights[i], 0),
        z: corners.reduce((sum, c, i) => sum + pick(c).z * weights[i], 0),
    });

    return { position: blend(p => p.position), normal: blend(p => p.normal) };
}

/**
 * Lifts a birth point off the surface along its normal.
 *
 * `emitFromMeshOffset` in the file, `m_surfaceOffset` in the engine: `position + normal * offset`.
 * Without it every particle is born exactly in the skin of the model and the near half of the
 * cloud is swallowed by the hull it came from.
 */
export function offsetAlongNormal(point: EmissionPoint, offset: number): AlamoVector3 {
    return {
        x: point.position.x + point.normal.x * offset,
        y: point.position.y + point.normal.y * offset,
        z: point.position.z + point.normal.z * offset,
    };
}
