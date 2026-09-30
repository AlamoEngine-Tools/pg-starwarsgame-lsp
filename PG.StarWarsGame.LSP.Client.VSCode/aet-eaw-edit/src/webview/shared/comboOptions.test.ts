// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {isCurrentOption, moveHighlight, opensUpward, queryFor, resolveChoice, visibleOptions} from './comboOptions';

const PLANETS = [{value: 'Kuat'}, {value: 'Hoth'}, {value: 'Coruscant'}, {value: 'Kashyyyk'}];

describe('visibleOptions', () => {
    /**
     * The bug this module exists for: a field holding a value opened a list filtered by that value,
     * so the only option on offer was the one already chosen.
     */
    it('offers every option while the text is unedited since the list opened', () => {
        assert.deepEqual(visibleOptions(PLANETS, 'Hoth', false).map(o => o.value),
            ['Kuat', 'Hoth', 'Coruscant', 'Kashyyyk']);
    });

    it('filters by the typed text once it has been edited, ignoring case, in the given order', () => {
        assert.deepEqual(visibleOptions(PLANETS, 'k', true).map(o => o.value), ['Kuat', 'Kashyyyk']);
        assert.deepEqual(visibleOptions(PLANETS, 'OTH', true).map(o => o.value), ['Hoth']);
    });

    it('offers everything again when the edited text is cleared', () => {
        assert.equal(visibleOptions(PLANETS, '  ', true).length, 4);
    });
});

describe('queryFor', () => {
    it('asks a server for everything until the text is edited, then for the typed prefix', () => {
        assert.equal(queryFor('Story_Flag_A', false), '');
        assert.equal(queryFor('Story_', true), 'Story_');
    });
});

describe('isCurrentOption', () => {
    it('marks the option equal to the field value, ignoring case and surrounding space', () => {
        assert.equal(isCurrentOption({value: 'Hoth'}, ' hoth '), true);
        assert.equal(isCurrentOption({value: 'Hoth'}, 'Ho'), false);
        assert.equal(isCurrentOption({value: 'Hoth'}, ''), false);
    });
});

describe('resolveChoice', () => {
    const LIFECYCLE = [{value: 'Armed'}, {value: 'Fired'}, {value: 'Disabled'}];

    /** A select-only field takes one of its options or nothing: typed text is a search, never a value. */
    it('takes the option the text names exactly, ignoring case', () => {
        assert.equal(resolveChoice(LIFECYCLE, 'fired'), 'Fired');
    });

    it('takes the only option the text still matches', () => {
        assert.equal(resolveChoice(LIFECYCLE, 'arm'), 'Armed');
    });

    it('takes nothing when the text matches several options or none', () => {
        assert.equal(resolveChoice(LIFECYCLE, 'ed'), null);
        assert.equal(resolveChoice(LIFECYCLE, 'Pending'), null);
    });
});

describe('opensUpward', () => {
    /** A field at the foot of the dock has no room under it: its list opens above instead. */
    it('opens above when the room below is short of the list and above has more', () => {
        assert.equal(opensUpward({top: 800, bottom: 824}, 900, 160), true);
    });

    it('opens below when there is room, and below when neither side has enough', () => {
        assert.equal(opensUpward({top: 100, bottom: 124}, 900, 160), false);
        assert.equal(opensUpward({top: 60, bottom: 84}, 150, 160), false);
    });
});

describe('moveHighlight', () => {
    it('steps through the list and wraps at both ends', () => {
        assert.equal(moveHighlight(-1, 1, 3), 0);
        assert.equal(moveHighlight(2, 1, 3), 0);
        assert.equal(moveHighlight(0, -1, 3), 2);
        assert.equal(moveHighlight(-1, -1, 3), 2);
    });

    it('has nothing to highlight in an empty list', () => {
        assert.equal(moveHighlight(0, 1, 0), -1);
    });
});
