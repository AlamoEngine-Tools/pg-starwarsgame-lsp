// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The ROOM the model preview shows a subject in: every Tier 1 value, in one place.
//
// Tier 1 is what describes how the reader works rather than the model in front of them - the grid,
// the floor, the lights, the camera presets, which flyout sections they keep shut - so it outlives
// the subject and is stored for the person. The component used to declare these twenty-two values
// across eight hundred lines, restore them in the message handler, reset them in a third place and
// remember them in a fourth; a setting added to one list and not the others was restored and never
// saved, or saved and never reset. Here the value, its default, its restore, its reset and its save
// sit together.
//
// State only. Pushing a value into the viewport is `useViewportSync`'s, called after the renderer
// exists.

import { useEffect, useState } from "react";

import {
    type BackgroundKind,
    type CameraBinding,
    type CameraPreset,
    DEFAULT_LIGHTS,
    DEFAULT_VIEWER_SETTINGS,
    type LightRig,
    type ViewerSettings,
    type Wind,
} from "../../shared/viewerSettings";
import type { LabelMode } from "./skeleton";

export interface RoomSettingsOptions {
    /** Whether the stored room has been applied; nothing is saved before, or defaults would be. */
    restored: () => boolean;
    /** Writes the room back for the person. */
    save: (settings: ViewerSettings) => void;
    /** Whether a stored camera preset names one of the presets, and so can be restored. */
    isPreset: (view: string) => boolean;
}

export type RoomSettings = ReturnType<typeof useRoomSettings>;

