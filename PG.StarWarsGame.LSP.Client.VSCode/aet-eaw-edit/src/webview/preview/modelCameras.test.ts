// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { cameraPose, cameraViewOptions, modelCameraEntries } from './modelCameras';

const camera = (name: string) => ({ name, position: [0, 0, 10], target: [0, 0, 0] });

describe('modelCameraEntries', () => {
    it('offers one entry per camera the model declares', () => {
        const entries = modelCameraEntries([camera('Camera01'), camera('Camera02')]);

        assert.deepEqual(entries.map(e => e.label), ['Camera01', 'Camera02']);
        assert.equal(entries.every(e => !e.disabled), true);
    });

    it('names the entry after the BONE, so it can be pasted into Lua', () => {
        // `Get_Bone_Position` is a shipped game-object method, so this exact string addresses the
        // same point from script. Prettifying it to "Author camera 1" would break that.
        assert.equal(modelCameraEntries([camera('CAMERA01')])[0].label, 'CAMERA01');
    });

    it('still offers an entry when the model declares none, disabled', () => {
        // 87% of the shipped models carry no camera. A control that vanishes teaches nothing; one
        // that is present and disabled says the feature exists and this subject has no shot.
        const entries = modelCameraEntries([]);
        const only = entries[0];

        assert.equal(entries.length, 1);
        assert.equal(only.disabled, true);
        // The REASON says there is none; the title still names what the entry would do. They were
        // one string before, which left the tooltip explaining an absence rather than a control.
        assert.match(only.disabledReason ?? '', /no camera/i);
        assert.match(only.title, /camera/i);
    });

    it('says what a camera is for in its tooltip', () => {
        assert.match(modelCameraEntries([camera('Camera01')])[0].title, /author/i);
    });

    it('keeps ids unique even if a file repeats a name', () => {
        // Nothing stops a mod exporting two bones with one name; two entries answering to the same
        // id would make the second unreachable.
        const entries = modelCameraEntries([camera('Camera01'), camera('Camera01')]);

        assert.notEqual(entries[0].id, entries[1].id);
    });
});


describe('cameraViewOptions', () => {
    const presets = [
        { view: 'threeQuarter', label: '3/4', title: 'Look from three-quarters on' },
        { view: 'front', label: 'Front', title: 'Look from the front' },
    ];

    it('is ONE list, because the camera is in one place at a time', () => {
        // The stage drew these as two groups of independent toggles, which said the reader could
        // hold a preset and an author camera at once. They cannot: `cameraView` is a single value.
        const options = cameraViewOptions(presets, [camera('Camera01')]);

        assert.deepEqual(options.map(o => o.label), ['3/4', 'Front', 'Camera01']);
    });

    it('keeps the presets first and the model`s own cameras after', () => {
        // The presets apply to any subject and the author's camera belongs to this one, so the
        // list runs from what is always there to what this file happens to carry.
        const options = cameraViewOptions(presets, [camera('Camera01')]);

        assert.equal(options[options.length - 1].id, 'camera:0');
    });

    // A preset applies to any subject, so it mentions no disabled state at all - which is what the
    // shared enablement rule means by "not disableable" rather than "disabled: false".
    it('never disables a preset', () => {
        const options = cameraViewOptions(presets, []);
        const preset = options.filter(o => o.id === 'threeQuarter')[0];

        assert.notEqual(preset.disabled, true);
    });

    /**
     * And it says WHY, in its own field rather than by leaving the title to do two jobs. The title
     * names what the entry would do; the reason names why it cannot right now.
     */
    it('carries the disabled author entry through when the model declares none', () => {
        const options = cameraViewOptions(presets, []);
        const last = options[options.length - 1];

        assert.equal(last.disabled, true);
        assert.match(last.disabledReason ?? '', /no camera/i);
    });

    it('gives every option a distinct id, so exactly one can read as chosen', () => {
        const options = cameraViewOptions(presets, [camera('Camera01'), camera('Camera01')]);
        const ids = new Set(options.map(o => o.id));

        assert.equal(ids.size, options.length);
    });
});

describe('a camera entry carries its POSE, not just its name', () => {
    /**
     * The panel used to re-find the camera in the loaded model by name, looking for a bone called
     * `Camera01` and one called `Camera01.Target`. The bone in the GLB is `Camera01Target` - the
     * dot does not survive the export - so the pair never resolved and pressing the entry did
     * nothing at all, silently, on every model that has one.
     *
     * The server already resolves both ends and sends them. Carrying the numbers through is both
     * simpler and the only version that cannot be broken by a name.
     */
    it('carries the position and target the server resolved', () => {
        const entries = modelCameraEntries([{
            name: 'Camera01',
            position: [-375.47, -372.2, 213.04],
            target: [-86.85, 101.88, -131.61],
        }]);

        assert.deepEqual(entries[0].position, [-375.47, -372.2, 213.04]);
        assert.deepEqual(entries[0].target, [-86.85, 101.88, -131.61]);
    });

    it('leaves the pose absent on the entry that stands in for none', () => {
        const [none] = modelCameraEntries([]);

        assert.equal(none.disabled, true);
        assert.equal(none.position, null);
    });
});

describe('cameraPose', () => {
    /**
     * The pose in the SCENE's axes, which is not the axes the server sends.
     *
     * `PreviewCamera` is MODEL space - Alamo, Z up - and the exporter turns the geometry into
     * glTF's Y up with a rotation on the root node. Applied raw the camera lands a quarter turn
     * out: measured on RB_CommandCenter, the eye went to (-375, -372, 213) instead of
     * (-375, 213, 372), which puts it under the floor looking back at the model from the wrong
     * side - and reads exactly like the eye and the target having been swapped.
     */
    it('turns Alamo Z-up into the scene Y-up, for both ends', () => {
        const pose = cameraPose({
            position: [-375, -372, 213],
            target: [-87, 102, -132],
        });

        assert.deepEqual(pose?.position, { x: -375, y: 213, z: 372 });
        assert.deepEqual(pose?.target, { x: -87, y: -132, z: -102 });
    });

    it('is null when either end is missing, so nothing is aimed at the origin', () => {
        assert.equal(cameraPose({ position: [1, 2, 3] }), null);
        assert.equal(cameraPose({ target: [1, 2, 3] }), null);
    });

    it('is null for a pose that is not three numbers', () => {
        assert.equal(cameraPose({ position: [1, 2], target: [1, 2, 3] }), null);
    });
});
