// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { hiddenAt, visibilityTracks } from './boneVisibility';

describe('visibilityTracks', () => {
    const json = {
        animations: [
            {
                name: 'die',
                extras: {
                    alamoVisibility: {
                        fps: 30,
                        bones: { 'P_ATST_Die#12': '0011', 'MUZZLE#3': '1100' },
                    },
                },
            },
            { name: 'idle' },
        ],
    };

    it('reads a clip`s tracks off the glTF extras', () => {
        const tracks = visibilityTracks(json);

        assert.deepEqual([...tracks.keys()], ['die']);
        assert.equal(tracks.get('die')?.fps, 30);
        // CANONICAL IDS, kept whole. What went wrong was never the keying here - it was that the
        // consumers disagreed about it. They all go through `boneIds` now.
        assert.deepEqual([...(tracks.get('die')?.bones.keys() ?? [])],
            ['P_ATST_Die#12', 'MUZZLE#3']);
    });

    it('leaves out a clip that hides nothing', () => {
        assert.equal(visibilityTracks(json).has('idle'), false);
    });

    /**
     * The extras are written by the server, but they arrive as untyped JSON through a webview
     * boundary. A shape nobody expected must cost that one clip, never the whole preview.
     */
    it('survives anything at all in place of the extras', () => {
        for (const bad of [null, undefined, 42, 'nope', [], { animations: 'no' },
            { animations: [{ name: 'x', extras: { alamoVisibility: 7 } }] },
            { animations: [{ name: 'x', extras: { alamoVisibility: { fps: 'a', bones: 1 } } }] }]) {
            assert.doesNotThrow(() => visibilityTracks(bad));
        }
    });

    /** A track with no fps cannot be turned into a frame, so it is no track at all. */
    it('refuses a track with a frame rate of zero', () => {
        const tracks = visibilityTracks({
            animations: [{ name: 'x', extras: { alamoVisibility: { fps: 0, bones: { 'a#0': '01' } } } }],
        });

        assert.equal(tracks.has('x'), false);
    });
});

describe('hiddenAt', () => {
    it('reads the frame the clip is at', () => {
        assert.equal(hiddenAt('0011', 30, 0), true);
        assert.equal(hiddenAt('0011', 30, 2 / 30), false);
    });

    /**
     * FLOOR, not round. A frame is held until the next one begins - which is what the engine does
     * and, more to the point, what stops a bone flickering on for half a frame at a boundary.
     */
    it('holds a frame until the next one starts', () => {
        assert.equal(hiddenAt('01', 30, 0.9 / 30), true, 'still on frame 0');
        assert.equal(hiddenAt('01', 30, 1.0 / 30), false, 'now on frame 1');
    });

    /** A clamped clip sits exactly on its end; the last frame is the answer, not an overrun. */
    it('clamps past the end to the last frame', () => {
        assert.equal(hiddenAt('01', 30, 99), false, 'the last frame is visible');
        assert.equal(hiddenAt('10', 30, 99), true, 'the last frame is hidden');
    });

    it('clamps a negative time to the first frame', () => {
        assert.equal(hiddenAt('01', 30, -5), true, 'frame 0 is hidden');
    });

    it('says nothing is hidden for an empty track', () => {
        assert.equal(hiddenAt('', 30, 0), false);
    });
});

describe('the node names a track is keyed by', () => {
    // The exporter disambiguates duplicate names by writing `Name#index` onto the glTF node, and
    // the skeleton strips that back off - so every lookup asks for `MuzzleA_01` while the track was
    // filed under `MuzzleA_01#22` and missed. Measured on `ev_at-at_attack_00`, whose only two
    // visibility tracks are exactly those muzzle bones: the flashes never fired.
    it('keeps a suffixed node id whole', () => {
        const tracks = visibilityTracks({
            animations: [{
                name: 'attack',
                extras: { alamoVisibility: { fps: 30, bones: { 'MuzzleA_01#22': '1111000' } } },
            }],
        });

        assert.equal(tracks.get('attack')?.bones.get('MuzzleA_01#22'), '1111000');
    });

    it('still finds one written against a bare name', () => {
        const tracks = visibilityTracks({
            animations: [{
                name: 'attack',
                extras: { alamoVisibility: { fps: 30, bones: { Barrel: '0011' } } },
            }],
        });

        assert.equal(tracks.get('attack')?.bones.get('Barrel'), '0011');
    });

    it('keeps two nodes that share a name APART', () => {
        // The whole reason the id carries an index. Stripping it collapsed these two onto one key
        // and one of the tracks was lost.
        const tracks = visibilityTracks({
            animations: [{
                name: 'attack',
                extras: { alamoVisibility: { fps: 30, bones: { 'Flash#0': '1000', 'Flash#1': '0001' } } },
            }],
        });

        assert.equal(tracks.get('attack')?.bones.get('Flash#0'), '1000');
        assert.equal(tracks.get('attack')?.bones.get('Flash#1'), '0001');
    });
});
