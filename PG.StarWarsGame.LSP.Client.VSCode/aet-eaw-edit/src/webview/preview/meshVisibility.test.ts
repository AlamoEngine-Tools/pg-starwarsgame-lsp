// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import * as THREE from 'three';

import { meshDrawn, setMeshDrawn } from './meshVisibility';

describe('setMeshDrawn', () => {
    it('round-trips', () => {
        const mesh = new THREE.Mesh();

        assert.equal(meshDrawn(mesh), true, 'a fresh mesh should be drawn');

        setMeshDrawn(mesh, false);
        assert.equal(meshDrawn(mesh), false);

        setMeshDrawn(mesh, true);
        assert.equal(meshDrawn(mesh), true);
    });

    /**
     * The whole reason this module exists. `W_fuel_cell_1.alo` hangs its three meshes off bone 0, so
     * the exporter put the first of them - the shadow volume, which is gated off - ON the Root node.
     * With `visible = false` that pruned the entire model; the hull was only drawn if you ticked the
     * shadow mesh back on.
     */
    it('leaves the subtree of a hidden mesh renderable', () => {
        const root = new THREE.Mesh();
        const child = new THREE.Mesh();
        root.add(child);

        setMeshDrawn(root, false);

        assert.equal(root.visible, true, 'visible must stay true, or three prunes the subtree');
        assert.equal(meshDrawn(child), true, 'the child stopped being drawn with its parent');
    });

    /**
     * Pins the three.js behaviour the fix depends on: a failed LAYER test skips that one object and
     * keeps descending, where `visible === false` returns immediately. If a future three ever
     * changed that, this is the test that should fail rather than a model quietly vanishing.
     */
    it('is a mechanism three keeps descending through', () => {
        const camera = new THREE.PerspectiveCamera();
        const hidden = new THREE.Mesh();
        setMeshDrawn(hidden, false);

        assert.equal(hidden.layers.test(camera.layers), false, 'the hidden mesh would still draw');

        const invisible = new THREE.Mesh();
        invisible.visible = false;

        // The contrast that matters: `visible` is checked before three ever looks at the children.
        assert.equal(invisible.layers.test(camera.layers), true);
    });
});
