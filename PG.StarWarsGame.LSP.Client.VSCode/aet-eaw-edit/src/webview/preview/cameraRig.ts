// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's framing: the sphere the subject fills, the distance the camera is fitted at and the draw
// distance, the clip planes that follow it, a pose or a model camera applied, and the per-frame step that
// readies the scene for the camera. It reads the camera, the controls and the drawn meshes; nothing else.

import { lightDirection, type Vector3 as Vector3Like } from "./lighting";
import * as THREE from "three";
import {
    frameSphere,
    type BoundingSphere,
    clipPlanes,
    DEFAULT_AZIMUTH_DEGREES,
    DEFAULT_ELEVATION_DEGREES,
    FIELD_OF_VIEW,
} from "./framing";
import { alamoNodeName } from "./skeleton";
import { resolveBoneId } from "./boneIds";
import { drawnBounds, isOverlay } from "./modelBounds";
import { type MaterialExtras, resolveMaterial } from "./materials";
import { type PresetView } from "./viewportTypes";
import { type ViewportContext } from "./viewportContext";
import {
    billboardLocalRotation,
    billboardRotation,
    billboardTypeOf,
} from "./billboards";

const PRESETS: Record<PresetView, { azimuth: number; elevation: number }> = {
    front: { azimuth: 0, elevation: 0 },
    side: { azimuth: 90, elevation: 0 },
    top: { azimuth: 0, elevation: 89.9 },
    threeQuarter: {
        azimuth: DEFAULT_AZIMUTH_DEGREES,
        elevation: DEFAULT_ELEVATION_DEGREES,
    },
};

/** A mesh the exporter marked as a shadow volume, rather than part of the model's own shape. */
function isShadowVolume(mesh: THREE.Mesh): boolean {
    const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

    return resolveMaterial(extras).archetype === "shadow-volume";
}

/** What CameraRig reads from the viewport it belongs to. */
const BILLBOARD_AT = new THREE.Vector3();
const BILLBOARD_LIGHT = new THREE.Vector3();
const BILLBOARD_BONE = new THREE.Quaternion();

export class CameraRig {
    /** How far the camera stands off, kept so the depth range can be redone without reframing. */
    fitDistance = 1;
    /** The reader's own multiplier on the fitted far plane. */
    private drawDistance = 1;
    /** See {@link modelSpan}. Cleared with the scene. */
    private cachedSpan: number | null = null;
    /** Bone nodes whose geometry turns to face the camera, the light or the wind. */
    readonly billboards: THREE.Object3D[] = [];

    constructor(private readonly context: ViewportContext) {}

    /** Stops turning these billboards: their subtree is being disposed. */
    dropBillboards(nodes: ReadonlySet<THREE.Object3D>): void {
        const kept = this.billboards.filter((node) => !nodes.has(node));
        this.billboards.length = 0;
        this.billboards.push(...kept);
    }

    /** Forgets every billboard, for a new scene. */
    forgetBillboards(): void {
        this.billboards.length = 0;
    }

    /** Drops the remembered span, for when the model it measured has changed. */
    forgetSpan(): void {
        this.cachedSpan = null;
    }

    /** Where the camera stands now, for saving the current shot as a preset. */
    get cameraPosition(): Vector3Like {
        return {
            x: this.context.camera.position.x,
            y: this.context.camera.position.y,
            z: this.context.camera.position.z,
        };
    }

    /**
     * Renders one frame and counts the canvas pixels that differ from its dominant colour, for the UI
     * tests. With the heat Debug view on, the dominant colour is the neutral field and the rest is the
     * heat the camera sees - whether a hull in front of a haze hides it is a depth test on the GPU, so
     * the pixels are the only place to read it. Read in the same task as the render, so the drawing
     * buffer still holds the frame.
     */
    canvasCoverage(): { lit: number; total: number } {
        this.context.environment.renderFrame();
        const gl = this.context.renderer.getContext();
        const width = gl.drawingBufferWidth;
        const height = gl.drawingBufferHeight;
        const pixels = new Uint8Array(width * height * 4);
        gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
        const counts = new Map<number, number>();
        for (let i = 0; i < pixels.length; i += 4) {
            const key =
                (pixels[i] << 16) | (pixels[i + 1] << 8) | pixels[i + 2];
            counts.set(key, (counts.get(key) ?? 0) + 1);
        }
        let dominant = 0;
        let most = -1;
        for (const [key, count] of counts) {
            if (count > most) {
                most = count;
                dominant = key;
            }
        }
        const [r, g, b] = [
            (dominant >> 16) & 255,
            (dominant >> 8) & 255,
            dominant & 255,
        ];
        let lit = 0;
        for (let i = 0; i < pixels.length; i += 4) {
            const departure =
                Math.abs(pixels[i] - r) +
                Math.abs(pixels[i + 1] - g) +
                Math.abs(pixels[i + 2] - b);
            if (departure > 3) {
                lit++;
            }
        }
        return { lit, total: width * height };
    }

