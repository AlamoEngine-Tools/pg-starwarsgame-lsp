// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every setting the model preview pushes into its viewport, one effect each.
//
// The component held these as forty-odd lines of three-line effects scattered between the things
// they had nothing to do with. Each is still its own effect with its own dependency, so changing one
// setting pushes that setting and nothing else; what changed is that they are in one place, and the
// list of what the dock can change about the viewport can be read at a glance.
//
// Called after the renderer exists - its effect creates the viewport, and these run after it on the
// first frame, so the viewport starts with every value the panel holds.

import { type RefObject, useEffect } from "react";

import {
    hexFromColour,
    type BackgroundKind,
    type LightRig,
    type Wind,
} from "../../shared/viewerSettings";
import type { LabelMode } from "./skeleton";
import type { PreviewViewport } from "./viewport";

/** What the viewport is told, by the value the dock holds for it. */
export interface ViewportSettings {
    grid: boolean;
    floor: boolean;
    floorLevel: number;
    wireframe: boolean;
    heat: boolean;
    heatDebug: boolean;
    bloom: boolean;
    lights: LightRig;
    wind: Wind;
    weatherIntensity: number;
    background: BackgroundKind;
    skeleton: boolean;
    labelMode: LabelMode;
    drawDistance: number;
    selectedBones: ReadonlySet<number>;
    alt: number;
    lod: number;
    particles: boolean;
    particlesPaused: boolean;
    particleSpeed: number;
    animation: string | null;
    animationSpeed: number;
    animationLoop: boolean;
    animationPaused: boolean;
    /** The effects' own translated shaders, and with them Game mode's shadow volumes. */
    translated: boolean;
    /** Game rendering's stand-in shadow, for a model without a shadow mesh. */
    standInShadow: boolean;
}

/** What the panel reads back after a push that changes what the viewport reports. */
export interface ViewportSyncReadback {
    /** A level change hid or showed geometry: the stats, the attachments and the tree changed. */
    levelsApplied(viewport: PreviewViewport): void;
    /** The translated shaders were switched: how many sub-meshes they reach changed. */
    translatedApplied(viewport: PreviewViewport): void;
}

export function useViewportSync(
    viewportRef: RefObject<PreviewViewport | null>,
    settings: ViewportSettings,
    readback: ViewportSyncReadback,
): void {
    const viewport = (): PreviewViewport | null => viewportRef.current;

    // ── the room ──────────────────────────────────────────────────────────────
    useEffect(() => {
        viewport()?.setGridVisible(settings.grid);
    }, [settings.grid]);

    useEffect(() => {
        viewport()?.setFloorVisible(settings.floor);
    }, [settings.floor]);

    useEffect(() => {
        viewport()?.setFloorLevel(settings.floorLevel);
    }, [settings.floorLevel]);

    useEffect(() => {
        viewport()?.setWireframe(settings.wireframe);
    }, [settings.wireframe]);

    useEffect(() => {
        viewport()?.setHeat(settings.heat);
    }, [settings.heat]);

    useEffect(() => {
        viewport()?.setHeatDebug(settings.heatDebug);
    }, [settings.heatDebug]);

    useEffect(() => {
        viewport()?.setBloom(settings.bloom);
    }, [settings.bloom]);

    useEffect(() => {
        viewport()?.setLightRig(settings.lights);
        viewport()?.setShadowColour(hexFromColour(settings.lights.shadow));
    }, [settings.lights]);

    useEffect(() => {
        viewport()?.setWind(settings.wind.heading, settings.wind.speed);
    }, [settings.wind]);

    useEffect(() => {
        viewport()?.setWeatherIntensity(settings.weatherIntensity);
    }, [settings.weatherIntensity]);

    useEffect(() => {
        viewport()?.setBackground(settings.background);
    }, [settings.background]);

    useEffect(() => {
        viewport()?.setDrawDistance(settings.drawDistance);
    }, [settings.drawDistance]);

    // ── the skeleton ──────────────────────────────────────────────────────────
    useEffect(() => {
        viewport()?.setSkeletonVisible(settings.skeleton);
    }, [settings.skeleton]);

    useEffect(() => {
        viewport()?.setLabelMode(settings.labelMode);
    }, [settings.labelMode]);

    useEffect(() => {
        viewport()?.setSelectedBones(settings.selectedBones);
    }, [settings.selectedBones]);

    // ── the subject ───────────────────────────────────────────────────────────
    useEffect(() => {
        const current = viewport();
        current?.setLevels(settings.alt, settings.lod);
        // Geometry may have been hidden or shown, which changes the stat line - and the tree's
        // checkboxes, since a level change drops every hand-set visibility along with it.
        if (current !== null) {
            readback.levelsApplied(current);
        }
        // The readback is the panel's to change; the levels are what decide that this runs.
    }, [settings.alt, settings.lod]);

    useEffect(() => {
        const current = viewport();
        if (current === null) {
            return;
        }

        current.setTranslatedShaders(settings.translated);

        // Game mode shadows the way the ENGINE does: stencil volumes cast by the model's own
        // authored shadow mesh, with the shadow colour reaching the hull as well as the ground,
        // and no shadow for a model without one. Default mode keeps three's shadow mapping.
        current.setShadowVolumes(settings.translated);
        readback.translatedApplied(current);
    }, [settings.translated]);

    useEffect(() => {
        viewport()?.setStandInShadow(settings.standInShadow);
    }, [settings.standInShadow]);

    // ── particles ─────────────────────────────────────────────────────────────
    useEffect(() => {
        viewport()?.setParticlesVisible(settings.particles);
    }, [settings.particles]);

    useEffect(() => {
        viewport()?.setParticlesPaused(settings.particlesPaused);
    }, [settings.particlesPaused]);

    useEffect(() => {
        viewport()?.setParticleSpeed(settings.particleSpeed);
    }, [settings.particleSpeed]);

    // ── animation ─────────────────────────────────────────────────────────────
    useEffect(() => {
        viewport()?.setAnimationSpeed(settings.animationSpeed);
    }, [settings.animationSpeed]);

    useEffect(() => {
        viewport()?.setAnimationLoop(settings.animationLoop);
    }, [settings.animationLoop]);

    useEffect(() => {
        viewport()?.setPaused(settings.animationPaused);
    }, [settings.animationPaused, settings.animation]);
}
