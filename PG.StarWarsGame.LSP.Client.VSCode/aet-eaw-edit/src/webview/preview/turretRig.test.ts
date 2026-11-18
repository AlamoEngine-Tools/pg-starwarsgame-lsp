// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import * as THREE from "three";

import { type LoadedPart } from "./loadedPart";
import { axisRange, TURRET_AT_REST, type TurretHandle } from "./turretHandles";
import { type TurretHost, TurretRig } from "./turretRig";
import { standInContext } from "./viewportContext.testing";

/** A part whose one turret bone hangs under a mount, the way a hull carries one. */
function turretPart(): {
    part: LoadedPart;
    mount: THREE.Object3D;
    turret: THREE.Object3D;
} {
    const root = new THREE.Group();
    const mount = new THREE.Object3D();
    const turret = new THREE.Object3D();
    root.add(mount);
    mount.add(turret);
    return {
        part: {
            id: "p",
            root,
            bones: new Map([["turret", turret]]),
            bonesByIndex: new Map(),
            subjectId: "subject",
        },
        mount,
        turret,
    };
}

/** A rig over a stand-in viewport holding the parts given. Nothing here projects or draws an arc. */
function rigWith(parts: ReadonlyMap<string, LoadedPart>): TurretRig {
    const context = standInContext({
        camera: new THREE.PerspectiveCamera(),
        canvas: null,
        modelRoot: new THREE.Group(),
        parts,
        cameraRig: { modelSpan: () => 10 },
        assembly: { fireBoneAttachment: () => null },
        modelScale: 1,
    });
    const host = {
        onTurretAimed: null,
    } as unknown as TurretHost;
    return new TurretRig(context, host);
}

const handle: TurretHandle = {
    id: "weapon:turret",
    partId: "p",
    turretBone: "Turret",
    barrelBone: null,
    yaw: axisRange("yaw", 90),
    pitch: axisRange("pitch", 30),
};

const overlaysUnder = (node: THREE.Object3D): number =>
    node.children.filter((child) => child.userData.aetOverlay === true).length;

/** The handle anchors hanging under a node, which is where a handle's visibility is set. */
const anchorsUnder = (node: THREE.Object3D): THREE.Object3D[] =>
    node.children.filter((child) => child.userData.aetOverlay === true);

/**
 * Turrets in front of a real camera, for hover: each a bone under a mount with a 4-unit box mesh, at the
 * position given. The camera stands 30 units out on +Z looking at the origin; the canvas is 100 px square.
 */
function hoverRig(turrets: { name: string; at: [number, number, number] }[]): {
    rig: TurretRig;
    mounts: Map<string, THREE.Object3D>;
    handles: TurretHandle[];
    camera: THREE.PerspectiveCamera;
    canvas: { style: { cursor: string } };
} {
    const root = new THREE.Group();
    const bones = new Map<string, THREE.Object3D>();
    const mounts = new Map<string, THREE.Object3D>();
    for (const turret of turrets) {
        const mount = new THREE.Object3D();
        mount.position.set(...turret.at);
        const bone = new THREE.Object3D();
        bone.name = turret.name;
        bone.add(
            new THREE.Mesh(
                new THREE.BoxGeometry(4, 4, 4),
                new THREE.MeshBasicMaterial(),
            ),
        );
        mount.add(bone);
        root.add(mount);
        bones.set(turret.name.toLowerCase(), bone);
        mounts.set(turret.name, mount);
    }
    root.updateMatrixWorld(true);
    const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 1000);
    camera.position.set(0, 0, 30);
    camera.lookAt(0, 0, 0);
    camera.updateMatrixWorld(true);
    const canvas = {
        getBoundingClientRect: () => ({
            left: 0,
            top: 0,
            width: 100,
            height: 100,
        }),
        setPointerCapture: () => undefined,
        style: { cursor: "" },
    };
    const context = standInContext({
        camera,
        canvas,
        modelRoot: root,
        parts: new Map([
            [
                "p",
                {
                    id: "p",
                    root,
                    bones,
                    bonesByIndex: new Map(),
                    subjectId: "subject",
                },
            ],
        ]),
        cameraRig: { modelSpan: () => 10 },
        levels: { drawn: () => true },
        modelScale: 1,
    });
    const rig = new TurretRig(context, { onTurretAimed: null });
    const handles = turrets.map((turret) => ({
        ...handle,
        id: `weapon:${turret.name}`,
        turretBone: turret.name,
    }));
    return { rig, mounts, handles, camera, canvas };
}

