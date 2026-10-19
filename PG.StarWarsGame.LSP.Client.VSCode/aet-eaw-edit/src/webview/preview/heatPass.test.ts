// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { HeatPass } from "./heatPass";

/**
 * A renderer that records what the pass asks of it, in order. The pass is a SEQUENCE - what is
 * drawn where, and what is cleared between - and the sequence is what the game fixes.
 */
class RecordingRenderer {
    readonly calls: string[] = [];
    autoClear = true;
    private target: THREE.WebGLRenderTarget | null = null;
    private readonly clearColor = new THREE.Color(0, 0, 0);
    private clearAlpha = 1;

    getDrawingBufferSize(target: THREE.Vector2): THREE.Vector2 {
        return target.set(64, 32);
    }

    getClearColor(target: THREE.Color): THREE.Color {
        return target.copy(this.clearColor);
    }

    getClearAlpha(): number {
        return this.clearAlpha;
    }

    setClearColor(color: THREE.Color, alpha: number): void {
        this.clearColor.copy(color);
        this.clearAlpha = alpha;
        // The raw channel, not the hex string: colour management would encode it on the way out.
        this.calls.push(`setClearColor(${Math.round(color.r * 255)})`);
    }

    setRenderTarget(target: THREE.WebGLRenderTarget | null): void {
        this.target = target;
        this.calls.push(
            `setRenderTarget(${target === null ? "canvas" : "target"})`,
        );
    }

    clear(color = true, depth = true, stencil = true): void {
        this.calls.push(
            `clear(${[color && "color", depth && "depth", stencil && "stencil"]
                .filter(Boolean)
                .join("+")})`,
        );
    }

    render(scene: THREE.Object3D, camera: THREE.Camera): void {
        const where = this.target === null ? "canvas" : "target";
        const layers =
            camera.layers.mask === 1
                ? "all"
                : `layer${Math.log2(camera.layers.mask)}`;
        this.calls.push(
            `render(${scene.type === "Scene" && scene.children.length === 1 && scene.children[0].type === "Mesh" && camera.type === "Camera" ? "quad" : layers} -> ${where}, autoClear=${this.autoClear})`,
        );
    }

    copyFramebufferToTexture(texture: THREE.Texture): void {
        this.calls.push(`copy(${texture.name || "unnamed"})`);
    }
}

describe("HeatPass", () => {
    /**
     * Measured in the game (FoC x64, 2024 release): heat sprites are drawn by the ordinary
     * particle path, depth-tested against the SCENE's depth buffer, into the back buffer after a
     * colour-only clear - the distortion field is a copy of the back buffer taken afterwards, and
     * the scene texture a copy taken before. Nothing is ever rendered into a target of its own,
     * so nothing is ever drawn without the scene's depth in front of it. Drawing the sprites into a
     * depthless target let a flamethrower's haze show through the hull holding it.
     */
    it("draws the heat sprites onto the canvas over the scene's depth, then copies the field off it", () => {
        const renderer = new RecordingRenderer();
        const pass = new HeatPass(3);
        const scene = new THREE.Scene();
        const camera = new THREE.PerspectiveCamera();

        pass.render(renderer as unknown as THREE.WebGLRenderer, scene, camera);

        assert.deepEqual(renderer.calls, [
            "setRenderTarget(canvas)",
            "render(all -> canvas, autoClear=true)",
            "copy(scene)",
            "setClearColor(128)",
            "clear(color)",
            "render(layer3 -> canvas, autoClear=false)",
            "copy(distortion)",
            "setClearColor(0)",
            "setRenderTarget(canvas)",
            "render(quad -> canvas, autoClear=true)",
        ]);
    });

    it("hands the camera's layers and the renderer's clearing back as it found them", () => {
        const renderer = new RecordingRenderer();
        const pass = new HeatPass(3);
        const scene = new THREE.Scene();
        const camera = new THREE.PerspectiveCamera();
        camera.layers.enable(5);
        const before = camera.layers.mask;

        pass.render(renderer as unknown as THREE.WebGLRenderer, scene, camera);

        assert.equal(camera.layers.mask, before);
        assert.equal(renderer.autoClear, true);
    });
});