    /** The bounding sphere a preset is resolved against, so the caller sizes the shot correctly. */
    get subjectSphere(): { center: Vector3Like; radius: number } {
        return this.boundingSphere();
    }

    /** Frames whatever is currently loaded. */
    /**
     * Puts the camera where the model's own authored camera stands, looking where it looks.
     *
     * Placed from the camera's own BONE NODES rather than from the coordinates the server sent.
     * Both describe the same point, but the bone is already in the scene and has been through
     * exactly the transform the geometry went through - including the root's Z-up to Y-up rotation,
     * which is the one thing certain to be got wrong if it is applied a second time by hand. A shot
     * a quarter turn out looks like a broken camera rather than a broken conversion.
     *
     * @returns false when the model does not carry that bone pair, so the caller can say so.
     */
    applyModelCamera(name: string): boolean {
        const eye = this.boneNodeFor(name);
        const at = this.boneNodeFor(`${name}.Target`);

        if (eye === undefined || at === undefined) {
            return false;
        }

        eye.updateWorldMatrix(true, false);
        at.updateWorldMatrix(true, false);

        this.applyCameraPose(
            new THREE.Vector3().setFromMatrixPosition(eye.matrixWorld),
            new THREE.Vector3().setFromMatrixPosition(at.matrixWorld),
        );

        return true;
    }

    /**
     * Points the camera at a place, from a place.
     *
     * The one path every framing goes through - the presets, the model's own camera and the
     * reader's saved shots - so the clip planes are refitted the same way for all of them. Without
     * that a camera parked 653 units out, which `Eb_icc`'s own camera is, lands outside the range
     * the previous framing chose and the model disappears.
     */
    applyCameraPose(position: Vector3Like, target: Vector3Like): void {
        this.fitDistance = Math.hypot(
            position.x - target.x,
            position.y - target.y,
            position.z - target.z,
        );
        this.applyClipPlanes(this.boundingSphere(true), this.fitDistance);

        this.context.camera.position.set(position.x, position.y, position.z);
        this.context.controls.target.set(target.x, target.y, target.z);
        this.context.controls.update();
    }

    frameAll(preset: PresetView = "threeQuarter"): void {
        const sphere = this.boundingSphere();
        const { azimuth, elevation } = PRESETS[preset];
        const aspect = this.context.camera.aspect;

        const pose = frameSphere(
            sphere,
            FIELD_OF_VIEW,
            aspect,
            azimuth,
            elevation,
        );
        const distance = Math.hypot(
            pose.position.x - sphere.center.x,
            pose.position.y - sphere.center.y,
            pose.position.z - sphere.center.z,
        );

        this.fitDistance = distance;
        this.applyClipPlanes(this.boundingSphere(true), distance);

        this.context.camera.position.set(
            pose.position.x,
            pose.position.y,
            pose.position.z,
        );
        this.context.controls.target.set(
            pose.target.x,
            pose.target.y,
            pose.target.z,
        );
        this.context.controls.update();

        // The grid is a sense of scale, so it has to grow with the subject: a fixed 100-unit grid is
        // invisible under a capital ship and swallows a fighter.
        this.context.environment.rebuildGrid(sphere);
    }

    /**
     * The reader's draw-distance multiplier.
     *
     * Applied on the next reframe as well as at once, so a change is visible without having to
     * touch the camera.
     */
    setDrawDistance(multiplier: number): void {
        this.drawDistance = multiplier;
        this.applyClipPlanes(this.boundingSphere(), this.fitDistance);
    }

