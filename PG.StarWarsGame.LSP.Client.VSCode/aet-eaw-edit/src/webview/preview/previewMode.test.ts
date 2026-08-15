// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { defaultMode, otherModeChips, previewModes } from './previewMode';

const SUBJECT = { animations: 3, hardpoints: 10, particles: 4 };

describe('defaultMode', () => {
    /** What you opened decides where you land: an `.ala` is an animation, whatever else it carries. */
    it('opens an animation file in Animation mode', () => {
        assert.equal(defaultMode('Animation', SUBJECT), 'animation');
    });

    it('opens a model or an object in Model mode', () => {
        assert.equal(defaultMode('Model', SUBJECT), 'model');
        assert.equal(defaultMode('Object', SUBJECT), 'model');
    });

    /**
     * An `.ala` whose model turns out to have no clips at all still opens in Animation mode: the
     * empty list IS the answer to why nothing is moving, and bouncing to Model mode would hide it.
     */
    it('stays in Animation mode even when the subject has no clips', () => {
        assert.equal(defaultMode('Animation', { ...SUBJECT, animations: 0 }), 'animation');
    });
});

describe('previewModes', () => {
    it('offers all three, in a fixed order and at fixed angles', () => {
        const modes = previewModes(SUBJECT);

        assert.deepEqual(modes.map(mode => mode.id), ['animation', 'model', 'gameplay']);
        assert.deepEqual(modes.map(mode => mode.angle), [210, 270, 330]);
    });

    /**
     * Counted rather than hidden. The story graph omits a mode that is off, and is right to - a
     * feature flag is nothing the reader can act on. Here an empty mode is a finding about the
     * SUBJECT: "this unit declares no hardpoints" is exactly what a preview exists to surface.
     */
    it('counts what each mode has to work with, zero included', () => {
        const bare = previewModes({ animations: 0, hardpoints: 0, particles: 0 });

        assert.equal(bare.length, 3);
        assert.deepEqual(bare.map(mode => mode.count), [0, undefined, 0]);
    });

    /** Model mode has nothing to count - every subject has geometry, or there is nothing to preview. */
    it('gives the model mode no count', () => {
        assert.equal(previewModes(SUBJECT).find(mode => mode.id === 'model')?.count, undefined);
    });
});

describe('otherModeChips', () => {
    /**
     * The soft switch's safety net: leaving a mode does not stop what it started, so each mode says
     * in one line what the others are doing. Without it, an animation playing behind Model mode is
     * invisible state.
     */
    it('says what the modes you are not in are doing', () => {
        const chips = otherModeChips('model', {
            playing: 'fly_00', destroyed: 2, particleSystems: 4,
        });

        assert.deepEqual(chips, [
            { mode: 'animation', text: 'fly_00 playing' },
            { mode: 'gameplay', text: '2 hardpoints destroyed' },
        ]);
    });

    it('says nothing about a mode that is doing nothing', () => {
        assert.deepEqual(
            otherModeChips('model', { playing: null, destroyed: 0, particleSystems: 0 }), []);
    });

    /** Never a chip for the mode you are already in - its controls are right there. */
    it('leaves out the mode you are in', () => {
        const chips = otherModeChips('animation', {
            playing: 'fly_00', destroyed: 2, particleSystems: 0,
        });

        assert.deepEqual(chips.map(chip => chip.mode), ['gameplay']);
    });

    it('counts one destroyed hardpoint in the singular', () => {
        const chips = otherModeChips('model', {
            playing: null, destroyed: 1, particleSystems: 0,
        });

        assert.equal(chips[0].text, '1 hardpoint destroyed');
    });
});
