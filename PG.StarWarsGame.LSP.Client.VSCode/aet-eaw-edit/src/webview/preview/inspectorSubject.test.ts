// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    geometryKeyOf,
    sameGeometryKey,
    type InspectorSubject,
} from './inspectorSubject';
import {
    type BoneInspection, type MeshInspection, type ParticleInspection,
} from './inspector';

const mesh = (over: Partial<MeshInspection> = {}): MeshInspection => ({
    kind: 'mesh',
    name: 'hull',
    boneName: 'Root',
    vertexCount: 120,
    triangleCount: 40,
    drawn: true,
    extras: { alamoShader: 'MeshBumpColorize.fx', alamoMesh: 'hull' },
    meshIndex: 2,
    subMeshIndex: 0,
    ...over,
});

const bone = (over: Partial<BoneInspection> = {}): BoneInspection => ({
    kind: 'bone',
    index: 3,
    name: 'HP_F-L',
    parentName: 'Root',
    parentIndex: 0,
    visible: true,
    billboard: null,
    ...over,
});

const particle = (over: Partial<ParticleInspection> = {}): ParticleInspection => ({
    kind: 'particle',
    name: 'Engine_Glow',
    boneName: 'engines_big',
    emitterCount: 3,
    playing: true,
    ...over,
});

const subject = (over: Partial<InspectorSubject> = {}): InspectorSubject => ({
    rowId: 'part0:hull:0',
    sources: [mesh()],
    modelDetail: null,
    modelReference: 'ev_stardestroyer',
    ...over,
});

describe('geometryKeyOf', () => {
    it('keys a mesh row by its model and its two indices', () => {
        assert.deepEqual(geometryKeyOf(subject()), {
            modelReference: 'ev_stardestroyer',
            meshIndex: 2,
            subMeshIndex: 0,
        });
    });

    it('finds the mesh among a merged row\'s sources', () => {
        // A bone and the mesh riding it are ONE row, so both facts arrive together and the
        // geometry belongs to the mesh half.
        const merged = subject({ sources: [bone(), mesh({ meshIndex: 7, subMeshIndex: 1 })] });

        assert.deepEqual(geometryKeyOf(merged), {
            modelReference: 'ev_stardestroyer',
            meshIndex: 7,
            subMeshIndex: 1,
        });
    });

    it('has no key without a mesh index', () => {
        assert.equal(geometryKeyOf(subject({ sources: [mesh({ meshIndex: undefined })] })), null);
    });

    it('has no key without a sub-mesh index', () => {
        assert.equal(
            geometryKeyOf(subject({ sources: [mesh({ subMeshIndex: undefined })] })), null);
    });

    it('has no key without a model to fetch it from', () => {
        assert.equal(geometryKeyOf(subject({ modelReference: null })), null);
    });

    it('has no key for a row that is only a bone', () => {
        assert.equal(geometryKeyOf(subject({ sources: [bone()] })), null);
    });

    it('has no key for a particle system', () => {
        assert.equal(geometryKeyOf(subject({ sources: [particle()] })), null);
    });

    it('has no key for a row that is gone from the model', () => {
        assert.equal(geometryKeyOf(subject({ sources: [] })), null);
    });

    // A sub-mesh index of zero is the FIRST sub-mesh and the commonest case; written because the
    // rule it replaces was a truthiness check away from dropping it.
    it('keeps a zero index, which is a real sub-mesh', () => {
        const key = geometryKeyOf(subject({ sources: [mesh({ meshIndex: 0, subMeshIndex: 0 })] }));

        assert.deepEqual(key, { modelReference: 'ev_stardestroyer', meshIndex: 0, subMeshIndex: 0 });
    });
});

describe('sameGeometryKey', () => {
    // The point of the whole comparison: the page being read survives anything that does not
    // change WHICH sub-mesh is on screen. Toggling a checkbox rebuilds every row's facts, so the
    // subject arrives as a new object saying the same thing.
    it('is true for equal keys in different objects', () => {
        assert.equal(sameGeometryKey(geometryKeyOf(subject()), geometryKeyOf(subject())), true);
    });

    it('is false when the sub-mesh changes', () => {
        assert.equal(sameGeometryKey(
            geometryKeyOf(subject()),
            geometryKeyOf(subject({ sources: [mesh({ subMeshIndex: 1 })] })),
        ), false);
    });

    it('is false when the mesh changes', () => {
        assert.equal(sameGeometryKey(
            geometryKeyOf(subject()),
            geometryKeyOf(subject({ sources: [mesh({ meshIndex: 9 })] })),
        ), false);
    });

    // Two models can each have a mesh 2 sub-mesh 0, and showing one's vertices under the other's
    // name is exactly the confusion the key exists to prevent.
    it('is false when the model changes', () => {
        assert.equal(sameGeometryKey(
            geometryKeyOf(subject()),
            geometryKeyOf(subject({ modelReference: 'ev_victory' })),
        ), false);
    });

    it('is true for two rows that both have no geometry', () => {
        assert.equal(sameGeometryKey(null, null), true);
    });

    it('is false when one row has geometry and the other does not', () => {
        assert.equal(sameGeometryKey(geometryKeyOf(subject()), null), false);
        assert.equal(sameGeometryKey(null, geometryKeyOf(subject())), false);
    });
});
