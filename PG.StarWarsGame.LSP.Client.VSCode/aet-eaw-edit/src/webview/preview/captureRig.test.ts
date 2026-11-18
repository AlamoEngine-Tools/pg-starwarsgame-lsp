// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type CaptureHost, CaptureRig } from "./captureRig";
import { type ViewportContext } from "./viewportContext";
import { standInContext } from "./viewportContext.testing";
import { type CaptureShot } from "./viewportTypes";

const STAGED = ["environment", "overlay", "particles", "turret"];

/**
 * A rig over a stand-in viewport: a real camera, a renderer that records its size, and four layers that
 * record the shot they were staged for and when they were put back. Drawing a frame records the size and
 * how far the staging had got at that moment; it can be told to throw.
 */
function rigWith(throwOnDraw = false): {
    capturer: CaptureRig;
    context: ViewportContext;
    shots: Record<string, unknown>[];
    staged: Map<string, CaptureShot>;
    restored: string[];
} {
    const camera = new THREE.PerspectiveCamera(50, 2, 0.1, 100);
    let pixelRatio = 2;
    let size = { width: 200, height: 100 };
    const staged = new Map<string, CaptureShot>();
    const restored: string[] = [];
    const reaimed: number[] = [];
    const shots: Record<string, unknown>[] = [];
    const layer = (name: string) => ({
        stageForCapture: (shot: CaptureShot) => {
            staged.set(name, shot);
            return () => {
                restored.push(name);
            };
        },
    });
    const context = standInContext({
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
            ...layer("environment"),
            renderFrame: () => {
                shots.push({
                    width: size.width,
                    height: size.height,
                    pixelRatio,
                    staged: [...staged.keys()].sort(),
                    restored: [...restored],
                    reaimed: reaimed.length,
                });
                if (throwOnDraw && shots.length === 1) {
                    throw new Error("lost the context");
                }
            },
        },
        overlay: layer("overlay"),
        particles: {
            ...layer("particles"),
            reaimAtCamera: () => reaimed.push(1),
        },
        turret: layer("turret"),
        cameraRig: { prepareForCamera: () => undefined },
    });
    const host = {
        resize: () => undefined,
    } as unknown as CaptureHost;
    return {
        capturer: new CaptureRig(context, host),
        context,
        shots,
        staged,
        restored,
    };
}

const ICON: CaptureShot = {
    width: 64,
    height: 64,
    background: null,
    grid: false,
    floor: false,
    particles: false,
    annotations: false,
};

describe("CaptureRig", () => {
    it("draws the shot at its own size with every layer staged for it, and hands back the PNG", () => {
        const { capturer, shots, staged } = rigWith();

        const png = capturer.capture(ICON);

        assert.equal(png, "data:image/png;base64,AAAA");
        assert.deepEqual(shots[0], {
            width: 64,
            height: 64,
            pixelRatio: 1,
            staged: STAGED,
            restored: [],
            reaimed: 1,
        });
        for (const shot of staged.values()) {
            assert.equal(shot, ICON);
        }
    });

    it("puts every layer, the pixel ratio and the projection back afterwards", () => {
        const { capturer, context, restored } = rigWith();

        capturer.capture(ICON);

        assert.deepEqual([...restored].sort(), STAGED);
        assert.equal(context.renderer.getPixelRatio(), 2);
        assert.equal(context.camera.view?.enabled ?? false, false);
    });

    it("puts everything back when the shot itself fails", () => {
        const { capturer, context, restored } = rigWith(true);

        assert.throws(() => capturer.capture(ICON), /lost the context/);

        assert.deepEqual([...restored].sort(), STAGED);
        assert.equal(context.renderer.getPixelRatio(), 2);
    });
});
