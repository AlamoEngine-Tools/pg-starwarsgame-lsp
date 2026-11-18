// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type CaptureHost, CaptureRig } from "./captureRig";
import { type ViewportContext } from "./viewportContext";

/**
 * A rig over a stand-in viewport: a real scene and camera, a renderer that records its size, and the
 * room, overlay and particle toggles as plain objects. Drawing a frame records what was visible at that
 * moment; it can be told to throw.
 */
function rigWith(throwOnDraw = false): {
    capturer: CaptureRig;
    context: ViewportContext;
    shots: Record<string, boolean | number | null>[];
    room: {
        grid: THREE.Object3D;
        floor: THREE.Object3D;
        shadowCatcher: THREE.Object3D;
        axes: THREE.Object3D;
    };
    overlay: {
        joints: THREE.Object3D;
        jointLines: THREE.Object3D;
        selectionBoxes: THREE.Object3D;
    };
} {
    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x1b1d21);
    const camera = new THREE.PerspectiveCamera(50, 2, 0.1, 100);
    let pixelRatio = 2;
    let size = { width: 200, height: 100 };
    const room = {
        grid: new THREE.Object3D(),
        floor: new THREE.Object3D(),
        shadowCatcher: new THREE.Object3D(),
        axes: new THREE.Object3D(),
    };
    room.floor.visible = false;
    room.shadowCatcher.visible = false;
    const overlay = {
        joints: new THREE.Object3D(),
        jointLines: new THREE.Object3D(),
        selectionBoxes: new THREE.Object3D(),
    };
    const particles = {
        particlesVisible: true,
        particleSystems: new Map(),
        windVector: { x: 0, y: 0, z: 0 },
    };
    const turret = { arcsShown: true };
    const shots: Record<string, boolean | number | null>[] = [];
    const context = {
        scene,
        camera,
        canvas: {
            width: 200,
            height: 100,
            toDataURL: () => "data:image/png;base64,AAAA",
        },
        renderer: {
            getPixelRatio: () => pixelRatio,
            setPixelRatio: (ratio: number) => {
                pixelRatio = ratio;
            },
            setSize: (width: number, height: number) => {
                size = { width, height };
            },
        },
        environment: {
            ...room,
            renderFrame: () => {
                shots.push({
                    width: size.width,
                    height: size.height,
                    pixelRatio,
                    background:
                        scene.background === null
                            ? null
                            : (scene.background as THREE.Color).getHex(),
                    grid: room.grid.visible,
                    floor: room.floor.visible,
                    axes: room.axes.visible,
                    joints: overlay.joints.visible,
                    particles: particles.particlesVisible,
                    arcs: turret.arcsShown,
                });
                if (throwOnDraw && shots.length === 1) {
                    throw new Error("lost the context");
                }
            },
        },
        overlay,
        particles: Object.assign(particles, {
            setParticlesVisible: (visible: boolean) => {
                particles.particlesVisible = visible;
            },
        }),
        turret: Object.assign(turret, {
            setFireArcsVisible: (visible: boolean) => {
                turret.arcsShown = visible;
            },
        }),
        cameraRig: { prepareForCamera: () => undefined },
    } as unknown as ViewportContext;
    const host = {
        resize: () => undefined,
    } as unknown as CaptureHost;
    return {
        capturer: new CaptureRig(context, host),
        context,
        shots,
        room,
        overlay,
    };
}

const ICON = {
    width: 64,
    height: 64,
    background: null,
    grid: false,
    floor: false,
    particles: false,
    annotations: false,
};

describe("CaptureRig", () => {
    it("draws the shot at its own size with what it asked to hide hidden, and hands back the PNG", () => {
        const { capturer, shots } = rigWith();

        const png = capturer.capture(ICON);

        assert.equal(png, "data:image/png;base64,AAAA");
        assert.deepEqual(shots[0], {
            width: 64,
            height: 64,
            pixelRatio: 1,
            background: null,
            grid: false,
            floor: false,
            axes: false,
            joints: false,
            particles: false,
            arcs: false,
        });
    });

    it("puts every toggle, the background and the pixel ratio back afterwards", () => {
        const { capturer, context, room, overlay } = rigWith();

        capturer.capture(ICON);

        assert.equal(
            (context.scene.background as THREE.Color).getHex(),
            0x1b1d21,
        );
        assert.equal(context.renderer.getPixelRatio(), 2);
        assert.equal(room.grid.visible, true);
        assert.equal(room.axes.visible, true);
        assert.equal(overlay.joints.visible, true);
        assert.equal(context.particles.particlesVisible, true);
        assert.equal(context.turret.arcsShown, true);
        assert.equal(context.camera.view?.enabled ?? false, false);
    });

    it("puts everything back when the shot itself fails", () => {
        const { capturer, room, context } = rigWith(true);

        assert.throws(() => capturer.capture(ICON), /lost the context/);

        assert.equal(room.grid.visible, true);
        assert.equal(context.particles.particlesVisible, true);
        assert.equal(context.renderer.getPixelRatio(), 2);
    });
});
