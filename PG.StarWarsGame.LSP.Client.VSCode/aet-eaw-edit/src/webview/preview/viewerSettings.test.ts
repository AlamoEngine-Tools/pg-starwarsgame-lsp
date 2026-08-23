// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import {
    colourFromHex, hexFromColour, DEFAULT_VIEWER_SETTINGS, SETTINGS_VERSION, viewerSettingsFrom,
} from './viewerSettings';

describe('colours across the control boundary', () => {
    it('round-trips a colour through the picker`s hex', () => {
        for (const hex of ['#000000', '#ffffff', '#3f7fbf']) {
            assert.equal(hexFromColour(colourFromHex(hex)), hex);
        }
    });

    /** The picker only ever sends six hex digits, but the value also arrives from storage. */
    it('takes an unreadable colour as white rather than as three NaNs', () => {
        for (const junk of ['', 'red', '#12345', 'rgb(1,2,3)']) {
            assert.deepEqual(colourFromHex(junk), [1, 1, 1], junk);
        }
    });

    it('clamps rather than wrapping when a channel is out of range', () => {
        assert.equal(hexFromColour([2, -1, 0.5]), '#ff0080');
    });
});

describe('viewerSettingsFrom', () => {
    it('gives the defaults for a workspace that has never opened a preview', () => {
        assert.deepEqual(viewerSettingsFrom(undefined), DEFAULT_VIEWER_SETTINGS);
        assert.deepEqual(viewerSettingsFrom(null), DEFAULT_VIEWER_SETTINGS);
    });

    it('keeps what was stored', () => {
        const stored = viewerSettingsFrom({ grid: false, particleSpeed: 0.25 });

        assert.equal(stored.grid, false);
        assert.equal(stored.particleSpeed, 0.25);
    });

    /**
     * The store outlives the code that wrote it. A settings blob from an older build is MISSING
     * fields rather than wrong, and every one of those has to fall back rather than arrive as
     * `undefined` and switch a control into an uncontrolled React input.
     */
    it('fills in anything an older build never wrote', () => {
        const partial = viewerSettingsFrom({ grid: false });

        assert.equal(partial.floor, DEFAULT_VIEWER_SETTINGS.floor);
        assert.deepEqual(partial.lights, DEFAULT_VIEWER_SETTINGS.lights);
    });

    /**
     * And it can be hand-edited or corrupted. Nothing here may throw: the preview would open to a
     * blank panel over a bad string in a settings file, with no way to get back.
     */
    it('refuses a value of the wrong type rather than passing it on', () => {
        const nonsense = viewerSettingsFrom({
            grid: 'yes', particleSpeed: 'fast', background: 42, lights: 'bright',
        });

        assert.deepEqual(nonsense, DEFAULT_VIEWER_SETTINGS);
    });

    it('survives a stored value that is not an object at all', () => {
        for (const junk of ['', 7, [], true]) {
            assert.deepEqual(viewerSettingsFrom(junk), DEFAULT_VIEWER_SETTINGS);
        }
    });

    it('refuses a background this build does not know', () => {
        assert.equal(viewerSettingsFrom({ background: 'nebula' }).background,
            DEFAULT_VIEWER_SETTINGS.background);
        assert.equal(viewerSettingsFrom({ background: 'starfield' }).background, 'starfield');
    });

    /** A slider that stored NaN or a negative would put the camera or the light somewhere absurd. */
    it('clamps a number into the range its control offers', () => {
        assert.equal(viewerSettingsFrom({ particleSpeed: -4 }).particleSpeed, 0);
        assert.equal(viewerSettingsFrom({ particleSpeed: 500 }).particleSpeed, 4);
        assert.equal(viewerSettingsFrom({ drawDistance: 0 }).drawDistance, 1);
        assert.equal(viewerSettingsFrom({ drawDistance: Number.NaN }).drawDistance,
            DEFAULT_VIEWER_SETTINGS.drawDistance);
    });

    /**
     * One wind, three consumers: the tree bend, the grass, and the 484 of 1019 emitters that set
     * `affectedByWind`. AloViewer exposes exactly these two numbers.
     */
    it('carries a wind heading and speed', () => {
        assert.equal(typeof DEFAULT_VIEWER_SETTINGS.wind.heading, 'number');
        assert.equal(typeof DEFAULT_VIEWER_SETTINGS.wind.speed, 'number');

        const stored = viewerSettingsFrom({ wind: { heading: 200, speed: 3 } });

        assert.equal(stored.wind.heading, 200);
        assert.equal(stored.wind.speed, 3);
    });

    it('clamps a wind that would blow the corpus away', () => {
        assert.equal(viewerSettingsFrom({ wind: { speed: -5 } }).wind.speed, 0);
        assert.deepEqual(viewerSettingsFrom({ wind: 'gale' }).wind, DEFAULT_VIEWER_SETTINGS.wind);
    });

    describe('the light rig', () => {
        /** AloViewer's own shape: a sun and two fills, each with a heading, a tilt and a colour. */
        it('carries three directional lights and the global terms', () => {
            const { lights } = DEFAULT_VIEWER_SETTINGS;

            assert.deepEqual(Object.keys(lights).sort(),
                ['ambient', 'fill1', 'fill2', 'shadow', 'specular', 'sun']);
            assert.equal(typeof lights.sun.azimuth, 'number');
            assert.equal(typeof lights.sun.elevation, 'number');
            assert.equal(lights.sun.colour.length, 3);
        });

        it('takes a stored light and leaves the others alone', () => {
            const stored = viewerSettingsFrom({
                lights: { sun: { azimuth: 200, colour: [1, 0.5, 0] } },
            });

            assert.equal(stored.lights.sun.azimuth, 200);
            assert.deepEqual(stored.lights.sun.colour, [1, 0.5, 0]);
            assert.equal(stored.lights.sun.elevation, DEFAULT_VIEWER_SETTINGS.lights.sun.elevation);
            assert.deepEqual(stored.lights.fill1, DEFAULT_VIEWER_SETTINGS.lights.fill1);
        });

        it('refuses a colour that is not three numbers in range', () => {
            for (const bad of [[1, 0], [1, 0, 0, 0], ['a', 'b', 'c'], [2, -1, 0]]) {
                assert.deepEqual(
                    viewerSettingsFrom({ lights: { sun: { colour: bad } } }).lights.sun.colour,
                    DEFAULT_VIEWER_SETTINGS.lights.sun.colour, JSON.stringify(bad));
            }
        });
    });
});

