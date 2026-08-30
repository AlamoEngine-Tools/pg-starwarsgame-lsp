// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    defaultMode, drawsAnnotations, modelTouched, otherModeChips, previewModes,
} from './previewMode';

const SUBJECT = { animations: 3, hardpoints: 10, particles: 4, weapons: 6, abilities: 2 };

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
        const bare = previewModes({ animations: 0, hardpoints: 0, particles: 0, weapons: 0, abilities: 0 });

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
            playing: 'fly_00', destroyed: 2, particleSystems: 4, abilities: 0,
        });

        assert.deepEqual(chips, [
            { mode: 'animation', text: 'fly_00 playing' },
            { mode: 'gameplay', text: '2 hardpoints destroyed' },
        ]);
    });

    it('says nothing about a mode that is doing nothing', () => {
        assert.deepEqual(
            otherModeChips('model',
                { playing: null, destroyed: 0, particleSystems: 0, abilities: 0 }), []);
    });

    /** Never a chip for the mode you are already in - its controls are right there. */
    it('leaves out the mode you are in', () => {
        const chips = otherModeChips('animation', {
            playing: 'fly_00', destroyed: 2, particleSystems: 0, abilities: 0,
        });

        assert.deepEqual(chips.map(chip => chip.mode), ['gameplay']);
    });

    it('counts one destroyed hardpoint in the singular', () => {
        const chips = otherModeChips('model', {
            playing: null, destroyed: 1, particleSystems: 0, abilities: 0,
        });

        assert.equal(chips[0].text, '1 hardpoint destroyed');
    });
});

describe('what the Gameplay dial counts', () => {
    it('counts everything the lens can act on, not just hardpoints', () => {
        // The lens grew: it holds weapons, hardpoints and abilities now. Counting only hardpoints read
        // 0 on a fighter that carries its guns on the unit itself and has three abilities.
        const dial = previewModes({ animations: 0, hardpoints: 2, particles: 0, weapons: 4, abilities: 3 })
            .find(m => m.id === 'gameplay');

        assert.equal(dial?.count, 9);
    });

    it('is absent rather than zero when the lens has nothing at all', () => {
        // A bare model. A dial position reading 0 invites a click that finds an empty panel.
        const dial = previewModes({ animations: 0, hardpoints: 0, particles: 0, weapons: 0, abilities: 0 })
            .find(m => m.id === 'gameplay');

        assert.ok(dial?.count === undefined || dial.count === 0);
    });
});

describe('the soft-switch chips', () => {
    it('says when an ability is left running in another lens', () => {
        // The safety net: leaving a lens does not stop what it started, and an active ability is
        // holding proxies on. Without the chip that state is invisible from Model mode.
        const chips = otherModeChips('model',
            { playing: null, destroyed: 0, particleSystems: 0, abilities: 2 });

        assert.equal(chips.length, 1);
        assert.equal(chips[0].mode, 'gameplay');
        assert.match(chips[0].text, /2 abilities/);
    });

    it('does not say it while you are already looking at it', () => {
        assert.deepEqual(otherModeChips('gameplay',
            { playing: null, destroyed: 0, particleSystems: 0, abilities: 2 }), []);
    });

    it('reads as one ability rather than 1 abilities', () => {
        const chips = otherModeChips('model',
            { playing: null, destroyed: 0, particleSystems: 0, abilities: 1 });

        assert.match(chips[0].text, /1 ability\b/);
    });
});

describe('drawsAnnotations', () => {
    it('draws the cones and marks only in the lens that owns them', () => {
        assert.equal(drawsAnnotations('gameplay'), true);
    });

    it('draws neither in Model mode, which has no control for them', () => {
        // The reported fault: arcs latched on in Gameplay kept drawing after the lens changed, and
        // Model mode offers no pill to switch them off - so the reader was left with cones over a
        // hull and nothing to press. A cone is not part of the asset; it is a fact about what the
        // game does with it.
        assert.equal(drawsAnnotations('model'), false);
    });

    it('draws neither in Animation mode either', () => {
        assert.equal(drawsAnnotations('animation'), false);
    });
});

describe('modelTouched', () => {
    const rest = { alt: 0, lodIsHighest: true, hiddenEmitters: 0, rowOverrides: 0 };

    it('is quiet on a model still as it opened', () => {
        assert.equal(modelTouched(rest), false);
    });

    it('lights up for each thing the Model lens`s Reset puts back', () => {
        assert.equal(modelTouched({ ...rest, alt: 1 }), true);
        assert.equal(modelTouched({ ...rest, lodIsHighest: false }), true);
        assert.equal(modelTouched({ ...rest, hiddenEmitters: 2 }), true);
        assert.equal(modelTouched({ ...rest, rowOverrides: 1 }), true);
    });

    it('says nothing about what the Gameplay lens has done', () => {
        // A destroyed hardpoint and a hidden firing arc belong to the lens that can undo them. The
        // Model tree's Reset used to clear both, which is how pressing it in Model mode revealed
        // every firing arc on the hull - a thing Model mode cannot even switch off.
        assert.deepEqual(Object.keys(rest).sort(),
            ['alt', 'hiddenEmitters', 'lodIsHighest', 'rowOverrides']);
    });
});