export function useRoomSettings(options: RoomSettingsOptions) {
    const [grid, setGrid] = useState(true);
    // Off by default: a floor is the right backdrop for a walker and the wrong one for a
    // capital ship, and most of what opens here is in space.
    const [floor, setFloor] = useState(false);
    /** Where the ground sits. Not every model is authored standing on the origin. */
    const [floorLevel, setFloorLevel] = useState(
        DEFAULT_VIEWER_SETTINGS.floorLevel,
    );
    const [wireframe, setWireframe] = useState(
        DEFAULT_VIEWER_SETTINGS.wireframe,
    );
    const [heatOn, setHeatOn] = useState(DEFAULT_VIEWER_SETTINGS.heat);
    const [heatDebug, setHeatDebug] = useState(
        DEFAULT_VIEWER_SETTINGS.heatDebug,
    );
    const [bloom, setBloom] = useState(DEFAULT_VIEWER_SETTINGS.bloom);
    const [lights, setLights] = useState<LightRig>(DEFAULT_LIGHTS);

    /**
     * The reader's own camera presets.
     *
     * Tier 1, with the rest of the room: a preset exists to frame a ROSTER the same way, so it has
     * to outlive the subject it was built on.
     */
    const [cameraPresets, setCameraPresets] = useState<CameraPreset[]>([]);

    /** Which camera preset a kind of subject opens with. Tier 1 with the presets they point at. */
    const [cameraBindings, setCameraBindings] = useState<CameraBinding[]>([]);

    const [wind, setWind] = useState<Wind>(DEFAULT_VIEWER_SETTINGS.wind);
    const [weatherIntensity, setWeatherIntensity] = useState(
        DEFAULT_VIEWER_SETTINGS.weatherIntensity,
    );
    const [background, setBackground] = useState<BackgroundKind>(
        DEFAULT_VIEWER_SETTINGS.background,
    );

    /**
     * The camera preset in force.
     *
     * Held rather than fired and forgotten: the presets are a one-of-N choice, and a segmented
     * control that never shows which one you picked is just four buttons in a box.
     *
     * Null once the camera is dragged. A preset is a place to jump to, not a mode the camera stays
     * in, so keeping "Front" lit while you look at the model from underneath would be a lie.
     */
    const [cameraView, setCameraView] = useState<string | null>("threeQuarter");

    // How much further than the subject the camera can see. Fitted to the model AND its effects, so
    // 1 already clears a flamethrower's throw; the multiplier is for the trails no fit anticipates.
    const [drawDistance, setDrawDistance] = useState(
        DEFAULT_VIEWER_SETTINGS.drawDistance,
    );

    const [skeletonOn, setSkeletonOn] = useState(false);
    const [labelMode, setLabelMode] = useState<LabelMode>("selected");

    /**
     * The flyout sections folded shut, by id.
     *
     * Held as the FOLDED ones rather than the open ones, so a section added later arrives open - a
     * control that appears already hidden is one nobody discovers. Tier 1: which groups you keep
     * shut describes how you work, not the model in front of you.
     */
    const [folded, setFolded] = useState<ReadonlySet<string>>(new Set());

    /**
     * Draw with the effects' own translated shaders, rather than the archetype reading of them.
     *
     * On by default: that is the point of the feature, and anything that cannot translate - a
     * mod-authored effect, or one of the eleven that are render state only - falls back to the
     * archetype on its own. The switch stays so the two can be compared.
     */
    const [translatedOn, setTranslatedOn] = useState(true);

    const [particlesOn, setParticlesOn] = useState(true);
    const [particleSpeed, setParticleSpeed] = useState(1);
    const [fireArcs, setFireArcs] = useState(false);

    /** The room the reader left behind, applied before any geometry arrives. */
    const applyRoom = (room: ViewerSettings): void => {
        setGrid(room.grid);
        setFloor(room.floor);
        setFloorLevel(room.floorLevel);
        setWireframe(room.wireframe);
        setHeatOn(room.heat);
        setHeatDebug(room.heatDebug);
        setBloom(room.bloom);
        setLights(room.lights);
        setCameraPresets(room.cameraPresets);
        setCameraBindings(room.cameraBindings);
        setFolded(new Set(room.collapsedPanels));
        setWind(room.wind);
        setWeatherIntensity(room.weatherIntensity);
        setBackground(room.background);
        // Only a PRESET is restorable. A camera id names a bone in whatever model was open at the
        // time, and a stored `camera:1` would light a button for a camera this subject may not
        // have - so anything else comes back as "no preset", which is what a dragged camera
        // already looks like.
        setCameraView(
            options.isPreset(room.cameraPreset) ? room.cameraPreset : null,
        );
        setDrawDistance(room.drawDistance);
        setSkeletonOn(room.skeleton);
        setLabelMode(room.boneLabels as LabelMode);
        setFireArcs(room.fireArcs);
        setTranslatedOn(room.effectShaders);
        setParticlesOn(room.particles);
        setParticleSpeed(room.particleSpeed);
    };

    /**
     * Puts the whole room back to its defaults.
     *
     * Every Tier 1 value at once rather than one control at a time: the settings that need
     * recovering are the ones a reader has already lost track of, and hunting fourteen of them down
     * individually is the problem rather than the fix. The subject's own state is left alone - it
     * is not part of the room, and resetting it would undo a damage state the reader set on purpose.
     *
     * The weapon bench is left alone for the same reason, and this changed when it moved tiers: it
     * belongs to the PROJECT now, so throwing away a mod's saved weapons is not something "reset
     * the room" should ever do.
     */
    const resetRoom = (): void => {
        const room = DEFAULT_VIEWER_SETTINGS;

        setGrid(room.grid);
        setFloor(room.floor);
        setFloorLevel(room.floorLevel);
        setWireframe(room.wireframe);
        setHeatOn(room.heat);
        setHeatDebug(room.heatDebug);
        setBloom(room.bloom);
        setBackground(room.background);
        setDrawDistance(room.drawDistance);
        setLights(room.lights);
        setCameraPresets(room.cameraPresets);
        setCameraBindings(room.cameraBindings);
        setFolded(new Set(room.collapsedPanels));
        setWind(room.wind);
        setWeatherIntensity(room.weatherIntensity);
        setSkeletonOn(room.skeleton);
        setLabelMode(room.boneLabels as LabelMode);
        setFireArcs(room.fireArcs);
        setTranslatedOn(room.effectShaders);
        setParticlesOn(room.particles);
        setParticleSpeed(room.particleSpeed);
    };

    /**
     * Remembers the room.
     *
     * One effect over every Tier 1 value rather than a write beside each control: a setting is
     * remembered because of what it IS, not because whoever added its checkbox remembered to save
     * it. Skipped until the stored room has been applied, or the first frame would overwrite it
     * with defaults.
     */
    useEffect(() => {
        if (!options.restored()) {
            return;
        }

        options.save({
            ...DEFAULT_VIEWER_SETTINGS,
            grid,
            floor,
            floorLevel,
            drawDistance,
            wireframe,
            heat: heatOn,
            heatDebug,
            bloom,
            skeleton: skeletonOn,
            boneLabels: labelMode,
            fireArcs,
            effectShaders: translatedOn,
            particles: particlesOn,
            particleSpeed,
            lights,
            cameraPresets,
            cameraBindings,
            collapsedPanels: [...folded],
            wind,
            weatherIntensity,
            background,
            cameraPreset: cameraView ?? "",
        });
        // The options are read when the effect runs; the values are what decide that it runs.
    }, [
        grid,
        floor,
        floorLevel,
        wireframe,
        heatOn,
        heatDebug,
        bloom,
        drawDistance,
        lights,
        wind,
        weatherIntensity,
        cameraPresets,
        cameraBindings,
        folded,
        background,
        cameraView,
        skeletonOn,
        labelMode,
        fireArcs,
        translatedOn,
        particlesOn,
        particleSpeed,
    ]);

    return {
        grid,
        setGrid,
        floor,
        setFloor,
        floorLevel,
        setFloorLevel,
        wireframe,
        setWireframe,
        heatOn,
        setHeatOn,
        heatDebug,
        setHeatDebug,
        bloom,
        setBloom,
        lights,
        setLights,
        cameraPresets,
        setCameraPresets,
        cameraBindings,
        setCameraBindings,
        wind,
        setWind,
        weatherIntensity,
        setWeatherIntensity,
        background,
        setBackground,
        cameraView,
        setCameraView,
        drawDistance,
        setDrawDistance,
        skeletonOn,
        setSkeletonOn,
        labelMode,
        setLabelMode,
        folded,
        setFolded,
        translatedOn,
        setTranslatedOn,
        particlesOn,
        setParticlesOn,
        particleSpeed,
        setParticleSpeed,
        fireArcs,
        setFireArcs,
        applyRoom,
        resetRoom,
    };
}