describe('viewport fidelity settings', () => {
    it('opens with the ground at zero and no wireframe', () => {
        assert.equal(DEFAULT_VIEWER_SETTINGS.floorLevel, 0);
        assert.equal(DEFAULT_VIEWER_SETTINGS.wireframe, false);
    });

    it('takes a ground height above or below the origin', () => {
        assert.equal(viewerSettingsFrom({ floorLevel: 25 }).floorLevel, 25);
        assert.equal(viewerSettingsFrom({ floorLevel: -40 }).floorLevel, -40);
    });

    it('keeps a stored ground height inside a range a model can be found in', () => {
        // AloViewer offers -10000..10000, and past that the floor is somewhere no camera fitted to
        // a model will ever look - which reads as the ground having vanished.
        assert.equal(viewerSettingsFrom({ floorLevel: 99999 }).floorLevel, 10000);
        assert.equal(viewerSettingsFrom({ floorLevel: -99999 }).floorLevel, -10000);
    });

    it('draws heat by default and debugs it by request', () => {
        // Heat was unconditional before there was a switch, so the default has to keep it on or
        // turning the setting into a control would silently change what every preview draws.
        assert.equal(DEFAULT_VIEWER_SETTINGS.heat, true);
        assert.equal(DEFAULT_VIEWER_SETTINGS.heatDebug, false);
    });

    it('leaves bloom off, as the engine does', () => {
        assert.equal(DEFAULT_VIEWER_SETTINGS.bloom, false);
    });

    it('carries a shadow colour with the rest of the global light terms', () => {
        // AloViewer keeps it in the same group as ambient and specular, because it is the same kind
        // of thing: one global term, not a property of any one light.
        //
        // The engine's own value, and it means the same thing here: the stencil darken MULTIPLIES
        // what is under it, so 0.5 is half as bright rather than a flat grey patch.
        assert.deepEqual(DEFAULT_VIEWER_SETTINGS.lights.shadow, [0.5, 0.5, 0.5]);
    });

    it('takes a stored shadow colour and refuses a broken one', () => {
        // Versioned, because an unversioned blob predates the shadow colour changing meaning.
        assert.deepEqual(
            viewerSettingsFrom({ version: SETTINGS_VERSION, lights: { shadow: [0, 0, 1] } })
                .lights.shadow, [0, 0, 1]);
        assert.deepEqual(
            viewerSettingsFrom({ lights: { shadow: 'blue' } }).lights.shadow,
            DEFAULT_VIEWER_SETTINGS.lights.shadow);
    });

    it('survives a blob written before any of this existed', () => {
        // The stored blob outlives the build that wrote it, which is the whole reason this defaults
        // field by field rather than all or nothing.
        const old = viewerSettingsFrom({ grid: false, faction: 'Empire' });

        assert.equal(old.grid, false);
        assert.equal(old.floorLevel, DEFAULT_VIEWER_SETTINGS.floorLevel);
        assert.equal(old.heat, DEFAULT_VIEWER_SETTINGS.heat);
        assert.deepEqual(old.lights.shadow, DEFAULT_VIEWER_SETTINGS.lights.shadow);
    });
});