    /**
     * The scene node for any way of naming a bone.
     *
     * The one place a name becomes a node. It used to be three different places with three
     * different rules - a lowercased map for cameras, a canonical-id map for the animation tracks,
     * a stripped-name lookup for the row chain - and a key that suited one of them matched nothing
     * in the others. `resolveBoneId` knows the fallbacks; nothing else needs to.
     */
    private boneNodeFor(key: string): THREE.Object3D | undefined {
        const hull = this.context.assembly.hull();

        if (hull === null) {
            return undefined;
        }

        const byId = new Map(
            [...hull.bonesByIndex.values()].map(
                (node) => [alamoNodeName(node), node] as const,
            ),
        );
        const id = resolveBoneId(byId.keys(), key);

        return id === undefined ? undefined : byId.get(id);
    }

    /**
     * A sphere around the subject.
     *
     * `includeEffects` separates two questions that were being answered with one number. Where the
     * CAMERA goes is about the model: a firing arc is a weapon's range and a smoke plume is where
     * the smoke has drifted to, and neither should push the hull into the distance. What the FAR
     * PLANE is set to is about reach: cull at the hull and a long trail is chopped off in mid-air,
     * which is what put this in in the first place.
     */
    boundingSphere(includeEffects = false): BoundingSphere {
        // How far the particles will get. Their geometry is allocated full of zeros and filled in over
        // the following seconds, so a box around it measures a point at the origin however large the
        // effect is about to be - and a zero radius puts the camera on its own target, which draws
        // nothing at all, grid included.
        const particleReach = !includeEffects
            ? 0
            : Math.max(
                  0,
                  ...[...this.context.particles.particleSystems.values()].map(
                      (entry) => entry.instance.extent(),
                  ),
              );

        // Only what is on screen, and only geometry. See `modelBounds.ts` - the firing-arc cones
        // under this same root are thousands of units across and are not the model.
        //
        // The SHADOW volume is excluded even when it is on screen. It is an extruded silhouette,
        // routinely larger than the hull it belongs to and offset from it, so a model that draws
        // one framed itself around a shape nobody was looking at - which is what made the presets
        // look like they were choosing at random. Someone who ticks it on wants to SEE it, not to
        // have the camera measure the subject by it.
        const box = drawnBounds(
            this.context.modelRoot,
            (mesh) => this.context.levels.drawn(mesh) && !isShadowVolume(mesh),
            // The arcs and handles are drawn and are meshes, so `drawn` alone let them in whenever
            // they were on screen: measured on the Gargantuan with its arcs showing, the framing
            // radius was 508 against a hull of 99, and every camera preset stood five times too far
            // off. The defect this function exists for, still open whenever the arcs were visible.
            isOverlay,
        );
        const sphere = box.isEmpty()
            ? { center: { x: 0, y: 0, z: 0 }, radius: 0 }
            : (() => {
                  const s = box.getBoundingSphere(new THREE.Sphere());
                  return {
                      center: { x: s.center.x, y: s.center.y, z: s.center.z },
                      radius: s.radius,
                  };
              })();

        // Guards NaN as well as zero: `>` is false for NaN, so the fallback wins.
        const radius = Math.max(particleReach, sphere.radius);
        return { center: sphere.center, radius: radius > 0 ? radius : 1 };
    }

    /**
     * How big the subject is, across its diagonal, cached until the scene changes.
     *
     * `Box3.setFromObject` walks every vertex, so anything asking per frame - the bone axes did -
     * turns a size lookup into a full geometry traversal. The hull does not change size while it is
     * loaded; an animation moves it. The one thing that does change it is `setModelScale`, which
     * drops the cache, because the span is measured in WORLD units.
     */
    modelSpan(): number {
        if (this.cachedSpan !== null) {
            return this.cachedSpan;
        }

        const hull = this.context.assembly.hull();
        if (hull === null) {
            return 100;
        }

        // Every mesh the hull carries, drawn or not, as before - but NOT the overlays hung inside it.
        // `setFromObject` measured those too, and on the Gargantuan with its arcs on the span came out
        // at 1015 against a hull of 204. The span sizes the turret handles and caps how long an arc
        // is drawn, so that fed back: handles sized against the arcs, arcs capped against themselves.
        const bounds = drawnBounds(hull.root, () => true, isOverlay);
        this.cachedSpan = bounds.isEmpty()
            ? 100
            : bounds.getSize(new THREE.Vector3()).length();

        return this.cachedSpan;
    }

