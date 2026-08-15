// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The settings that describe the ROOM rather than what is standing in it.
//
// The rule that decides what belongs here: a setting lives at the tier of the thing it describes.
// The grid, the floor, the lights and the background describe the viewer, so they follow the person
// from one file to the next and across sessions. A model's damage state, its detail level and its
// hidden meshes describe THAT model, so they are held per subject and reset when another is opened
// - which is also what keeps the agreed opening rules honest (highest detail, undamaged, quiet).
//
// Everything here is read back from storage that outlives this build, so `viewerSettingsFrom` never
// trusts what it is handed: a missing field takes its default, a wrong one is refused, and nothing
// throws. A preview that opens blank because of a bad string in a settings file has no way back.

import { type CameraBinding } from './cameraBindings';
import { type CameraPreset } from './cameraPresets';

/** Red, green and blue, each 0..1 - the range three.js and the effects both work in. */
export type Colour = readonly [number, number, number];

/** One directional light, in the terms AloViewer's own settings dialog uses. */
export interface DirectionalSetting {
    /** Degrees around the model, 0 at the front. */
    azimuth: number;
    /** Degrees above the horizon. */
    elevation: number;
    colour: Colour;
    /** Brightness, kept apart from the colour so a light can be dimmed without desaturating it. */
    intensity: number;
}

/** What is drawn behind the model. */
export type BackgroundKind = 'flat' | 'starfield' | 'sky';

/** The three directionals, by the names the engine numbers them in. */
export type DirectionalName = 'sun' | 'fill1' | 'fill2';

/** In engine order, so index 0 is the sun a shader asks for as DIR_LIGHT_VEC_0. */
export const LIGHT_NAMES: readonly DirectionalName[] = ['sun', 'fill1', 'fill2'];

export const LIGHT_LABELS: Record<DirectionalName, string> = {
    sun: 'Sun',
    fill1: 'Fill 1',
    fill2: 'Fill 2',
};

/** A colour as an `input type=color` wants it. */
export function hexFromColour(colour: Colour): string {
    const channel = (value: number): string =>
        Math.round(Math.min(1, Math.max(0, value)) * 255).toString(16).padStart(2, '0');

    return `#${channel(colour[0])}${channel(colour[1])}${channel(colour[2])}`;
}

/** And back. An unreadable string comes back white rather than as three NaNs. */
export function colourFromHex(hex: string): Colour {
    const match = /^#?([0-9a-f]{6})$/i.exec(hex.trim());

    if (match === null) {
        return WHITE;
    }

    const value = Number.parseInt(match[1], 16);

    return [((value >> 16) & 255) / 255, ((value >> 8) & 255) / 255, (value & 255) / 255];
}

export interface LightRig {
    /** The one that casts. */
    sun: DirectionalSetting;
    fill1: DirectionalSetting;
    fill2: DirectionalSetting;
    ambient: { colour: Colour; intensity: number };
    /** The engine keeps a global specular colour; the translated effects read it. */
    specular: Colour;
    /**
     * What a cast shadow is tinted.
     *
     * One global term rather than a property of the sun, which is where AloViewer keeps it too -
     * beside ambient and specular. Only the GROUND shadow can take it here: Alamo tints stencil
     * shadow volumes, while three's shadow mapping has no colour to set, so the ground plane
     * catches a tinted shadow and self-shadowing on the hull cannot.
     */
    shadow: Colour;
}

/**
 * The weather, such as it is.
 *
 * One wind with three consumers: `Tree.fx` bends along it, `Grass.fx` reads its own vector with the
 * speed in `w`, and 484 of the corpus's 1019 emitters set `affectedByWind` and are thrown by it.
 * AloViewer's settings dialog exposes exactly these two numbers.
 */
export interface Wind {
    /** Degrees, the direction the wind blows TOWARDS. */
    heading: number;
    speed: number;
}

/**
 * What the stored blob means, not what it contains.
 *
 * Bumped only when an EXISTING field changes meaning, which no amount of type checking can catch:
 * the value is still valid, it just no longer says what it used to. Version 2 is the shadow colour
 * becoming a multiplier rather than the colour drawn over the ground.
 */
export const SETTINGS_VERSION = 2;