describe('the default light rig is the engine`s own', () => {
    const { lights } = DEFAULT_VIEWER_SETTINGS;

    it('uses the engine`s angles', () => {
        // `Z Angle` and `Tilt` in AloViewer's Nature panel, straight out of GetDefaultEnvironment.
        assert.deepEqual([lights.sun.azimuth, lights.sun.elevation], [0, 45]);
        assert.deepEqual([lights.fill1.azimuth, lights.fill1.elevation], [210, -10]);
        assert.deepEqual([lights.fill2.azimuth, lights.fill2.elevation], [120, -10]);
    });

    it('halves the sun rather than over-driving it', () => {
        // The engine multiplies a light's colour by its alpha, and the sun ships at alpha 0.5, so
        // its diffuse is mid grey. A 1.6 white sun - what this used to default to - is over three
        // times the brightest the tool even lets you ask for, and it blew every hull out to white.
        assert.equal(lights.sun.intensity, 0.5);
        assert.deepEqual(lights.sun.colour, [1, 1, 1]);
    });

    it('keeps the fills dark and BLUE, which is what stops the model reading as flat white', () => {
        for (const fill of [lights.fill1, lights.fill2]) {
            assert.deepEqual(fill.colour, [0.25, 0.25, 0.5]);
            assert.equal(fill.intensity, 0.5);
        }
    });

    it('leaves the ambient at a tenth', () => {
        assert.equal(lights.ambient.intensity, 0.1);
    });
});

describe('a setting whose MEANING changed has to be migrated', () => {
    it('drops a shadow colour stored before the darken multiplied', () => {
        // B3 shipped the shadow colour as the colour DRAWN over the ground, so its default was
        // near-black. The stencil darken multiplies instead, and near-black multiplied is black -
        // it turned whole models into silhouettes. The value is still a perfectly valid colour, so
        // nothing but the version can tell that it means something else now.
        const old = viewerSettingsFrom({ lights: { shadow: [0.06, 0.06, 0.08] } });

        assert.deepEqual(old.lights.shadow, DEFAULT_VIEWER_SETTINGS.lights.shadow);
    });

    it('keeps a shadow colour stored under the current meaning', () => {
        const current = viewerSettingsFrom({
            version: SETTINGS_VERSION, lights: { shadow: [0.2, 0.1, 0.1] },
        });

        assert.deepEqual(current.lights.shadow, [0.2, 0.1, 0.1]);
    });

    it('leaves everything else an older build stored alone', () => {
        // A version bump is not a reset. Only what changed meaning is dropped.
        const old = viewerSettingsFrom({ grid: false, particleSpeed: 0.25 });

        assert.equal(old.grid, false);
        assert.equal(old.particleSpeed, 0.25);
    });
});