    /**
     * Everything that has to be recomputed for the camera AS IT STANDS, before anything is drawn.
     *
     * Its own method because there are two callers and they must not drift: the animation loop, and
     * `capture`, which moves the camera and changes the aspect before rendering a single frame.
     * Capture used to call `renderFrame` on its own, and the result did not represent the scene at
     * all - the translated materials still held the PREVIOUS frame's projection, so every mesh
     * drawn by one landed in the wrong place or off the edge while the floor and the grid, which
     * are ordinary three materials, drew correctly against the new one. Meshes ghosting off to the
     * side with the back and the engines missing is exactly what a stale WORLDVIEWPROJECTION looks
     * like.
     *
     * Time is NOT advanced here. Nothing in this method simulates anything - it only answers "where
     * is the camera now", which is the one thing a capture changes.
     */
    prepareForCamera(): void {
        this.context.camera.updateMatrixWorld();

        // The engine's per-frame contract, including the matrices. Last, not first, so it sees the
        // bone transforms the animation and skeleton work above have already settled.
        this.context.materials.updateAlamoUniforms();
        this.updateBillboards();

        // The sky dome travels with the camera and keeps its own orientation, so the horizon stays
        // level and infinitely far however the camera is turned.
        if (
            this.context.environment.sky !== null &&
            this.context.environment.sky.visible
        ) {
            this.context.environment.sky.position.copy(
                this.context.camera.position,
            );
            this.context.environment.sky.scale.setScalar(
                Math.max(
                    this.context.camera.near * 4,
                    this.context.camera.far * 0.4,
                ),
            );
        }

        // The stars need the same treatment, and for a reason that took a regression to notice:
        // parenting them to the CAMERA exempts them from frustum CULLING but not from the far
        // plane, which clips their vertices like anything else's. The far plane is fitted to the
        // model, so the moment the hardpoint rotation was corrected and the bounds shrank, the
        // whole sky fell outside it and the stars simply went out.
        if (
            this.context.environment.stars !== null &&
            this.context.environment.stars.visible
        ) {
            this.context.environment.stars.scale.setScalar(
                Math.max(
                    this.context.camera.near * 4,
                    this.context.camera.far * 0.4,
                ),
            );
        }
    }

    /**
     * Sets the depth range from the model, its effects and the reader's own draw distance.
     *
     * The camera is framed on the MODEL - fitting it to the effects would push a trooper into the
     * distance to make room for his flamethrower - but the far PLANE has to clear both, or the
     * flame is culled halfway along and reads as an effect that stops.
     */
    applyClipPlanes(sphere: BoundingSphere, distance: number): void {
        const planes = clipPlanes(
            sphere,
            distance,
            this.context.particles.effectReach(),
            this.drawDistance,
        );

        this.context.camera.near = planes.near;
        this.context.camera.far = planes.far;
        this.context.camera.updateProjectionMatrix();
    }

    /**
     * Turns each billboarded bone's geometry to face whatever it is meant to face.
     *
     * On the MESH rather than on the bone, which is what the engine does: `DoBillboard` composes the
     * rotation onto the sub-mesh's world matrix as it draws, so the bone still supplies the position
     * and its child bones are left exactly where the file put them. Rotating the bone itself would
     * swing a whole limb round with the card.
     */
    private updateBillboards(): void {
        if (this.billboards.length === 0) {
            return;
        }

        // The direction the light TRAVELS, which is the reverse of where it stands.
        const light = BILLBOARD_LIGHT.copy(
            lightDirection(
                this.context.environment.lightAzimuth,
                this.context.environment.lightElevation,
            ),
        ).negate();

        for (const node of this.billboards) {
            const type = billboardTypeOf(node);
            if (type === null) {
                continue;
            }

            node.updateWorldMatrix(true, false);
            node.getWorldPosition(BILLBOARD_AT);
            node.getWorldQuaternion(BILLBOARD_BONE);

            const world = billboardRotation(
                type,
                this.context.camera,
                light,
                BILLBOARD_AT,
            );

            for (const child of node.children) {
                if (child instanceof THREE.Mesh) {
                    // The billboard turns what the node ALREADY had, the way
                    // `ObjectTemplate::DoBillboard` does - it must not replace the world rotation,
                    // because that throws away the exporter's Z-up-to-Y-up root correction and lays
                    // every tree's shadow card flat. See `billboardLocalRotation`.
                    billboardLocalRotation(
                        BILLBOARD_BONE,
                        world,
                        child.quaternion,
                    );
                }
            }
        }
    }
}
