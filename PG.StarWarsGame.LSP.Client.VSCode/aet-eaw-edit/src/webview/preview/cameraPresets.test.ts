// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    luaFor, poseFromPreset, presetFromPose, type CameraPreset,
} from './cameraPresets';

const sphere = { center: { x: 0, y: 0, z: 0 }, radius: 10 };
const preset = (over: Partial<CameraPreset> = {}): CameraPreset => ({
    id: 'p1', name: 'Icon', distance: 3, pitch: 30, yaw: 45, bone: null, ...over,
});

describe('poseFromPreset', () => {
    it('puts the camera the stored number of RADII out', () => {
        // Stored relative, so one preset frames a trooper and a Star Destroyer the same way. An
        // absolute distance would be a different shot on every subject, which defeats the point of
        // a preset in the first place.
        const pose = poseFromPreset(preset({ distance: 3, pitch: 0, yaw: 0 }), sphere);

        assert.equal(Math.round(Math.hypot(
            pose.position.x - pose.target.x,
            pose.position.y - pose.target.y,
            pose.position.z - pose.target.z)), 30);
    });

    it('reads yaw the way the presets and the light rig already do', () => {
        // Yaw 0 is +Z, which is where the Front view stands. One convention across the whole panel.
        const pose = poseFromPreset(preset({ distance: 1, pitch: 0, yaw: 0 }), sphere);

        assert.ok(pose.position.z > 9, `z was ${pose.position.z}`);
        assert.ok(Math.abs(pose.position.x) < 1e-6);
    });

    it('lifts the camera for a positive pitch', () => {
        const pose = poseFromPreset(preset({ distance: 1, pitch: 90, yaw: 0 }), sphere);

        assert.ok(pose.position.y > 9, `y was ${pose.position.y}`);
    });

    it('drops it below for a negative pitch, as the engine allows', () => {
        // Shipped keys use negative pitch freely - `(Tyranny, 250, -30, 150, ...)`.
        assert.ok(poseFromPreset(preset({ distance: 1, pitch: -30, yaw: 0 }), sphere).position.y < 0);
    });

    it('looks at the subject centre', () => {
        const pose = poseFromPreset(preset(), { center: { x: 5, y: 6, z: 7 }, radius: 2 });

        assert.deepEqual(pose.target, { x: 5, y: 6, z: 7 });
    });

    it('never collapses onto a subject with no measurable size', () => {
        // A radius of zero would put the camera inside the model and show nothing at all.
        const pose = poseFromPreset(preset(), { center: { x: 0, y: 0, z: 0 }, radius: 0 });

        assert.ok(Math.hypot(pose.position.x, pose.position.y, pose.position.z) > 0);
    });
});

describe('presetFromPose', () => {
    it('round-trips a pose it just produced', () => {
        // Save-then-apply has to land where it was saved from, or the button lies.
        const original = preset({ distance: 2.5, pitch: 25, yaw: 200 });
        const pose = poseFromPreset(original, sphere);
        const saved = presetFromPose('Icon', pose.position, sphere);

        assert.ok(Math.abs(saved.distance - 2.5) < 1e-4, `distance ${saved.distance}`);
        assert.ok(Math.abs(saved.pitch - 25) < 1e-4, `pitch ${saved.pitch}`);
        assert.ok(Math.abs(saved.yaw - 200) < 1e-4, `yaw ${saved.yaw}`);
    });

    it('reports yaw in 0 to 360, as the shipped keys are written', () => {
        const saved = presetFromPose('x', { x: -1, y: 0, z: 0 }, sphere);

        assert.ok(saved.yaw >= 0 && saved.yaw < 360, `yaw ${saved.yaw}`);
    });

    it('takes the name it was given', () => {
        assert.equal(presetFromPose('Roster shot', { x: 0, y: 0, z: 10 }, sphere).name, 'Roster shot');
    });

    it('gives every preset an id of its own', () => {
        const a = presetFromPose('a', { x: 0, y: 0, z: 10 }, sphere);
        const b = presetFromPose('a', { x: 0, y: 0, z: 10 }, sphere);

        assert.notEqual(a.id, b.id);
    });
});

describe('luaFor', () => {
    it('emits a Set_Cinematic_Camera_Key the engine would accept', () => {
        // Argument order read off the shipped campaign scripts:
        // `Set_Cinematic_Camera_Key(acclamator, 300, 5, 100, 1, 0, 0, 0)`.
        const lua = luaFor(preset({ distance: 3, pitch: 5, yaw: 100 }), sphere, 'acclamator');

        assert.equal(lua, 'Set_Cinematic_Camera_Key(acclamator, 30, 5, 100, 1, 0, 0, 0)');
    });

    it('resolves the distance for THIS subject, because the engine wants world units', () => {
        // The preset is relative; the script is not. A key written for a trooper is not a key for a
        // Star Destroyer, so the number that goes out is the one this subject needs.
        const lua = luaFor(preset({ distance: 2 }), { center: { x: 0, y: 0, z: 0 }, radius: 125 },
            'ship');

        assert.match(lua, /\(ship, 250,/);
    });

    it('rounds to something a person would type', () => {
        const lua = luaFor(preset({ distance: 1 / 3, pitch: 1 / 7, yaw: 2 / 3 }), sphere, 'x');

        assert.doesNotMatch(lua, /\d\.\d{3}/);
    });

    it('falls back to a placeholder when there is no object name to use', () => {
        // A model preview has no game object; the line is still worth copying with an obvious hole.
        assert.match(luaFor(preset(), sphere, null), /Set_Cinematic_Camera_Key\(<object>/);
    });
});