describe('camera presets', () => {
    const good = { id: 'p1', name: 'Icon', distance: 3, pitch: 20, yaw: 45, bone: null };

    it('starts with none', () => {
        assert.deepEqual(viewerSettingsFrom({}).cameraPresets, []);
    });

    it('reads a stored list back', () => {
        assert.deepEqual(viewerSettingsFrom({ cameraPresets: [good] }).cameraPresets, [good]);
    });

    it('drops an entry that is not a preset rather than throwing', () => {
        // Tier 1 storage outlives any one version of this panel, so it has to survive whatever is
        // in it - a half-written preset must not take the whole settings object down with it.
        const settings = viewerSettingsFrom({
            cameraPresets: [good, null, 'nope', { name: 'no id' }, { id: 'x' }],
        });

        assert.deepEqual(settings.cameraPresets, [good]);
    });

    it('reads a list that is not a list as none', () => {
        assert.deepEqual(viewerSettingsFrom({ cameraPresets: 'Icon' }).cameraPresets, []);
    });

    it('keeps a bone target when one is stored', () => {
        const settings = viewerSettingsFrom({
            cameraPresets: [{ ...good, bone: 'HP_Turret_01' }],
        });

        assert.equal(settings.cameraPresets[0].bone, 'HP_Turret_01');
    });

    it('refuses a distance that would put the camera inside the model', () => {
        // Zero or negative is not a shot; it is a camera at the origin looking at itself.
        const settings = viewerSettingsFrom({ cameraPresets: [{ ...good, distance: 0 }] });

        assert.deepEqual(settings.cameraPresets, []);
    });

    it('refuses an angle that is not a number', () => {
        assert.deepEqual(
            viewerSettingsFrom({ cameraPresets: [{ ...good, pitch: 'up' }] }).cameraPresets, []);
    });
});

describe('camera bindings', () => {
    const good = { id: 'b1', kind: 'type', value: 'Fighter', presetId: 'p1' };

    it('starts with none', () => {
        assert.deepEqual(viewerSettingsFrom({}).cameraBindings, []);
    });

    it('reads a stored rule back', () => {
        assert.deepEqual(viewerSettingsFrom({ cameraBindings: [good] }).cameraBindings, [good]);
    });

    it('refuses a rule whose kind is not one of the three', () => {
        // A rule nobody can evaluate is worse than no rule: it would sit in the list looking bound.
        assert.deepEqual(
            viewerSettingsFrom({ cameraBindings: [{ ...good, kind: 'faction' }] }).cameraBindings,
            []);
    });

    it('refuses a rule with nothing to match on', () => {
        assert.deepEqual(
            viewerSettingsFrom({ cameraBindings: [{ ...good, value: '  ' }] }).cameraBindings, []);
    });

    it('keeps the order the reader put them in', () => {
        // First match wins within a kind, so the order IS the meaning.
        const settings = viewerSettingsFrom({
            cameraBindings: [good, { ...good, id: 'b2', value: 'Bomber' }],
        });

        assert.deepEqual(settings.cameraBindings.map(b => b.value), ['Fighter', 'Bomber']);
    });
});

