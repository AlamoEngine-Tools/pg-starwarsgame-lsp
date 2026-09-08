// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { bindingFor, type CameraBinding, type PreviewSubject } from './cameraBindings';

const subject: PreviewSubject = {
    objectId: 'Rebel_Xwing',
    objectType: 'Fighter',
    categories: ['Fighter', 'AntiFighter'],
};

const bind = (kind: CameraBinding['kind'], value: string, presetId: string): CameraBinding =>
    ({ id: `${kind}:${value}`, kind, value, presetId });

describe('bindingFor', () => {
    it('finds nothing when nothing is bound', () => {
        assert.equal(bindingFor(subject, []), null);
    });

    it('matches an object by id', () => {
        const rule = bind('object', 'Rebel_Xwing', 'p1');

        assert.equal(bindingFor(subject, [rule])?.presetId, 'p1');
    });

    it('matches an object by its type', () => {
        assert.equal(bindingFor(subject, [bind('type', 'Fighter', 'p2')])?.presetId, 'p2');
    });

    it('matches any ONE of the subject`s categories', () => {
        // CategoryMask is multi-valued - `Vehicle | AntiInfantry | AntiVehicle` - so a rule tests
        // membership rather than equality. That is the whole reason it cannot be a lookup key.
        assert.equal(bindingFor(subject, [bind('category', 'AntiFighter', 'p3')])?.presetId, 'p3');
    });

    it('lets an object id beat a type and a category', () => {
        // The per-object override always wins: it is the most specific thing anyone can say, and a
        // reader who bound one unit meant that unit.
        const rules = [
            bind('category', 'AntiFighter', 'category'),
            bind('type', 'Fighter', 'type'),
            bind('object', 'Rebel_Xwing', 'object'),
        ];

        assert.equal(bindingFor(subject, rules)?.presetId, 'object');
    });

    it('lets a type beat a category', () => {
        const rules = [bind('category', 'AntiFighter', 'category'), bind('type', 'Fighter', 'type')];

        assert.equal(bindingFor(subject, rules)?.presetId, 'type');
    });

    it('takes the FIRST rule when two of the same kind match', () => {
        // The list is ordered, and the reader ordered it. Picking the last would make adding a rule
        // silently change what an existing one does.
        const rules = [
            bind('category', 'Fighter', 'first'),
            bind('category', 'AntiFighter', 'second'),
        ];

        assert.equal(bindingFor(subject, rules)?.presetId, 'first');
    });

    it('matches case-insensitively, as the XML is written every which way', () => {
        assert.equal(bindingFor(subject, [bind('object', 'rebel_xwing', 'p')])?.presetId, 'p');
        assert.equal(bindingFor(subject, [bind('category', 'antifighter', 'q')])?.presetId, 'q');
    });

    it('binds nothing to a bare model, which has no game object at all', () => {
        // Previewing an .alo directly: there is no id, no type and no category to match on, and a
        // rule that fired anyway would be framing a shot for a unit that is not there.
        const bare: PreviewSubject = { objectId: null, objectType: null, categories: [] };

        assert.equal(bindingFor(bare, [bind('type', 'Fighter', 'p')]), null);
    });

    it('ignores a rule with nothing to match on', () => {
        assert.equal(bindingFor(subject, [bind('type', '', 'p')]), null);
    });
});