export interface ViewerSettings {
    /** See {@link SETTINGS_VERSION}. */
    version: number;
    grid: boolean;
    floor: boolean;
    /**
     * Where the ground plane sits, in model units.
     *
     * Not every model is authored standing on the origin - a prop dug into terrain or a turret on a
     * pad sits above or below it - and a floor at zero then cuts through the model or floats under
     * it. AloViewer offers the same control for the same reason.
     */
    floorLevel: number;
    /** Draw every mesh as its edges. Reaches the translated effect shaders too. */
    wireframe: boolean;
    /** Whether heat sprites bend the frame at all. */
    heat: boolean;
    /** Draw the heat BUFFER instead of the bent picture - AloViewer's own debug view. */
    heatDebug: boolean;
    /** The engine's bloom, off by default as it is there. */
    bloom: boolean;
    background: BackgroundKind;
    /**
     * How much further than the subject the camera can see, as a multiple of the fitted distance.
     *
     * Fitted to the model AND its effects, so 1 already reaches the end of a flamethrower's throw.
     * The multiplier is for the cases the fit cannot know about - a very long trail, or pulling the
     * far plane in to settle z-fighting on a huge hull.
     */
    drawDistance: number;
    lights: LightRig;

    /**
     * Shots the reader has saved, shared across every subject.
     *
     * Tier 1 on purpose: the whole point of a preset is to frame a ROSTER identically, so it has to
     * outlive the subject it was built on. Distance is stored relative to the subject's radius -
     * see `cameraPresets.ts` - which is what lets one shot mean the same thing on a trooper and on
     * a Star Destroyer.
     */
    cameraPresets: CameraPreset[];

    /**
     * The flyout sections the reader has folded shut, by id.
     *
     * A viewer setting by the same rule as the lights: which sections you keep open describes how
     * YOU work, not the model in front of you. Stored as the folded ones rather than the open ones
     * so a section added later arrives OPEN - a new control that appears already hidden is one
     * nobody discovers.
     */
    collapsedPanels: string[];

    /**
     * Rules deciding which saved shot a subject opens with.
     *
     * Ordered, and the order is the meaning: the first matching rule of a kind wins. See
     * `cameraBindings.ts` for why a category cannot simply be a key.
     */
    cameraBindings: CameraBinding[];
    wind: Wind;
    skeleton: boolean;
    boneLabels: string;
    fireArcs: boolean;
    effectShaders: boolean;
    particles: boolean;
    particleSpeed: number;
    /**
     * The faction to tint with, by NAME.
     *
     * By name because the index differs per subject: "I am reviewing the Rebel roster" should
     * survive opening the next unit, and quietly fall back when that unit has no such faction.
     */
    faction: string | null;
    customColour: string | null;
    cameraPreset: string;
}

const WHITE: Colour = [1, 1, 1];

/**
 * The rig the preview opens with.
 *
 * A key from the front-right and two dimmer fills, which is what makes an untextured hull read as a
 * solid rather than a silhouette. Only the sun casts: a second casting light doubles the cost and
 * crosses two shadows over the model, which reads as a fault rather than as a fill.
 */
export const DEFAULT_LIGHTS: LightRig = {
    // The ENGINE's own rig, value for value, out of `Config::GetDefaultEnvironment`. The engine
    // multiplies each light's colour by its alpha, so the sun ships as mid grey and the two fills
    // as a dark blue an eighth as strong - and the ambient is a tenth, not a third.
    //
    // What this replaces was a 1.6 white sun with white fills and a 0.35 white ambient: about four
    // times the light, all of it colourless, which blew every hull out to a flat white silhouette
    // with no shading left in it. The blue fills are what put the cool tone back into the shadowed
    // faces, and they are the difference you see against AloViewer side by side.
    sun: { azimuth: 0, elevation: 45, colour: WHITE, intensity: 0.5 },
    fill1: { azimuth: 210, elevation: -10, colour: [0.25, 0.25, 0.5], intensity: 0.5 },
    fill2: { azimuth: 120, elevation: -10, colour: [0.25, 0.25, 0.5], intensity: 0.5 },
    ambient: { colour: WHITE, intensity: 0.1 },
    specular: WHITE,

    // AloViewer's own default, and now it means what theirs means: the stencil darken MULTIPLIES
    // what is under it, so 0.5 is "half as bright" rather than a flat grey patch. An earlier build
    // darkened by alpha blending instead, where this value would have painted a shadow brighter
    // than the floor it fell on - the number was never wrong, the blend was.
    shadow: [0.5, 0.5, 0.5],
};

export const DEFAULT_VIEWER_SETTINGS: ViewerSettings = {
    version: SETTINGS_VERSION,
    grid: true,
    floor: false,
    floorLevel: 0,
    wireframe: false,

    // On, because it WAS unconditional before there was a switch. Defaulting it off would quietly
    // change what every preview draws on the day the control appeared.
    heat: true,
    heatDebug: false,
    bloom: false,
    background: 'flat',
    drawDistance: 1,
    lights: DEFAULT_LIGHTS,
    cameraPresets: [],
    cameraBindings: [],
    collapsedPanels: [],

    // A gentle breeze across the model rather than dead calm: the foliage shaders are only
    // interesting in motion, and zero would look like the feature is missing.
    wind: { heading: 90, speed: 1 },
    skeleton: false,
    boneLabels: 'none',
    fireArcs: false,
    effectShaders: false,
    particles: true,
    particleSpeed: 1,
    faction: null,
    customColour: null,
    cameraPreset: 'threeQuarter',
};

