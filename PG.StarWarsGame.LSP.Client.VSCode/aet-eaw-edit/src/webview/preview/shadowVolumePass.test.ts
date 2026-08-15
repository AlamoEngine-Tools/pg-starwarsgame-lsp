// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import * as THREE from 'three';

import { ShadowVolumePass, volumeReach } from './shadowVolumePass';

describe('volumeReach', () => {
    it('extrudes far enough to clear the model', () => {
        assert.ok((volumeReach(10, 1000)) > (10));
    });

    it('never extrudes past the far plane, which would clip the volume open', () => {
        // A fragment beyond the far plane is CLIPPED, not depth-failed, so the count is never
        // closed and the whole frame reads as shadowed.
        assert.ok((volumeReach(1000, 100)) <= (50));
    });

    it('scales the reach with the model', () => {
        assert.ok((volumeReach(200, 10000)) > (volumeReach(20, 10000)));
    });

    it('still reaches somewhere for a model with no measurable size', () => {
        assert.ok((volumeReach(0, 1000)) > (0));
    });
});

describe('which volumes count', () => {
    const volumeMesh = (name: string): THREE.Mesh => {
        const mesh = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1));
        mesh.name = name;
        return mesh;
    };

    it('counts a volume as soon as it is adopted', () => {
        const pass = new ShadowVolumePass();

        assert.equal(pass.active, false);
        pass.add(volumeMesh('hull_shadow'));
        assert.equal(pass.active, true);
    });

    it('stops counting a volume the current detail level does not draw', () => {
        // An ALO carries a shadow mesh per ALT level. The source meshes are gated off by LAYER
        // rather than by `visible`, so a counting mesh parented to a gated-off source still drew -
        // and a Lambda shuttle showing ALT0 was being marked by ALT1's volume as well.
        const pass = new ShadowVolumePass();
        const source = volumeMesh('hull_shadow');
        pass.add(source);

        pass.setEnabled(true);
        pass.setCounting(source, false);

        assert.equal(pass.active, false);
        assert.equal(source.children.every(child => !child.visible), true);
    });

    it('counts it again when the level comes back', () => {
        const pass = new ShadowVolumePass();
        const source = volumeMesh('hull_shadow');
        pass.add(source);

        pass.setCounting(source, false);
        pass.setCounting(source, true);

        assert.equal(pass.active, true);
    });

    it('ignores a mesh it never adopted', () => {
        const pass = new ShadowVolumePass();
        pass.add(volumeMesh('hull_shadow'));

        assert.doesNotThrow(() => pass.setCounting(volumeMesh('stranger'), false));
        assert.equal(pass.active, true);
    });
});
