// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { StoryParamSchemaDto } from '../../protocol';
import { booleanParamLabel, shortParamLabel } from './paramLabels';

function withDescription(description: string | null): StoryParamSchemaDto {
    return { position: 0, valueType: 'string', optional: false, description };
}

describe('shortParamLabel', () => {
    it('drops the trailing full stop', () => {
        assert.equal(shortParamLabel(withDescription('Attacker faction.')), 'Attacker faction');
    });

    it('takes only the phrase before the first clause break', () => {
        assert.equal(
            shortParamLabel(withDescription('Target planet, by name; must exist.')),
            'Target planet');
        assert.equal(
            shortParamLabel(withDescription('Delay (in seconds) before firing.')),
            'Delay');
    });

    // The caller falls back to "Param N", which is better than an empty label.
    it('gives up rather than return nothing usable', () => {
        assert.equal(shortParamLabel(undefined), null);
        assert.equal(shortParamLabel(withDescription(null)), null);
        assert.equal(shortParamLabel(withDescription('   ')), null);
        assert.equal(shortParamLabel(withDescription('. Leading punctuation.')), null);
    });
});

describe('booleanParamLabel', () => {
    // The checkbox already encodes the mechanics, so repeating "1 = " beside it is noise.
    it('strips the numeric prefix and reads as a statement', () => {
        assert.equal(
            booleanParamLabel('1 = loop the movie; 0 = play it once'),
            'Loop the movie');
        assert.equal(booleanParamLabel('0 = hide the marker; 1 = show it'), 'Hide the marker');
    });

    it('tolerates the spacing the schema happens to use', () => {
        assert.equal(booleanParamLabel('1=enable the shield'), 'Enable the shield');
        assert.equal(booleanParamLabel('  1  =  enable the shield  '), 'Enable the shield');
    });

    it('stops at the first clause break', () => {
        assert.equal(booleanParamLabel('1 = loop the movie. Ignored otherwise'), 'Loop the movie');
        assert.equal(booleanParamLabel('1 = loop (see below)'), 'Loop');
    });

    // Anything not in the 0/1 shape is a description of something else - the caller uses the
    // generic label rather than mangling prose into a checkbox caption.
    it('declines a description of another shape', () => {
        assert.equal(booleanParamLabel(null), null);
        assert.equal(booleanParamLabel(undefined), null);
        assert.equal(booleanParamLabel('Whether to loop the movie'), null);
        assert.equal(booleanParamLabel('2 = something odd'), null);
    });
});