const BACKGROUNDS: BackgroundKind[] = ['flat', 'starfield', 'sky'];

/** The ranges the controls offer, so a stored value can never put the viewer somewhere unusable. */
const RANGES: Record<string, { min: number; max: number }> = {
    drawDistance: { min: 1, max: 16 },

    // AloViewer's own range. Past it the floor is somewhere no camera fitted to a model will ever
    // look, which reads as the ground having vanished rather than as a number out of range.
    floorLevel: { min: -10000, max: 10000 },
    particleSpeed: { min: 0, max: 4 },
    azimuth: { min: 0, max: 360 },
    elevation: { min: -90, max: 90 },
    intensity: { min: 0, max: 8 },
    windSpeed: { min: 0, max: 20 },
};

/**
 * Reads settings back out of storage, falling back field by field.
 *
 * Field by field rather than all-or-nothing: one unreadable value should cost its own control, not
 * every preference the reader has set.
 */
/**
 * The saved shots that are still readable.
 *
 * Entry by entry, and a bad one is DROPPED rather than repaired or thrown on. This store outlives
 * any one version of the panel, so it has to survive whatever ends up in it - and a preset that
 * cannot be read is one shot the reader loses, where a throw here would cost them every setting
 * they have.
 */
function presetsFrom(stored: unknown): CameraPreset[] {
    if (!Array.isArray(stored)) {
        return [];
    }

    const presets: CameraPreset[] = [];

    for (const entry of stored) {
        const raw = asRecord(entry);
        const { id, name, distance, pitch, yaw, bone } = raw;

        if (typeof id !== 'string' || id === '' || typeof name !== 'string'
            // A distance of zero or less is not a shot - it is a camera at the centre of the model
            // looking at itself.
            || typeof distance !== 'number' || !Number.isFinite(distance) || distance <= 0
            || typeof pitch !== 'number' || !Number.isFinite(pitch)
            || typeof yaw !== 'number' || !Number.isFinite(yaw)) {
            continue;
        }

        presets.push({
            id, name, distance, pitch, yaw,
            bone: typeof bone === 'string' && bone !== '' ? bone : null,
        });
    }

    return presets;
}

/** The binding rules that are still readable. Same rule as the presets: drop, never repair. */
function bindingsFrom(stored: unknown): CameraBinding[] {
    if (!Array.isArray(stored)) {
        return [];
    }

    const kinds = new Set(['object', 'type', 'category']);
    const bindings: CameraBinding[] = [];

    for (const entry of stored) {
        const raw = asRecord(entry);
        const { id, kind, value, presetId } = raw;

        // A rule nobody can evaluate is worse than no rule at all: it would sit in the list looking
        // like the subject was bound while never firing.
        if (typeof id !== 'string' || id === ''
            || typeof kind !== 'string' || !kinds.has(kind)
            || typeof value !== 'string' || value.trim() === ''
            || typeof presetId !== 'string' || presetId === '') {
            continue;
        }

        bindings.push({ id, kind: kind as CameraBinding['kind'], value, presetId });
    }

    return bindings;
}

