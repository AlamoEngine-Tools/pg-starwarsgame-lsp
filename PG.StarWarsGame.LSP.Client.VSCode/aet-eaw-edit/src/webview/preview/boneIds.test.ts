// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { boneIndexOf, displayName, resolveBoneId } from './boneIds';

describe('the canonical bone id', () => {
    it('is what the exporter writes, and carries the index', () => {
        assert.equal(displayName('MuzzleA_01#22'), 'MuzzleA_01');
        assert.equal(boneIndexOf('MuzzleA_01#22'), 22);
    });

    it('survives a name that contains a hash of its own', () => {
        // `Crusher#0#4` - the LAST hash is the exporter's.
        assert.equal(displayName('Crusher#0#4'), 'Crusher#0');
        assert.equal(boneIndexOf('Crusher#0#4'), 4);
    });

    it('treats a bare name as a name with no index', () => {
        assert.equal(displayName('Root'), 'Root');
        assert.equal(boneIndexOf('Root'), null);
    });
});

describe('resolveBoneId', () => {
    const ids = ['Root#0', 'MuzzleA_00#24', 'MuzzleA_01#22', 'Camera01.Target#9'];

    it('takes a canonical id straight through', () => {
        assert.equal(resolveBoneId(ids, 'MuzzleA_01#22'), 'MuzzleA_01#22');
    });

    it('resolves a bare name, which is how the XML and the ALA name things', () => {
        assert.equal(resolveBoneId(ids, 'MuzzleA_01'), 'MuzzleA_01#22');
    });

    it('ignores case, because the engine uppercases and the files do not', () => {
        assert.equal(resolveBoneId(ids, 'muzzlea_01'), 'MuzzleA_01#22');
    });

    it('resolves a name three stripped the dots out of', () => {
        // GLTFLoader sanitises node names - a dot is illegal in an animation property path - so a
        // model camera authored as `Camera01.Target` arrives as `Camera01Target`.
        assert.equal(resolveBoneId(ids, 'Camera01Target'), 'Camera01.Target#9');
    });

    it('has nothing to say about a name the model does not carry', () => {
        assert.equal(resolveBoneId(ids, 'NoSuchBone'), undefined);
    });

    it('prefers an exact id over a name that would also match', () => {
        // Two bones, same name, different indices: the canonical id is the only thing that can
        // separate them, and it must not be second-guessed.
        const twins = ['Flash#3', 'Flash#7'];

        assert.equal(resolveBoneId(twins, 'Flash#7'), 'Flash#7');
    });

    it('is deterministic when only a bare name is offered for twins', () => {
        const twins = ['Flash#7', 'Flash#3'];

        assert.equal(resolveBoneId(twins, 'Flash'), resolveBoneId([...twins].reverse(), 'Flash'));
    });
});