describe('collapsedPanels', () => {
    it('starts with everything open', () => {
        // A panel that opens folded shut hides the controls someone came for, and there is nothing
        // on screen to say they exist.
        assert.deepEqual(viewerSettingsFrom({}).collapsedPanels, []);
    });

    it('carries the sections the reader folded away', () => {
        // Which sections you keep shut describes how YOU work, not the model - so it is a viewer
        // setting and follows the person, like the grid and the lights.
        assert.deepEqual(
            viewerSettingsFrom({ collapsedPanels: ['scene.light', 'camera.capture'] })
                .collapsedPanels,
            ['scene.light', 'camera.capture']);
    });

    it('refuses anything that is not a list of names', () => {
        assert.deepEqual(viewerSettingsFrom({ collapsedPanels: 'scene.light' }).collapsedPanels, []);
        assert.deepEqual(viewerSettingsFrom({ collapsedPanels: [1, 2] }).collapsedPanels, []);
    });

    it('drops the bad entries from an otherwise good list rather than the whole list', () => {
        assert.deepEqual(
            viewerSettingsFrom({ collapsedPanels: ['scene.light', 7, null] }).collapsedPanels,
            ['scene.light']);
    });
});

describe('the attacker and its presets', () => {
    it('opens on a plain hitpoint weapon', () => {
        // Not shield-only: against an unshielded target that visibly does nothing, which reads as
        // the panel being broken rather than as the rule it is.
        assert.equal(DEFAULT_VIEWER_SETTINGS.attacker.hitpoint, true);
        assert.equal(DEFAULT_VIEWER_SETTINGS.attackerPresets.length, 0);
    });

    it('round-trips a configuration and its saved presets', () => {
        const stored = {
            attacker: {
                damage: 250, damageType: 'Damage_Ion',
                shield: true, energy: false, hitpoint: true,
            },
            attackerPresets: [
                { id: 'p1', name: 'Broadside missile', damage: 400, damageType: 'Damage_Default',
                    shield: false, energy: false, hitpoint: true },
            ],
        };

        const read = viewerSettingsFrom(stored);

        assert.equal(read.attacker.damage, 250);
        assert.equal(read.attacker.damageType, 'Damage_Ion');
        assert.equal(read.attacker.shield, true);
        assert.equal(read.attackerPresets[0].name, 'Broadside missile');
        assert.equal(read.attackerPresets[0].damage, 400);
    });

    it('is Tier 1 - a preset describes the PERSON, not the model on screen', () => {
        // You would not expect a "Broadside missile" preset to vanish when you open a different
        // ship, which is why this lives in globalState beside the camera presets rather than in the
        // per-subject blob.
        assert.ok('attackerPresets' in DEFAULT_VIEWER_SETTINGS);
    });

    it('survives an older stored shape that has neither field', () => {
        const read = viewerSettingsFrom({ fireArcs: true });

        assert.deepEqual(read.attacker, DEFAULT_VIEWER_SETTINGS.attacker);
        assert.deepEqual(read.attackerPresets, []);
    });

    it('drops a preset missing its name or its id rather than showing a blank row', () => {
        const read = viewerSettingsFrom({
            attackerPresets: [
                { id: '', name: 'No id', damage: 1, damageType: 'D',
                    shield: false, energy: false, hitpoint: true },
                { id: 'ok', name: 'Keeps', damage: 1, damageType: 'D',
                    shield: false, energy: false, hitpoint: true },
            ],
        });

        assert.deepEqual(read.attackerPresets.map(p => p.name), ['Keeps']);
    });

    it('does not throw on junk in either field', () => {
        // The blob is written by an older build and read by this one; a string where an object was
        // expected must cost the reader their attacker, not their whole room.
        assert.deepEqual(
            viewerSettingsFrom({ attacker: 'nope', attackerPresets: 7 }).attacker,
            DEFAULT_VIEWER_SETTINGS.attacker);
    });

    it('refuses a damage number that is not one', () => {
        const read = viewerSettingsFrom({
            attacker: { damage: Number.NaN, damageType: 'D',
                shield: false, energy: false, hitpoint: true },
        });

        assert.equal(read.attacker.damage, DEFAULT_VIEWER_SETTINGS.attacker.damage);
    });
});