/** A pointer at canvas pixel (x, y). */
const pointerAt = (x: number, y: number): PointerEvent =>
    ({
        clientX: x,
        clientY: y,
        pointerId: 1,
        preventDefault: () => undefined,
        stopImmediatePropagation: () => undefined,
    }) as unknown as PointerEvent;

/** Where a handle's yaw knob is drawn on the 100 px canvas. */
function knobPixel(
    mount: THREE.Object3D,
    camera: THREE.PerspectiveCamera,
): { x: number; y: number } {
    mount.updateMatrixWorld(true);
    let knob: THREE.Object3D | undefined;
    anchorsUnder(mount)[0].traverse((node) => {
        if (node instanceof THREE.Mesh && knob === undefined) {
            knob = node;
        }
    });
    const at = knob!.getWorldPosition(new THREE.Vector3()).project(camera);
    return { x: (at.x * 0.5 + 0.5) * 100, y: (-at.y * 0.5 + 0.5) * 100 };
}

describe("TurretRig", () => {
    it("aims a turret at rest until it is turned by hand", () => {
        const rig = rigWith(new Map());

        assert.deepEqual(rig.turretAim("weapon:turret"), TURRET_AT_REST);
    });

    it("turns a turret bone to the rest angle the XML declares", () => {
        const { part, turret } = turretPart();
        const rig = rigWith(new Map([["p", part]]));

        rig.setTurretRestAngles([
            {
                partId: "p",
                turretBone: "Turret",
                barrelBone: null,
                rest: { yaw: 90, pitch: 0 },
            },
        ]);

        // A quarter turn, whichever way round the traverse axis is signed.
        const angle = 2 * Math.acos(Math.min(1, Math.abs(turret.quaternion.w)));
        assert.ok(Math.abs(angle - Math.PI / 2) < 1e-6, `turned by ${angle}`);
    });

    it("hangs a yaw and a pitch track beside a handled turret and takes them off with the handle", () => {
        const { part, mount } = turretPart();
        const rig = rigWith(new Map([["p", part]]));

        rig.setTurretHandles([handle]);
        assert.equal(overlaysUnder(mount), 2);

        rig.setTurretHandles([]);
        assert.equal(overlaysUnder(mount), 0);
    });

    it("builds no handle for a turret bone the part does not have", () => {
        const { part, mount } = turretPart();
        const rig = rigWith(new Map([["p", part]]));

        rig.setTurretHandles([{ ...handle, turretBone: "Missing" }]);
        assert.equal(overlaysUnder(mount), 0);
    });

    it("says whether the arcs are shown", () => {
        const rig = rigWith(new Map());

        rig.setFireArcsVisible(true);
        assert.equal(rig.arcsShown, true);
        rig.setFireArcsVisible(false);
        assert.equal(rig.arcsShown, false);
    });

    it("hides the arcs from a shot without annotations, and puts them back", () => {
        const rig = rigWith(new Map());
        rig.setFireArcsVisible(true);

        const restore = rig.stageForCapture({
            width: 64,
            height: 64,
            background: null,
            grid: false,
            floor: true,
            particles: false,
            annotations: false,
        });
        assert.equal(rig.arcsShown, false);

        restore();
        assert.equal(rig.arcsShown, true);
    });

    it("turns a turret back to its rest angle after a clip put the file's pose back", () => {
        const { part, turret } = turretPart();
        const rig = rigWith(new Map([["p", part]]));
        rig.setTurretRestAngles([
            {
                partId: "p",
                turretBone: "Turret",
                barrelBone: null,
                rest: { yaw: 90, pitch: 0 },
            },
        ]);

        // What `AnimationRig.resetPose` does to every bone it snapshotted at load.
        turret.quaternion.identity();
        rig.reposeTurrets();

        const angle = 2 * Math.acos(Math.min(1, Math.abs(turret.quaternion.w)));
        assert.ok(Math.abs(angle - Math.PI / 2) < 1e-6, `turned by ${angle}`);
    });

    it("shows a turret's handles while the pointer is over its meshes, and hides them when it leaves", () => {
        const { rig, mounts, handles } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles);
        const anchors = anchorsUnder(mounts.get("Turret")!);
        assert.equal(anchors.length, 2);
        assert.deepEqual(
            anchors.map((a) => a.visible),
            [false, false],
        );

        rig.onPointerMove(pointerAt(50, 50));
        assert.deepEqual(
            anchors.map((a) => a.visible),
            [true, true],
        );

        rig.onPointerMove(pointerAt(2, 2));
        assert.deepEqual(
            anchors.map((a) => a.visible),
            [false, false],
        );
    });

    it("keeps a shown turret's handles drawn wherever the pointer is", () => {
        const { rig, mounts, handles } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles, new Set([handles[0].id]));
        const anchors = anchorsUnder(mounts.get("Turret")!);

        assert.deepEqual(
            anchors.map((a) => a.visible),
            [true, true],
        );
        rig.onPointerMove(pointerAt(2, 2));
        assert.deepEqual(
            anchors.map((a) => a.visible),
            [true, true],
        );
    });

    it("gives two turrets under the pointer to the one nearest the camera", () => {
        const { rig, mounts, handles } = hoverRig([
            { name: "Far", at: [0, 0, 0] },
            { name: "Near", at: [0, 0, 8] },
        ]);
        rig.setTurretHandles(handles);

        rig.onPointerMove(pointerAt(50, 50));

        assert.equal(anchorsUnder(mounts.get("Near")!)[0].visible, true);
        assert.equal(anchorsUnder(mounts.get("Far")!)[0].visible, false);
    });

    it("keeps a hovered turret's handles while the pointer moves from the turret onto them", () => {
        const { rig, mounts, handles, camera } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles);
        rig.onPointerMove(pointerAt(50, 50));
        const knob = knobPixel(mounts.get("Turret")!, camera);
        assert.ok(Math.abs(knob.x - 50) > 10, `knob at ${knob.x}`);

        rig.onPointerMove(pointerAt(knob.x, knob.y));

        assert.equal(anchorsUnder(mounts.get("Turret")!)[0].visible, true);
    });

    it("lets no knob be grabbed while its handles are not drawn", () => {
        const { rig, mounts, handles, camera } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles);
        rig.onPointerMove(pointerAt(50, 50));
        const knob = knobPixel(mounts.get("Turret")!, camera);
        rig.onPointerMove(pointerAt(2, 2));

        assert.equal(rig.grabKnob(pointerAt(knob.x, knob.y)), false);
    });

    it("hides the handles from a shot without annotations, and puts them back", () => {
        const { rig, mounts, handles } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles, new Set([handles[0].id]));
        const anchors = anchorsUnder(mounts.get("Turret")!);

        const restore = rig.stageForCapture({
            width: 64,
            height: 64,
            background: null,
            grid: false,
            floor: false,
            particles: false,
            annotations: false,
        });
        assert.deepEqual(
            anchors.map((a) => a.visible),
            [false, false],
        );

        restore();
        assert.deepEqual(
            anchors.map((a) => a.visible),
            [true, true],
        );
    });

    it("forgets the hovered turret when the pointer leaves the canvas", () => {
        const { rig, mounts, handles } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles);
        rig.onPointerMove(pointerAt(50, 50));

        rig.onPointerLeave();

        assert.equal(anchorsUnder(mounts.get("Turret")!)[0].visible, false);
    });

    it("finds a skinned turret by the hull vertices its bone carries", () => {
        // Four of five turreted land units measured carry no mesh under the turret bone: the turret is
        // the part of a skinned hull weighted to it. Here a hull of two boxes - one weighted to the root
        // bone on the left of the screen, one to the turret bone on the right.
        const root = new THREE.Group();
        const hullBone = new THREE.Bone();
        const turretBone = new THREE.Bone();
        turretBone.name = "Turret";
        turretBone.position.set(6, 0, 0);
        hullBone.add(turretBone);
        root.add(hullBone);
        const left = new THREE.BoxGeometry(4, 4, 4).translate(-6, 0, 0);
        const right = new THREE.BoxGeometry(4, 4, 4).translate(6, 0, 0);
        const positions = [
            ...left.attributes.position.array,
            ...right.attributes.position.array,
        ];
        const count = positions.length / 3;
        const half = left.attributes.position.count;
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute(
            "position",
            new THREE.Float32BufferAttribute(positions, 3),
        );
        geometry.setAttribute(
            "skinIndex",
            new THREE.Uint16BufferAttribute(
                Array.from({ length: count }, (_, i) => [
                    i < half ? 0 : 1,
                    0,
                    0,
                    0,
                ]).flat(),
                4,
            ),
        );
        geometry.setAttribute(
            "skinWeight",
            new THREE.Float32BufferAttribute(
                Array.from({ length: count }, () => [1, 0, 0, 0]).flat(),
                4,
            ),
        );
        const hull = new THREE.SkinnedMesh(
            geometry,
            new THREE.MeshBasicMaterial(),
        );
        root.add(hull);
        root.updateMatrixWorld(true);
        hull.bind(new THREE.Skeleton([hullBone, turretBone]));

        const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 1000);
        camera.position.set(0, 0, 30);
        camera.lookAt(0, 0, 0);
        camera.updateMatrixWorld(true);
        const context = standInContext({
            camera,
            canvas: {
                getBoundingClientRect: () => ({
                    left: 0,
                    top: 0,
                    width: 100,
                    height: 100,
                }),
                setPointerCapture: () => undefined,
                style: {},
            },
            modelRoot: root,
            parts: new Map([
                [
                    "p",
                    {
                        id: "p",
                        root,
                        bones: new Map<string, THREE.Object3D>([
                            ["turret", turretBone],
                        ]),
                        bonesByIndex: new Map(),
                        subjectId: "subject",
                    },
                ],
            ]),
            cameraRig: { modelSpan: () => 10 },
            levels: { drawn: () => true },
            modelScale: 1,
        });
        const rig = new TurretRig(context, { onTurretAimed: null });
        rig.setTurretHandles([handle]);
        const anchors = anchorsUnder(hullBone);
        const turretOnScreen = new THREE.Vector3(6, 0, 0).project(camera);
        const x = (turretOnScreen.x * 0.5 + 0.5) * 100;

        rig.onPointerMove(pointerAt(x, 50));
        assert.equal(anchors[0].visible, true, `at ${x}`);

        rig.onPointerMove(pointerAt(100 - x, 50));
        assert.equal(anchors[0].visible, false, "over the hull's own half");
    });

    it("takes a knob anywhere inside its drawn size when the camera is close", () => {
        const { rig, mounts, handles, camera } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles, new Set([handles[0].id]));
        // Up close to the yaw knob, so it is drawn far larger than the 14 px floor.
        let knob: THREE.Object3D | undefined;
        anchorsUnder(mounts.get("Turret")!)[0].traverse((node) => {
            if (node instanceof THREE.Mesh && knob === undefined) {
                knob = node;
            }
        });
        const at = knob!.getWorldPosition(new THREE.Vector3());
        camera.position.set(at.x, at.y, at.z + 1.5);
        camera.lookAt(at);
        camera.updateMatrixWorld(true);

        assert.equal(rig.grabKnob(pointerAt(50 + 25, 50)), true);
    });

    it("still takes a small knob from 14 px away", () => {
        const { rig, mounts, handles, camera } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles, new Set([handles[0].id]));
        const knob = knobPixel(mounts.get("Turret")!, camera);

        assert.equal(rig.grabKnob(pointerAt(knob.x + 13, knob.y)), true);
        rig.onPointerUp();
        assert.equal(rig.grabKnob(pointerAt(knob.x + 30, knob.y)), false);
    });

    it("shows a grab cursor over a knob, grabbing while it is dragged, and none elsewhere", () => {
        const { rig, mounts, handles, camera, canvas } = hoverRig([
            { name: "Turret", at: [0, 0, 0] },
        ]);
        rig.setTurretHandles(handles, new Set([handles[0].id]));
        const knob = knobPixel(mounts.get("Turret")!, camera);

        rig.onPointerMove(pointerAt(knob.x, knob.y));
        assert.equal(canvas.style.cursor, "grab");

        rig.grabKnob(pointerAt(knob.x, knob.y));
        assert.equal(canvas.style.cursor, "grabbing");

        rig.onPointerUp();
        assert.equal(canvas.style.cursor, "grab");

        rig.onPointerMove(pointerAt(2, 2));
        assert.equal(canvas.style.cursor, "");
    });

    it("puts every turned turret back where the model authored it, and says so for each", () => {
        const { rig, mounts, handles, camera } = hoverRig([
            { name: "Left", at: [-6, 0, 0] },
            { name: "Right", at: [6, 0, 0] },
        ]);
        const told: [string, { yaw: number; pitch: number }][] = [];
        rig.setTurretHandles(handles, new Set(handles.map((one) => one.id)));
        (
            rig as unknown as { host: { onTurretAimed: unknown } }
        ).host.onTurretAimed = (
            id: string,
            aim: { yaw: number; pitch: number },
        ) => told.push([id, aim]);
        for (const name of ["Left", "Right"]) {
            const knob = knobPixel(mounts.get(name)!, camera);
            rig.grabKnob(pointerAt(knob.x, knob.y));
            rig.onPointerMove(pointerAt(knob.x + 20, knob.y));
            rig.onPointerUp();
        }
        assert.notDeepEqual(rig.turretAim(handles[0].id), TURRET_AT_REST);
        assert.notDeepEqual(rig.turretAim(handles[1].id), TURRET_AT_REST);
        told.length = 0;

        rig.resetAllTurretAims();

        assert.deepEqual(rig.turretAim(handles[0].id), TURRET_AT_REST);
        assert.deepEqual(rig.turretAim(handles[1].id), TURRET_AT_REST);
        assert.deepEqual(
            told.map(([id]) => id).sort(),
            handles.map((one) => one.id).sort(),
        );
    });
});
