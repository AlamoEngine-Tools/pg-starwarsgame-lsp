// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { attachmentProblem } from './attachments';

describe('attachmentProblem', () => {
    it('says nothing when the attachment resolved', () => {
        assert.equal(
            attachmentProblem({ partId: 'hull', bone: 'HP_F-L' }, { part: true, bone: true }),
            null);
    });

    it('names the part and the bone, which is what finds the thing', () => {
        const problem = attachmentProblem(
            { partId: 'hp:HP_FL', bone: 'FP_F-L_00' }, { part: true, bone: false });

        assert.match(problem!, /hp:HP_FL/);
        assert.match(problem!, /FP_F-L_00/);
    });

    /**
     * The message must not claim to know what happened to the object.
     *
     * The same unresolved bone means a MISPLACED object to `attachmentFor`, which falls back to the
     * owning model's root, and a MISSING one to `bonePosition`, which answers null. It said "drawn
     * at that model's origin" until the second of those existed.
     */
    it('does not promise where the geometry ended up', () => {
        for (const found of [{ part: false, bone: false }, { part: true, bone: false }]) {
            const problem = attachmentProblem({ partId: 'p', bone: 'b' }, found);

            assert.doesNotMatch(problem!, /drawn/);
        }
    });

    it('carries the bone index, since bone names repeat', () => {
        // Boba Fett has two `p_boba_jetpack` bones; a name alone cannot say which one was wanted.
        const problem = attachmentProblem(
            { partId: 'hull', bone: 'p_boba_jetpack', boneIndex: 41 },
            { part: true, bone: false });

        assert.match(problem!, /index 41/);
    });

    it('reports a part that has not loaded separately from a bone that is absent', () => {
        const problem = attachmentProblem(
            { partId: 'hp:late', bone: 'MuzzleA' }, { part: false, bone: false });

        assert.match(problem!, /has not loaded/);
        assert.match(problem!, /MuzzleA/);
    });

    it('does not invent a bone for a request that named none', () => {
        const problem = attachmentProblem({ partId: 'hp:late' }, { part: false, bone: false });

        assert.doesNotMatch(problem!, /bone '/);
    });

    it('is silent for a part-only request that resolved, whatever the bone flag says', () => {
        assert.equal(attachmentProblem({ partId: 'hull' }, { part: true, bone: false }), null);
    });
});
