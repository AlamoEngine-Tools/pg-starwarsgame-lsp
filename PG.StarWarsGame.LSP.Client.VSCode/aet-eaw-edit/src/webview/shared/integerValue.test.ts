// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {parseInteger, stepInteger} from './integerValue';

describe('parseInteger', () => {
    it('reads a whole number, signed, with surrounding space', () => {
        assert.equal(parseInteger(' 12 '), 12);
        assert.equal(parseInteger('-3'), -3);
        assert.equal(parseInteger('0'), 0);
    });

    it('refuses anything that is not a whole number', () => {
        assert.equal(parseInteger(''), null);
        assert.equal(parseInteger('1.5'), null);
        assert.equal(parseInteger('1e3'), null);
        assert.equal(parseInteger('two'), null);
        assert.equal(parseInteger('-'), null);
    });
});

describe('stepInteger', () => {
    it('adds the step to the value in the field', () => {
        assert.equal(stepInteger('4', 1), '5');
        assert.equal(stepInteger('0', -1), '-1');
    });

    it('steps from zero when the field holds no number', () => {
        assert.equal(stepInteger('', 1), '1');
        assert.equal(stepInteger('abc', -1), '-1');
    });
});
