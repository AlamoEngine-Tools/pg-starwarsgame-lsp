// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { skinHiddenByClip } from './skinVisibility';

/** A clip that hides exactly the named bones. */
const clip = (...hidden: string[]) => (id: string): boolean | undefined =>
    hidden.includes(id) ? false : undefined;

describe('skinHiddenByClip', () => {
    /**
     * Yoda's blade, which is the case this exists for. `sabre_light` is skinned to
     * `{Root#0, saber#1, B_saber#21}` and rides bone 0, so no bone track can prune it - it drew on
     * every frame of every clip, hilt-less and stuck by his leg. His `idle_00` hides both the
     * non-root bones, and that is the whole signal.
     */
    it('hides a mesh whose every non-root skin bone the clip hides', () => {
        assert.equal(
            skinHiddenByClip(['Root#0', 'saber#1', 'B_saber#21'], clip('saber#1', 'B_saber#21')),
            true);
    });

    /**
     * The quantifier is EVERY, and the corpus is emphatic about it. 88 shipped cases have some but
     * not all of a skin set hidden, and they include the dark trooper's body at all three LODs plus
     * its shadow - 11 to 18 bones - on every frame of two clips. "Any" would blank the trooper.
     */
    it('says nothing when only some of the set is hidden', () => {
        assert.equal(
            skinHiddenByClip(['Root#0', 'B_Hand_R#17', 'B_Gun#22'], clip('B_Gun#22')),
            undefined);
    });

    it('says nothing when the clip is silent about the whole set', () => {
        assert.equal(skinHiddenByClip(['Root#0', 'saber#1'], clip()), undefined);
    });

    /**
     * Root is excluded, and that is load-bearing rather than tidy: `sabre_light`'s set INCLUDES
     * bone 0, which nothing ever hides. Counted, the rule could never fire on anything.
     */
    it('ignores the root, which nothing ever hides', () => {
        assert.equal(skinHiddenByClip(['Root#0', 'saber#1'], clip('saber#1')), true);
    });

    it('has nothing to say about a mesh skinned to the root alone', () => {
        assert.equal(skinHiddenByClip(['Root#0'], clip('Root#0')), undefined);
    });

    it('has nothing to say about an unskinned mesh', () => {
        assert.equal(skinHiddenByClip([], clip()), undefined);
    });

    /**
     * Only ever a hide. A skinned mesh whose bones the clip shows is not thereby overriding what
     * the FILE says about it - the file marks plenty of geometry hidden on purpose, and the clip
     * speaking about a bone the mesh happens to use is not permission to draw the mesh.
     */
    it('never asserts that a mesh is visible', () => {
        assert.equal(
            skinHiddenByClip(['Root#0', 'saber#1'], () => true),
            undefined);
    });
});
