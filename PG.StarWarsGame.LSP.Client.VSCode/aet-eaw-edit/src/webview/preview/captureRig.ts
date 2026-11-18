// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's still capture: the renderer resized, the subject framed for the shot, every layer asked to
// stage its own part of the room for it, one frame drawn and read back as a PNG, and every one of those put
// back afterwards. It drives the renderer and the camera; what a layer shows is the layer's.

import { captureFrame } from "./capture";
import { type ViewportContext } from "./viewportContext";
import { type CaptureShot } from "./viewportTypes";

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
     *
     * Each layer stages its own part of the room and hands back what puts it back, so a new reading
     * aid is hidden by the layer that draws it rather than by a list kept here.
     */
    capture(shot: CaptureShot): string {
        const before = {
            width: this.context.canvas.width,
            height: this.context.canvas.height,
            aspect: this.context.camera.aspect,
            pixelRatio: this.context.renderer.getPixelRatio(),
        };
        const restores: (() => void)[] = [];

        try {
            // One device pixel per requested pixel: a capture is measured in pixels, not in the
            // reader's display scaling, or the same request would give a different file on a
            // different monitor.
            this.context.renderer.setPixelRatio(1);
            this.context.renderer.setSize(shot.width, shot.height, false);
            // The frame drawn over the viewport, exactly: the largest rectangle of the capture's
            // shape that fits the viewport, centred (captureFrame). Rendered as a view offset on the
            // viewport's own projection, so a capture wider than the viewport shows what the frame
            // shows - not more to either side, which is what widening the aspect used to do.
            const frame = captureFrame(
                before.width,
                before.height,
                shot.width,
                shot.height,
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

            // One at a time, so a layer that throws leaves the ones before it to be put back.
            for (const layer of [
                this.context.environment,
                this.context.particles,
                this.context.overlay,
                this.context.turret,
            ]) {
                restores.push(layer.stageForCapture(shot));
            }

            this.context.cameraRig.prepareForCamera();
            // Re-aimed for this camera: a capture must not move the effects on.
            this.context.particles.reaimAtCamera();

            this.context.environment.renderFrame();

            return this.context.canvas.toDataURL("image/png");
        } finally {
            // Always put the room back, including when the render throws: a failed capture must not
            // leave the reader looking at a 64-pixel viewport with the grid switched off.
            this.context.renderer.setPixelRatio(before.pixelRatio);
            for (const restore of restores.reverse()) {
                restore();
            }
            this.context.camera.clearViewOffset();
            this.context.camera.aspect = before.aspect;
            this.context.camera.updateProjectionMatrix();
            this.host.resize();
            this.context.cameraRig.prepareForCamera();
            this.context.environment.renderFrame();
        }
    }
}