export function viewerSettingsFrom(stored: unknown): ViewerSettings {
    const raw = asRecord(stored);

    // Anything written before a field changed meaning has to give that field up. Only that field:
    // a version bump is a migration, not a reset.
    const version = typeof raw.version === 'number' ? raw.version : 1;
    const lights = lightsFrom(raw.lights);

    return {
        version: SETTINGS_VERSION,
        grid: boolean_(raw.grid, DEFAULT_VIEWER_SETTINGS.grid),
        floor: boolean_(raw.floor, DEFAULT_VIEWER_SETTINGS.floor),
        floorLevel: number_(raw.floorLevel, DEFAULT_VIEWER_SETTINGS.floorLevel, RANGES.floorLevel),
        wireframe: boolean_(raw.wireframe, DEFAULT_VIEWER_SETTINGS.wireframe),
        heat: boolean_(raw.heat, DEFAULT_VIEWER_SETTINGS.heat),
        heatDebug: boolean_(raw.heatDebug, DEFAULT_VIEWER_SETTINGS.heatDebug),
        bloom: boolean_(raw.bloom, DEFAULT_VIEWER_SETTINGS.bloom),
        background: BACKGROUNDS.includes(raw.background as BackgroundKind)
            ? raw.background as BackgroundKind
            : DEFAULT_VIEWER_SETTINGS.background,
        drawDistance: number_(raw.drawDistance, DEFAULT_VIEWER_SETTINGS.drawDistance,
            RANGES.drawDistance),
        lights: version >= 2
            ? lights
            : { ...lights, shadow: DEFAULT_LIGHTS.shadow },
        cameraPresets: presetsFrom(raw.cameraPresets),
        cameraBindings: bindingsFrom(raw.cameraBindings),
        collapsedPanels: names(raw.collapsedPanels),
        wind: windFrom(raw.wind),
        skeleton: boolean_(raw.skeleton, DEFAULT_VIEWER_SETTINGS.skeleton),
        boneLabels: string_(raw.boneLabels, DEFAULT_VIEWER_SETTINGS.boneLabels),
        fireArcs: boolean_(raw.fireArcs, DEFAULT_VIEWER_SETTINGS.fireArcs),
        effectShaders: boolean_(raw.effectShaders, DEFAULT_VIEWER_SETTINGS.effectShaders),
        particles: boolean_(raw.particles, DEFAULT_VIEWER_SETTINGS.particles),
        particleSpeed: number_(raw.particleSpeed, DEFAULT_VIEWER_SETTINGS.particleSpeed,
            RANGES.particleSpeed),
        faction: nullableString(raw.faction),
        customColour: nullableString(raw.customColour),
        cameraPreset: string_(raw.cameraPreset, DEFAULT_VIEWER_SETTINGS.cameraPreset),
    };
}

function windFrom(stored: unknown): Wind {
    const raw = asRecord(stored);

    return {
        heading: number_(raw.heading, DEFAULT_VIEWER_SETTINGS.wind.heading, RANGES.azimuth),
        speed: number_(raw.speed, DEFAULT_VIEWER_SETTINGS.wind.speed, RANGES.windSpeed),
    };
}

function lightsFrom(stored: unknown): LightRig {
    const raw = asRecord(stored);
    const ambient = asRecord(raw.ambient);

    return {
        sun: directionalFrom(raw.sun, DEFAULT_LIGHTS.sun),
        fill1: directionalFrom(raw.fill1, DEFAULT_LIGHTS.fill1),
        fill2: directionalFrom(raw.fill2, DEFAULT_LIGHTS.fill2),
        ambient: {
            colour: colourFrom(ambient.colour, DEFAULT_LIGHTS.ambient.colour),
            intensity: number_(ambient.intensity, DEFAULT_LIGHTS.ambient.intensity,
                RANGES.intensity),
        },
        specular: colourFrom(raw.specular, DEFAULT_LIGHTS.specular),
        shadow: colourFrom(raw.shadow, DEFAULT_LIGHTS.shadow),
    };
}

function directionalFrom(stored: unknown, fallback: DirectionalSetting): DirectionalSetting {
    const raw = asRecord(stored);

    return {
        azimuth: number_(raw.azimuth, fallback.azimuth, RANGES.azimuth),
        elevation: number_(raw.elevation, fallback.elevation, RANGES.elevation),
        colour: colourFrom(raw.colour, fallback.colour),
        intensity: number_(raw.intensity, fallback.intensity, RANGES.intensity),
    };
}

function colourFrom(stored: unknown, fallback: Colour): Colour {
    if (!Array.isArray(stored) || stored.length !== 3) {
        return fallback;
    }

    const channels = stored.map(
        channel => typeof channel === 'number' && Number.isFinite(channel) ? channel : Number.NaN);

    return channels.every(channel => channel >= 0 && channel <= 1)
        ? [channels[0], channels[1], channels[2]]
        : fallback;
}

function asRecord(value: unknown): Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
        ? value as Record<string, unknown>
        : {};
}

function boolean_(value: unknown, fallback: boolean): boolean {
    return typeof value === 'boolean' ? value : fallback;
}

/** A list of names, keeping the good entries. One bad string is not a reason to unfold the lot. */
function names(value: unknown): string[] {
    return Array.isArray(value) ? value.filter(entry => typeof entry === 'string') : [];
}

function string_(value: unknown, fallback: string): string {
    return typeof value === 'string' ? value : fallback;
}

function nullableString(value: unknown): string | null {
    return typeof value === 'string' ? value : null;
}

function number_(value: unknown, fallback: number, range: { min: number; max: number }): number {
    if (typeof value !== 'number' || !Number.isFinite(value)) {
        return fallback;
    }

    return Math.min(range.max, Math.max(range.min, value));
}
