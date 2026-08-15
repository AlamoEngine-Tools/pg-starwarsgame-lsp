// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { modelCameraEntries } from './modelCameras';

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
        assert.match(only.title, /does not|no camera/i);
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

