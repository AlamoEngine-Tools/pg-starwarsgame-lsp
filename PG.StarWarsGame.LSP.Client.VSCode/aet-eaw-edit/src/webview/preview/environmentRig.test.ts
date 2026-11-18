// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { VIEWPORT_BACKGROUND } from "./backdrop";
import { type EnvironmentHost, EnvironmentRig } from "./environmentRig";
import { shadowCatcherOpacity } from "./materialState";
import { standInContext } from "./viewportContext.testing";

/**
 * A rig over a stand-in viewport: an empty scene, a camera, a unit model at the origin and no particle
 * systems. Nothing here draws a frame, so the renderer is never reached.
 */
function rigWith(): { environment: EnvironmentRig; scene: THREE.Scene } {
    const scene = new THREE.Scene();
    const context = standInContext({
        levels: { alt: 0, lod: 0, stealthed: false },
        camera: new THREE.PerspectiveCamera(45, 1, 0.1, 10000),
        canvas: { clientWidth: 100, clientHeight: 100 },
        renderer: null,
        scene,
        particles: {
            particleSystems: new Map(),
            particlesVisible: true,
            wind: { heading: 0, speed: 0 },
        },
        cameraRig: {
            boundingSphere: () => ({ center: { x: 0, y: 0, z: 0 }, radius: 1 }),
        },
        assembly: { forEachDrawnMesh: () => undefined },
        animation: { skinPalette: () => new Float32Array(0) },
    });
    const host = {
        elapsed: 0,
    } as unknown as EnvironmentHost;
    return { environment: new EnvironmentRig(context, host), scene };
}

const keyLightOf = (scene: THREE.Scene): THREE.DirectionalLight => {
    const light = scene.children.find(
        (child): child is THREE.DirectionalLight =>
            child instanceof THREE.DirectionalLight,
    );
    assert.ok(light, "no key light in the scene");
    return light;
};

describe("EnvironmentRig", () => {
    it("puts the room in the scene, the ground hidden until asked for", () => {
        const { environment, scene } = rigWith();

        for (const part of [
            environment.grid,
            environment.axes,
            environment.floor,
            environment.shadowCatcher,
        ]) {
            assert.ok(scene.children.includes(part));
        }
        assert.equal(environment.floor.visible, false);
        assert.equal(environment.shadowCatcher.visible, false);
        assert.equal(environment.grid.visible, true);
    });

    it("shows the floor and its shadow catcher together, and moves the whole ground to a level", () => {
        const { environment } = rigWith();

        environment.setFloorVisible(true);
        environment.setFloorLevel(-2.5);

        assert.equal(environment.floor.visible, true);
        assert.equal(environment.shadowCatcher.visible, true);
        for (const part of [
            environment.floor,
            environment.grid,
            environment.axes,
            environment.shadowCatcher,
        ]) {
            assert.equal(part.position.y, -2.5);
        }
    });

    it("paints space black behind the stars and the neutral grey behind a flat backdrop", () => {
        const { environment, scene } = rigWith();

        environment.setBackground("starfield");
        assert.equal((scene.background as THREE.Color).getHex(), 0x05070c);
        // Anywhere under the scene: the stars are parented to the camera, which joins the scene.
        let stars: THREE.Points | undefined;
        scene.traverse((node) => {
            if (node instanceof THREE.Points) {
                stars = node;
            }
        });
        assert.ok(stars, "no starfield in the scene");
        assert.equal(stars.visible, true);

        environment.setBackground("flat");
        assert.equal(
            (scene.background as THREE.Color).getHex(),
            VIEWPORT_BACKGROUND,
        );
        assert.equal(stars.visible, false);
    });

    it("moves the key light with the light angles", () => {
        const { environment, scene } = rigWith();
        const before = keyLightOf(scene).position.clone();

        environment.setLightAngles(180, 10);

        assert.notDeepEqual(
            keyLightOf(scene).position.toArray(),
            before.toArray(),
        );
    });

    it("sets the shadow catcher's strength from the shadow colour", () => {
        const { environment } = rigWith();

        environment.setShadowColour(0x404040);

        assert.equal(
            environment.shadowCatcher.material instanceof
                THREE.ShadowMaterial &&
                environment.shadowCatcher.material.opacity,
            shadowCatcherOpacity(0x404040),
        );
    });

    it("keeps the stars inside the far plane the camera is fitted to", () => {
        const { environment } = rigWith();
        environment.setBackground("starfield");
        const camera = (
            environment as unknown as {
                context: { camera: THREE.PerspectiveCamera };
            }
        ).context.camera;
        camera.far = 5000;

        environment.followCamera();

        let stars: THREE.Points | undefined;
        camera.traverse((node) => {
            if (node instanceof THREE.Points) {
                stars = node;
            }
        });
        assert.ok(stars, "no starfield on the camera");
        assert.equal(stars.scale.x, 2000);
    });

    it("sets the room as a shot asks and puts it back as it was", () => {
        const { environment, scene } = rigWith();
        environment.setBackground("starfield");
        environment.setFloorVisible(true);

        const restore = environment.stageForCapture({
            width: 64,
            height: 64,
            background: null,
            grid: false,
            floor: true,
            particles: false,
            annotations: false,
        });

        assert.equal(
            scene.background === null,
            true,
            "the shot is not transparent",
        );
        assert.equal(environment.grid.visible, false);
        assert.equal(environment.axes.visible, false);
        assert.equal(environment.floor.visible, true);
        assert.equal(environment.shadowCatcher.visible, true);

        restore();

        assert.equal((scene.background as THREE.Color).getHex(), 0x05070c);
        assert.equal(environment.grid.visible, true);
        assert.equal(environment.axes.visible, true);
        assert.equal(environment.floor.visible, true);
        assert.equal(environment.shadowCatcher.visible, true);
    });

    it("draws a shot over the backdrop it names", () => {
        const { environment, scene } = rigWith();

        environment.stageForCapture({
            ...{
                width: 64,
                height: 64,
                background: null,
                grid: false,
                floor: true,
                particles: false,
                annotations: false,
            },
            background: "#ff0000",
        });

        assert.equal((scene.background as THREE.Color).getHex(), 0xff0000);
    });
});
