// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's still capture: the renderer resized, the subject framed for the shot, the grid, floor,
// particles and annotations shown or hidden as asked, one frame drawn and read back as a PNG, and every one
// of those put back afterwards. It reads the renderer, the camera and the layers whose toggles it borrows;
// nothing else.

import { captureFrame } from "./capture";
import * as THREE from "three";
import { type CameraRig } from "./cameraRig";
import { type EnvironmentRig } from "./environmentRig";
import { type OverlayLayer } from "./overlayLayer";
import { type ParticleHost } from "./particleHost";
import { type TurretRig } from "./turretRig";

/** What CaptureRig reads from the viewport it belongs to. */
/** What CaptureRig reads from the viewport it belongs to. */
export interface CaptureViewport {
    readonly camera: THREE.PerspectiveCamera;
    readonly cameraRig: CameraRig;
    readonly canvas: HTMLCanvasElement;
    readonly environment: EnvironmentRig;
    readonly overlay: OverlayLayer;
    readonly particles: ParticleHost;
    readonly renderer: THREE.WebGLRenderer;
    resize(): void;
    readonly scene: THREE.Scene;
    setFireArcsVisible(visible: boolean): void;
    setParticlesVisible(visible: boolean): void;
    readonly turret: TurretRig;
}

export class CaptureRig {
    constructor(private readonly host: CaptureViewport) {}

    /**
     * Renders one frame at a chosen size and hands back the PNG bytes.
     *
     * The renderer is resized, drawn and put back inside ONE synchronous block. Without
     * `preserveDrawingBuffer` the drawing buffer is only guaranteed to hold its contents until the
     * browser composites, which cannot happen while this is running - and asking for that flag
     * instead would cost every ordinary frame for the sake of the rare capture.
     *
     * What it hides is passed in rather than read from the panel's own toggles: an icon usually
     * wants no grid, no floor and no hardpoint markers even while the reader is working with all
     * three switched on, and a capture that quietly changed what they were looking at would be
     * worse than one that asked.
     */
    capture(options: {
        width: number;
        height: number;
        background: string | null;
        grid: boolean;
        floor: boolean;
        particles: boolean;
        /** The axes, the skeleton and the firing arcs: diagnostics, not part of the subject. */
        annotations: boolean;
    }): string {
        const before = {
            width: this.host.canvas.width,
            height: this.host.canvas.height,
            aspect: this.host.camera.aspect,
            pixelRatio: this.host.renderer.getPixelRatio(),
            background: this.host.scene.background,
            grid: this.host.environment.grid.visible,
            floor: this.host.environment.floor.visible,
            catcher: this.host.environment.shadowCatcher.visible,
            particles: this.host.particles.particlesVisible,
            axes: this.host.environment.axes.visible,
            joints: this.host.overlay.joints.visible,
            jointLines: this.host.overlay.jointLines.visible,
            selection: this.host.overlay.selectionBoxes.visible,
            arcs: this.host.turret.arcsShown,
        };

        try {
            // One device pixel per requested pixel: a capture is measured in pixels, not in the
            // reader's display scaling, or the same request would give a different file on a
            // different monitor.
            this.host.renderer.setPixelRatio(1);
            this.host.renderer.setSize(options.width, options.height, false);
            // The frame drawn over the viewport, exactly: the largest rectangle of the capture's
            // shape that fits the viewport, centred (captureFrame). Rendered as a view offset on the
            // viewport's own projection, so a capture wider than the viewport shows what the frame
            // shows - not more to either side, which is what widening the aspect used to do.
            const frame = captureFrame(
                before.width,
                before.height,
                options.width,
                options.height,
            );
            this.host.camera.setViewOffset(
                before.width,
                before.height,
                frame.x,
                frame.y,
                frame.width,
                frame.height,
            );
            this.host.camera.updateProjectionMatrix();

            // Null is TRANSPARENT, which is what an icon wants: the alpha the renderer was created
            // with is what makes it possible at all.
            this.host.scene.background =
                options.background === null
                    ? null
                    : new THREE.Color(options.background);

            this.host.environment.grid.visible = options.grid;
            this.host.environment.floor.visible = options.floor;
            this.host.environment.shadowCatcher.visible =
                options.floor && before.catcher;
            this.host.setParticlesVisible(options.particles);

            // The axes, the skeleton and the firing arcs are things drawn to help someone READ the
            // model. They are not part of it, and an icon with a coloured cross through it is not
            // an icon - which is exactly what the first capture came out as.
            this.host.environment.axes.visible =
                options.annotations && before.axes;
            this.host.overlay.joints.visible =
                options.annotations && before.joints;
            this.host.overlay.jointLines.visible =
                options.annotations && before.jointLines;

            // A selection box is a thing drawn to help someone READ the model, exactly like the
            // axes and the skeleton, and an icon with an amber cage around it is not an icon.
            this.host.overlay.selectionBoxes.visible =
                options.annotations && before.selection;

            if (!options.annotations) {
                this.host.setFireArcsVisible(false);
            }

            this.host.cameraRig.prepareForCamera();

            // A sprite is oriented against the VIEW matrix - a streak's length is a screen-space
            // measurement - so it has to be re-aimed for this camera. A zero step re-aims without
            // advancing the simulation: a capture must not move the effects on.
            if (this.host.particles.particlesVisible) {
                for (const entry of this.host.particles.particleSystems.values()) {
                    entry.instance.update(
                        0,
                        this.host.camera.matrixWorldInverse,
                        this.host.particles.windVector,
                    );
                }
            }

            this.host.environment.renderFrame();

            return this.host.canvas.toDataURL("image/png");
        } finally {
            // Always put the room back, including when the render throws: a failed capture must not
            // leave the reader looking at a 64-pixel viewport with the grid switched off.
            this.host.renderer.setPixelRatio(before.pixelRatio);
            this.host.scene.background = before.background;
            this.host.environment.grid.visible = before.grid;
            this.host.environment.floor.visible = before.floor;
            this.host.environment.shadowCatcher.visible = before.catcher;
            this.host.setParticlesVisible(before.particles);
            this.host.environment.axes.visible = before.axes;
            this.host.overlay.joints.visible = before.joints;
            this.host.overlay.jointLines.visible = before.jointLines;
            this.host.overlay.selectionBoxes.visible = before.selection;
            this.host.setFireArcsVisible(before.arcs);
            this.host.camera.clearViewOffset();
            this.host.camera.aspect = before.aspect;
            this.host.camera.updateProjectionMatrix();
            this.host.resize();
            this.host.cameraRig.prepareForCamera();
            this.host.environment.renderFrame();
        }
    }
}
