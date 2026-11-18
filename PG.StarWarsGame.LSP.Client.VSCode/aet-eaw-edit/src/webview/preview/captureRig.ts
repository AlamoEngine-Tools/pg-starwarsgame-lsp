// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's still capture: the renderer resized, the subject framed for the shot, the grid, floor,
// particles and annotations shown or hidden as asked, one frame drawn and read back as a PNG, and every one
// of those put back afterwards. It reads the renderer, the camera and the layers whose toggles it borrows;
// nothing else.

import { captureFrame } from "./capture";
import * as THREE from "three";
import { type ViewportContext } from "./viewportContext";

/** What CaptureRig still asks of the viewport itself: what only the viewport has. */
export interface CaptureHost {
    resize(): void;
}

export class CaptureRig {
    constructor(
        private readonly context: ViewportContext,
        private readonly host: CaptureHost,
    ) {}

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
            width: this.context.canvas.width,
            height: this.context.canvas.height,
            aspect: this.context.camera.aspect,
            pixelRatio: this.context.renderer.getPixelRatio(),
            background: this.context.scene.background,
            grid: this.context.environment.grid.visible,
            floor: this.context.environment.floor.visible,
            catcher: this.context.environment.shadowCatcher.visible,
            particles: this.context.particles.particlesVisible,
            axes: this.context.environment.axes.visible,
            joints: this.context.overlay.joints.visible,
            jointLines: this.context.overlay.jointLines.visible,
            selection: this.context.overlay.selectionBoxes.visible,
            arcs: this.context.turret.arcsShown,
        };

        try {
            // One device pixel per requested pixel: a capture is measured in pixels, not in the
            // reader's display scaling, or the same request would give a different file on a
            // different monitor.
            this.context.renderer.setPixelRatio(1);
            this.context.renderer.setSize(options.width, options.height, false);
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
            this.context.camera.setViewOffset(
                before.width,
                before.height,
                frame.x,
                frame.y,
                frame.width,
                frame.height,
            );
            this.context.camera.updateProjectionMatrix();

            // Null is TRANSPARENT, which is what an icon wants: the alpha the renderer was created
            // with is what makes it possible at all.
            this.context.scene.background =
                options.background === null
                    ? null
                    : new THREE.Color(options.background);

            this.context.environment.grid.visible = options.grid;
            this.context.environment.floor.visible = options.floor;
            this.context.environment.shadowCatcher.visible =
                options.floor && before.catcher;
            this.context.particles.setParticlesVisible(options.particles);

            // The axes, the skeleton and the firing arcs are things drawn to help someone READ the
            // model. They are not part of it, and an icon with a coloured cross through it is not
            // an icon - which is exactly what the first capture came out as.
            this.context.environment.axes.visible =
                options.annotations && before.axes;
            this.context.overlay.joints.visible =
                options.annotations && before.joints;
            this.context.overlay.jointLines.visible =
                options.annotations && before.jointLines;

            // A selection box is a thing drawn to help someone READ the model, exactly like the
            // axes and the skeleton, and an icon with an amber cage around it is not an icon.
            this.context.overlay.selectionBoxes.visible =
                options.annotations && before.selection;

            if (!options.annotations) {
                this.context.turret.setFireArcsVisible(false);
            }

            this.context.cameraRig.prepareForCamera();

            // A sprite is oriented against the VIEW matrix - a streak's length is a screen-space
            // measurement - so it has to be re-aimed for this camera. A zero step re-aims without
            // advancing the simulation: a capture must not move the effects on.
            if (this.context.particles.particlesVisible) {
                for (const entry of this.context.particles.particleSystems.values()) {
                    entry.instance.update(
                        0,
                        this.context.camera.matrixWorldInverse,
                        this.context.particles.windVector,
                    );
                }
            }

            this.context.environment.renderFrame();

            return this.context.canvas.toDataURL("image/png");
        } finally {
            // Always put the room back, including when the render throws: a failed capture must not
            // leave the reader looking at a 64-pixel viewport with the grid switched off.
            this.context.renderer.setPixelRatio(before.pixelRatio);
            this.context.scene.background = before.background;
            this.context.environment.grid.visible = before.grid;
            this.context.environment.floor.visible = before.floor;
            this.context.environment.shadowCatcher.visible = before.catcher;
            this.context.particles.setParticlesVisible(before.particles);
            this.context.environment.axes.visible = before.axes;
            this.context.overlay.joints.visible = before.joints;
            this.context.overlay.jointLines.visible = before.jointLines;
            this.context.overlay.selectionBoxes.visible = before.selection;
            this.context.turret.setFireArcsVisible(before.arcs);
            this.context.camera.clearViewOffset();
            this.context.camera.aspect = before.aspect;
            this.context.camera.updateProjectionMatrix();
            this.host.resize();
            this.context.cameraRig.prepareForCamera();
            this.context.environment.renderFrame();
        }
    }
}
