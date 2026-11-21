// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { DEFAULT_LIGHTS } from "../../shared/viewerSettings";
import { VIEWPORT_BACKGROUND } from "./backdrop";
import { type EnvironmentHost, EnvironmentRig } from "./environmentRig";
import { lightDirection } from "./lighting";
import { shadowCatcherOpacity } from "./materialState";
import { standInContext } from "./viewportContext.testing";

/**
 * A rig over a stand-in viewport: an empty scene, a camera, a unit model at the origin and no particle
 * systems. Nothing here draws a frame, so the renderer is only asked for its drawing-buffer size.
 */
function rigWith(
    drawn: THREE.Mesh[] = [],
    room: {
        centre?: { x: number; y: number; z: number };
        drawingBuffer?: { width: number; height: number };
    } = {},
): {
    environment: EnvironmentRig;
    scene: THREE.Scene;
} {
    const scene = new THREE.Scene();
    const centre = room.centre ?? { x: 0, y: 0, z: 0 };
    const buffer = room.drawingBuffer ?? { width: 100, height: 100 };
    const context = standInContext({
        levels: { alt: 0, lod: 0 },
        rules: {},
        camera: new THREE.PerspectiveCamera(45, 1, 0.1, 10000),
        canvas: { clientWidth: 100, clientHeight: 100 },
        renderer: {
            getDrawingBufferSize: (target: THREE.Vector2) =>
                target.set(buffer.width, buffer.height),
        },
        scene,
        particles: {
            particleSystems: new Map(),
            particlesVisible: true,
            wind: { heading: 0, speed: 0 },
        },
        cameraRig: {
            boundingSphere: () => ({ center: centre, radius: 1 }),
        },
        assembly: {
            forEachDrawnMesh: (visit: (mesh: THREE.Mesh) => void) =>
                drawn.forEach(visit),
        },
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
    /** A solid render mesh, and a shadow mesh, as a model's file names their shaders. */
    const hull = (): THREE.Mesh => {
        const mesh = new THREE.Mesh();
        mesh.userData.alamo = { alamoShader: "MeshGloss.fx" };
        return mesh;
    };
    const shadowMesh = (): THREE.Mesh => {
        const mesh = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1));
        mesh.userData.alamo = { alamoShader: "MeshShadowVolume.fx" };
        return mesh;
    };

    it("casts no shadow-map shadow from render geometry in the Game renderer", () => {
        // #412: a model without a shadow mesh cast the Default renderer's shadow under Game, which
        // the game never draws.
        const mesh = hull();
        const { environment } = rigWith([mesh]);

        environment.setShadowVolumes(true);

        assert.equal(mesh.castShadow, false);
        assert.equal(environment.castsIntoShadowMap(mesh), false);
    });

    it("casts the stand-in shadow under Game while it is on, and stops when it is off", () => {
        const mesh = hull();
        const { environment } = rigWith([mesh]);
        environment.setShadowVolumes(true);

        environment.setStandInShadow(true);
        assert.equal(mesh.castShadow, true);

        environment.setStandInShadow(false);
        assert.equal(mesh.castShadow, false);
    });

    it("casts no stand-in from a model with a shadow mesh", () => {
        const mesh = hull();
        const { environment } = rigWith([mesh]);
        const root = new THREE.Group();
        root.add(shadowMesh());
        environment.setShadowVolumes(true);
        environment.setStandInShadow(true);

        environment.collectShadowVolumes(root);

        assert.equal(environment.shadowMesh().authored, true);
        assert.equal(mesh.castShadow, false);
    });

    it("casts from render geometry in the Default renderer, stand-in or none", () => {
        const mesh = hull();
        const { environment } = rigWith([mesh]);

        environment.setShadowVolumes(false);
        assert.equal(mesh.castShadow, true);

        environment.setStandInShadow(true);
        assert.equal(mesh.castShadow, true);
    });

    /**
     * The seam a model's whole look reaches every translated material through: the per-model light
     * scale the effects multiply by - its colour the tint, its alpha the fade.
     */
    it("hands a mesh its model's look as the light scale", () => {
        const { environment } = rigWith();
        const mesh = new THREE.Mesh();
        const identity = new THREE.Matrix4();

        const plain = environment.frameFor(
            mesh,
            identity,
            identity,
            identity,
            new THREE.Vector3(),
        );
        assert.deepEqual(plain.lightScale, [1, 1, 1, 1]);

        const faded = environment.frameFor(
            mesh,
            identity,
            identity,
            identity,
            new THREE.Vector3(),
            { r: 1, g: 0.25, b: 0.25, a: 0.5 },
        );
        assert.deepEqual(faded.lightScale, [1, 0.25, 0.25, 0.5]);
    });

    /** One frame for a mesh at the origin, with nothing but the rig to read. */
    const frameOf = (environment: EnvironmentRig) => {
        const identity = new THREE.Matrix4();
        return environment.frameFor(
            new THREE.Mesh(),
            identity,
            identity,
            identity,
            new THREE.Vector3(),
        );
    };

    // #418, measured in the game (FoC x64, 2024 release): DIR_LIGHT_SPECULAR_0 is twice the specular
    // colour times the sun's intensity.
    it("hands the sun's highlight as twice its specular times its intensity", () => {
        const { environment } = rigWith();
        environment.setLightRig({
            ...DEFAULT_LIGHTS,
            sun: { ...DEFAULT_LIGHTS.sun, intensity: 0.75 },
            specular: [1, 0.5, 0.25],
        });

        assert.deepEqual(
            frameOf(environment).lights[0]?.specular,
            [1.5, 0.75, 0.375, 0],
        );
    });

    // #418, measured: the game writes only light 0's uniforms. A fill's DIR_LIGHT_*_1 and _2 keep
    // whatever the effect declares, so the frame carries no fill at all.
    it("hands the shaders the sun alone, no fill light", () => {
        const { environment } = rigWith();

        assert.equal(frameOf(environment).lights.length, 1);
    });

    // #418: the key light is PLACED off the model's centre for its shadow camera, so its position is
    // not a direction once the model is off the origin.
    it("hands the rig's sun direction wherever the model stands", () => {
        const { environment } = rigWith([], {
            centre: { x: 400, y: -50, z: 900 },
        });
        environment.setLightAngles(30, 20);

        const vector = frameOf(environment).lights[0]?.vector ?? [];
        const wanted = lightDirection(30, 20);
        [wanted.x, wanted.y, wanted.z].forEach((component, axis) =>
            assert.ok(
                Math.abs((vector[axis] ?? NaN) - component) < 1e-9,
                `axis ${axis}: ${vector[axis]} against ${component}`,
            ),
        );
    });

    // #418: RESOLUTION_CONSTANTS in the drawing buffer's pixels - not the canvas's CSS pixels, which
    // differ by the device pixel ratio - with the half-texel terms in z and w.
    it("hands the resolution in drawing-buffer pixels with the half-texel terms", () => {
        const { environment } = rigWith([], {
            drawingBuffer: { width: 200, height: 125 },
        });

        assert.deepEqual(frameOf(environment).resolution, [
            200,
            125,
            0.5 / 200,
            0.5 / 125,
        ]);
    });

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
