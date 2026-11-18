// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's turrets: the firing arcs drawn from each weapon's fire bones, the drag handles on the turrets
// whose arcs are shown, the sweep through each declared traverse, the rest angle the XML gives, and the aim the
// reader has set by hand. It poses turret bones and reads the parts and the model scale; nothing else.

import { type PreviewTurret } from "../../protocol/modelPreview";
import {
    type TurretHandle,
    type TurretAim,
    type TurretAxis,
    TURRET_AT_REST,
    dragToAim,
    type TurretAxisRange,
} from "./turretHandles";
import * as THREE from "three";
import {
    drawnConeRange,
    hullFrameBasis,
    fireConeOutline,
    fireConeSurface,
} from "./fireArc";
import {
    trackRadius,
    trackSegments,
    stopTicks,
    knobPoint,
} from "./turretTrack";
import { sweepAngles } from "./deathClone";
import { type ViewportContext } from "./viewportContext";

/**
 * The firing-arc gizmo's look.
 *
 * Unlit and never writing depth, because it is an annotation drawn over a model rather than a thing
 * in the scene.
 */
/**
 * The fill of a firing cone.
 *
 * Translucent and double-sided, with no depth WRITE: fifty overlapping cones on a capital ship must
 * not occlude each other or the hull they describe, and the reader is usually looking at the model
 * through them. Additive was tried in spirit and rejected - fifty stacked cones saturate to white
 * and the hull disappears inside its own arcs.
 */
const FIRE_ARC_MATERIAL = new THREE.MeshBasicMaterial({
    color: 0xff9d3c,
    transparent: true,
    // Faint on purpose. The outline is what carries the shape now, so the fill only has to suggest
    // a body - and six overlapping weapons at 0.10 still stacked up into a solid orange field.
    opacity: 0.05,
    depthWrite: false,
    side: THREE.DoubleSide,
});

/** How many segments go round a cone's rim. Enough to read as a cone, cheap enough for fifty. */
const FIRE_ARC_SEGMENTS = 24;

/** Rings from the axis to the rim. Enough to hold a wide cap's curve without being a mesh.  */
const FIRE_ARC_RINGS = 5;

/** Ribs from the apex to the rim. Every rim point would be a solid disc of lines. */
const FIRE_ARC_RIBS = 4;

/**
 * The cone's EDGES, drawn over the fill.
 *
 * The same hue at full strength. The fill alone gives a cone no boundary - at 10% opacity a wide
 * arc is a soft haze, and fifty overlapping ones are one field - so the outline is what says where
 * each arc actually stops. Depth-tested like the fill, so an edge behind the hull stays behind it.
 */
const FIRE_ARC_OUTLINE_MATERIAL = new THREE.LineBasicMaterial({
    color: 0xff9d3c,
    transparent: true,
    opacity: 0.55,
    depthWrite: false,
});

/**
 * The BARREL's envelope, where a turret hardpoint keeps one the shot does not share.
 *
 * The same hue at a lower weight, not a second colour. Both envelopes belong to one weapon, and
 * giving the second its own hue would read as a second gun rather than as a second limit on the
 * first. It sits INSIDE the firing envelope in pitch and exactly on it in yaw, so what the reader
 * sees is the fire arc with a narrower band drawn through it - which is the relationship.
 */
const ROTATION_ARC_MATERIAL = new THREE.MeshBasicMaterial({
    color: 0xff9d3c,
    transparent: true,
    opacity: 0.03,
    depthWrite: false,
    side: THREE.DoubleSide,
});

/** Dashed would be better still; at these lengths a lighter line reads more cleanly than a dash. */
const ROTATION_ARC_OUTLINE_MATERIAL = new THREE.LineBasicMaterial({
    color: 0xffc98a,
    transparent: true,
    opacity: 0.3,
    depthWrite: false,
});

/**
 * The turret handle's track, and the knob that rides it.
 *
 * Cool against the arcs' warm orange, because they are answers to different questions sitting in
 * the same space: the arc is where the weapon may SHOOT and the track is where the bone may TURN.
 * Drawn over everything - a manipulator you cannot see behind the hull is one you cannot grab - and
 * with no depth write, so it never occludes the model it is attached to.
 */
const TURRET_TRACK_MATERIAL = new THREE.LineBasicMaterial({
    color: 0x5ec8e0,
    transparent: true,
    opacity: 0.5,
    depthTest: false,
    depthWrite: false,
});

/** The track under the pointer, brightened. Hover highlights the axis; it does not gate it. */
const TURRET_TRACK_HOVER_MATERIAL = new THREE.LineBasicMaterial({
    color: 0x9fe8ff,
    transparent: true,
    opacity: 1,
    depthTest: false,
    depthWrite: false,
});

/** The end stops, at full strength: the whole point of a stop is that it reads as a limit. */
const TURRET_STOP_MATERIAL = new THREE.LineBasicMaterial({
    color: 0x5ec8e0,
    transparent: true,
    opacity: 0.95,
    depthTest: false,
    depthWrite: false,
});

const TURRET_KNOB_MATERIAL = new THREE.MeshBasicMaterial({
    color: 0x5ec8e0,
    transparent: true,
    opacity: 0.9,
    depthTest: false,
    depthWrite: false,
});

const TURRET_KNOB_HOVER_MATERIAL = new THREE.MeshBasicMaterial({
    color: 0xffffff,
    depthTest: false,
    depthWrite: false,
});

/** Enough to read a 45-degree arc as a curve; a full ring gets the same step. */
const TURRET_TRACK_SEGMENTS = 48;

/** The knob, and the stop ticks, as a fraction of the track radius. */
const TURRET_KNOB_FRACTION = 0.08;

const TURRET_STOP_TICK_FRACTION = 0.18;

/** How far a drag turns the turret. A degree every two pixels is a full sweep across a wide panel. */
const TURRET_DEGREES_PER_PIXEL = 0.5;

/** How near the pointer has to be to a knob to take it. The joints' own threshold. */
const TURRET_KNOB_PICK_RADIUS = 14;

/** Over the arcs, which are already over the hull. A manipulator is the frontmost thing there is. */
const TURRET_HANDLE_RENDER_ORDER = 3;

/** The two axes, in the order a reader meets them: traverse first, then elevation. */
const TURRET_AXES: readonly TurretAxis[] = ["yaw", "pitch"];

/** One turret bone across the lists that name it: by its part and its name, case folded. */
function restKey(partId: string, bone: string): string {
    return `${partId}/${bone.toLowerCase()}`;
}

/** One axis of one handle: its track, its stops, its knob, and the range it was built for. */
interface TurretHandleParts {
    /**
     * The frame the axis is MEASURED against, which the track hangs on - never the bone that moves.
     *
     * A sibling of `bone`, re-placed every frame from the bone's position and REST orientation. The
     * track used to hang on the moving bone itself, so a turret yawed 40 degrees carried its own stop
     * ticks round by 40 - an indicator of how far it may go that moved every time it went anywhere -
     * and the knob, placed at the aim inside that already-turned track, showed 80. See
     * `placeTurretAnchors`.
     */
    anchor: THREE.Object3D;
    /** The bone this axis turns: the turret for yaw, the barrel - or the turret again - for pitch. */
    bone: THREE.Object3D;
    /**
     * A pitch track on a bone that ALSO carries the yaw, which has to swing round by that yaw
     * explicitly. Decided once, when the handle is built. See `placeTurretAnchor`.
     */
    followsYaw: boolean;
    track: THREE.LineSegments;
    stops: THREE.LineSegments;
    knob: THREE.Mesh;
    radius: number;
    range: TurretAxisRange;
}

/** A flat `[x, y, z, ...]` buffer as a geometry, which is all any of these lines need. */
function positionGeometry(positions: readonly number[]): THREE.BufferGeometry {
    const geometry = new THREE.BufferGeometry();

    geometry.setAttribute(
        "position",
        new THREE.Float32BufferAttribute(Float32Array.from(positions), 3),
    );

    return geometry;
}

/** Takes one handle off the model and frees what it owns. The materials are shared and are not. */
function disposeTurretHandle(parts: TurretHandleParts): void {
    // The ANCHOR is what hangs on the model; the track goes with it.
    parts.anchor.removeFromParent();
    parts.track.geometry.dispose();
    parts.stops.geometry.dispose();
    parts.knob.geometry.dispose();
}

/**
 * A cone from a fire bone's origin out to its range: a faint fill inside a drawn outline.
 *
 * Both, because each alone fails at one end of the corpus. A filled volume on its own drowns the
 * ship - a Star Destroyer's arcs are 160 degrees wide and reach 2000 units on a hull about 600
 * long, so at any opacity worth seeing they paint over the model they describe. An outline on its
 * own says where the guns bear and how far, but gives the eye no body to read the direction from.
 * Kept faint enough to look THROUGH, with the edges carrying the shape.
 */
function coneGeometry(arc: {
    widthDegrees: number;
    heightDegrees: number;
    range: number;
}): THREE.BufferGeometry {
    // A cone with a rounded bottom - see `fireConeSurface`. The apex-to-rim fan it replaced drew a
    // Pringle at the wide arcs the shipped hardpoints declare.
    const geometry = new THREE.BufferGeometry();

    geometry.setAttribute(
        "position",
        new THREE.Float32BufferAttribute(
            fireConeSurface(
                arc.widthDegrees,
                arc.heightDegrees,
                arc.range,
                FIRE_ARC_SEGMENTS,
                FIRE_ARC_RINGS,
            ),
            3,
        ),
    );

    return geometry;
}

/** The edges of the same cone, as line segments in the same space. */
function coneOutlineGeometry(arc: {
    widthDegrees: number;
    heightDegrees: number;
    range: number;
}): THREE.BufferGeometry {
    const geometry = new THREE.BufferGeometry();

    geometry.setAttribute(
        "position",
        new THREE.Float32BufferAttribute(
            fireConeOutline(
                arc.widthDegrees,
                arc.heightDegrees,
                arc.range,
                FIRE_ARC_SEGMENTS,
                FIRE_ARC_RIBS,
            ),
            3,
        ),
    );

    return geometry;
}

/**
 * The axes a turret turns about, in the BONE's own space.
 *
 * Alamo is Z-up and a fire bone aims along local **X** - the one directional fact about these models
 * that has been verified, and the reason `AlamoFireBone.AimDirection` exists. So with X forward and
 * Z up, the remaining axis, Y, is the turret's side.
 *
 *   traverse (yaw)   - about Z, the up axis
 *   elevation (pitch) - about Y, the side axis
 *
 * The elevation used to be applied about **X**, which is the barrel's own length: the barrel ROLLED
 * instead of rising. Reported as "the barrel rotates amongst its horizontal axis not up and down".
 */
const TRAVERSE_AXIS = new THREE.Vector3(0, 0, 1);

const ELEVATION_AXIS = new THREE.Vector3(0, 1, 0);

/** What TurretRig still asks of the viewport itself: what only the viewport has. */
export interface TurretHost {
    readonly modelScale: number;
    readonly onTurretAimed: ((id: string, aim: TurretAim) => void) | null;
}

export class TurretRig {
    /** The turrets currently sweeping, and how far through the swing they are. */
    private turretSweeps: {
        partId: string;
        turretBone: string;
        barrelBone: string | null;
        turret: PreviewTurret;
    }[] = [];
    private sweepPhase = 0;
    /**
     * The turrets the reader can drag, and where each one is currently pointed.
     *
     * The aim is held HERE rather than in the shell because it is the pose of a bone: a re-render
     * that cleared it would snap every turret back to rest mid-drag. The shell is told about it
     * through {@link onTurretAimed} so the row can read it out, which is a copy for display and
     * never the source.
     */
    private turretHandles: TurretHandle[] = [];
    private readonly turretAims = new Map<string, TurretAim>();
    /**
     * The pose each turret rests in, by part and turret bone, as an aim off the authored pose.
     * A handle's aim is measured from it, as the sweep is, the way the game measures a turret's
     * swing from its rest vector.
     */
    private readonly turretRests = new Map<string, TurretAim>();
    /** The knob under the pointer, and the one being dragged. At most one of each. */
    private hoveredTurretAxis: { id: string; axis: TurretAxis } | null = null;
    private turretDrag: {
        id: string;
        axis: TurretAxis;
        startAim: TurretAim;
        startX: number;
        startY: number;
    } | null = null;
    /** The drawn handles, so they can be rebuilt or repositioned without a scene walk. */
    private readonly turretHandleNodes = new Map<
        string,
        {
            yaw: TurretHandleParts;
            pitch: TurretHandleParts;
        }
    >();
    /** Firing-arc gizmos, attached to their fire bones so they follow the turret. */
    private readonly arcs: THREE.Mesh[] = [];
    /**
     * The HULL-frame arcs and the muzzle each one belongs to, re-placed every frame.
     *
     * A bone-frame arc is a child of its bone and follows it for free. A hull-frame one cannot be:
     * it takes its orientation from the hull's facing, so it lives on the model root - and a copy of
     * the muzzle's position taken when the arc was built goes stale the moment a clip moves the bone.
     * The X-Wing's deploy carries its MuzzleA bones 3.4 units, and the cones stayed behind. The
     * engine reads the muzzle fresh on every check, so this does too.
     */
    private readonly hullArcs: { mesh: THREE.Mesh; muzzle: THREE.Object3D }[] =
        [];
    /** Scratch for {@link followHullArcs}, which runs every frame and should not allocate. */
    private readonly muzzleScratch = new THREE.Vector3();
    private arcsVisible = false;
    /** Scratch for {@link placeTurretAnchor}, which runs every frame and should not allocate. */
    private readonly twistScratch = new THREE.Quaternion();
    /**
     * The pose the MODEL authored for each bone this class swings, captured before it first moves.
     *
     * A sweep is an angle ON TOP of that, not an angle instead of it. The old code assigned
     * `bone.rotation.z` outright, which threw the authored orientation away and left the other two
     * Euler components composing against a value that no longer meant anything - the offset the user
     * reported. A turret bone is placed AND oriented by the exporter, so its rest rotation is almost
     * never identity.
     */
    private readonly sweepRest = new Map<THREE.Object3D, THREE.Quaternion>();
    /**
     * Brightens the axis under the pointer.
     *
     * Hover HIGHLIGHTS rather than decides. Which handles exist is the arc toggle's answer, because
     * a manipulator the pointer has to travel to cannot be gated on where the pointer is now. What
     * hover is genuinely for is telling twenty overlapping handles on a station apart.
     */
    onPointerMove = (event: PointerEvent): void => {
        if (this.turretDrag !== null) {
            this.dragTurret(event);
            return;
        }

        const rect = this.context.canvas.getBoundingClientRect();
        const found = this.turretKnobAt(
            event.clientX - rect.left,
            event.clientY - rect.top,
        );

        if (
            found?.id === this.hoveredTurretAxis?.id &&
            found?.axis === this.hoveredTurretAxis?.axis
        ) {
            return;
        }

        this.hoveredTurretAxis = found;
        this.paintTurretHandles();
    };
    onPointerUp = (): void => {
        if (this.turretDrag === null) {
            return;
        }

        // The turret stays where it was put. Springing back to rest on release would make the
        // handle a momentary readout rather than a control.
        this.turretDrag = null;
        this.paintTurretHandles();
    };

    constructor(
        private readonly context: ViewportContext,
        private readonly host: TurretHost,
    ) {}

    /**
     * Takes a pointer-down that lands on a turret knob, and starts the drag; false when it missed every
     * knob and the event is the viewport's to handle.
     */
    grabKnob(event: PointerEvent): boolean {
        const rect0 = this.context.canvas.getBoundingClientRect();
        const grabbed = this.turretKnobAt(
            event.clientX - rect0.left,
            event.clientY - rect0.top,
        );

        if (grabbed !== null) {
            // Before anything else, and it takes the event: a drag that also orbited the camera
            // would move the turret and the view at once, and neither would be readable.
            //
            // IMMEDIATE propagation, and this listener runs in the CAPTURE phase - both required.
            // `OrbitControls` listens for `pointerdown` on this same canvas and was registered
            // first, and plain `stopPropagation` does not stop another listener on the same
            // element. It only starts tracking a drag from that one listener (three 0.185 adds its
            // document move/up listeners there), so keeping it from seeing the grab is the whole
            // fix. Measured with a real mouse on the Gargantuan: the turret turned 40 degrees and
            // the camera swung 173 units with it.
            event.preventDefault();
            event.stopImmediatePropagation();

            // Keep the moves coming when the drag leaves the canvas, which a fast one does; the
            // controls would have captured the pointer here and no longer do.
            this.context.canvas.setPointerCapture(event.pointerId);

            this.turretDrag = {
                id: grabbed.id,
                axis: grabbed.axis,
                startAim: this.turretAim(grabbed.id),
                startX: event.clientX,
                startY: event.clientY,
            };

            this.paintTurretHandles();
            return true;
        }

        return false;
    }

    /** Re-sizes the arcs and the handle tracks for a new model scale. See `PreviewViewport.setModelScale`. */
    applyModelScale(): void {
        for (const arc of this.arcs) {
            this.applyArcScale(arc);
        }

        for (const nodes of this.turretHandleNodes.values()) {
            nodes.yaw.track.scale.setScalar(1 / this.host.modelScale);
            nodes.pitch.track.scale.setScalar(1 / this.host.modelScale);
        }
    }

    /** Whether the arcs are drawn, so a capture can put them back as it found them. */
    get arcsShown(): boolean {
        return this.arcsVisible;
    }

    /**
     * Draws the firing arcs, replacing whatever was there.
     *
     * Off by default, and for good reason: a Star Destroyer's arcs reach 2000 units on a hull about
     * 600 long, so leaving them on hides the ship inside its own cones.
     */
    setFireArcs(
        arcs: readonly {
            partId: string;
            bone: string;
            frame: "bone" | "hull";
            kind: "fire" | "rotation";
            widthDegrees: number;
            heightDegrees: number;
            range: number;
        }[],
    ): void {
        this.clearFireArcs();

        // How BIG the subject is, so a 2000-unit reach on a 600-unit hull is drawn at a length the
        // reader can stand outside of. See `drawnConeRange` - the row still states the true range.
        const modelSize = this.context.cameraRig.modelSpan();

        for (const arc of arcs) {
            const boneNode = this.context.assembly.fireBoneAttachment(
                arc.partId,
                arc.bone,
            );
            const drawn = {
                ...arc,
                range: drawnConeRange(
                    arc.range,
                    modelSize,
                    arc.widthDegrees,
                    arc.heightDegrees,
                ),
            };
            // Two envelopes, told apart by weight rather than by hue: they describe the SAME
            // weapon, and a second colour would read as a second weapon. The solid one is where it
            // may shoot; the lighter one is where the barrel may point.
            const rotation = arc.kind === "rotation";
            const mesh = new THREE.Mesh(
                coneGeometry(drawn),
                (rotation ? ROTATION_ARC_MATERIAL : FIRE_ARC_MATERIAL).clone(),
            );

            // Which frame the arc is measured in is the engine's choice, not ours, and the two
            // firing paths disagree:
            //
            //   bone - the weapon-aim test measures in the hardpoint's OWN
            //          coordinate system, so the bone's rotation IS the aim and parenting to it is
            //          exactly right.
            //   hull - the cone-of-fire test reads the muzzle matrix for its
            //          TRANSLATION and then discards the rotation, rebuilding the frame from
            //          owner->Get_Facing(). The arc starts at the bone and points where the HULL
            //          points, so a rotated MuzzleA must not carry it round.
            //
            // Parenting to the model root and placing the mesh at the bone's position in that space
            // gives exactly that: the bone supplies the origin, and the HULL'S FACING supplies the
            // orientation. Not the root's raw axes - the nose is +Z there, and the cone is built
            // along +X. See `hullFrameBasis`.
            const parent =
                arc.frame === "hull" ? this.context.modelRoot : boneNode;

            if (arc.frame === "hull") {
                boneNode.updateWorldMatrix(true, false);
                this.context.modelRoot.updateWorldMatrix(true, false);
                mesh.position.copy(
                    this.context.modelRoot.worldToLocal(
                        boneNode.getWorldPosition(new THREE.Vector3()),
                    ),
                );

                // And the HULL's facing, not the root's raw axes. The cone is built along +X for a
                // fire bone; the hull's nose is +Z here. Leaving this out is what put every
                // WEAPON-behaviour arc a quarter turn off. See `hullFrameBasis`.
                const [forward, lateral, up] = hullFrameBasis();
                mesh.quaternion.setFromRotationMatrix(
                    new THREE.Matrix4().makeBasis(
                        new THREE.Vector3(...forward),
                        new THREE.Vector3(...lateral),
                        new THREE.Vector3(...up),
                    ),
                );

                // The position above is only the starting point; `followHullArcs` keeps it on the
                // muzzle once a clip starts moving the bone.
                this.hullArcs.push({ mesh, muzzle: boneNode });
            }

            mesh.visible = this.arcsVisible;
            // The gizmo is a hint, not geometry: it must never occlude the hull it describes.
            mesh.renderOrder = 1;

            // A range is a WORLD distance, so an arc parented inside the scaled geometry has to
            // undo that scale. Recorded on the mesh so a later `setModelScale` can redo it without
            // rebuilding every cone.
            mesh.userData.aetArcFrame = arc.frame;
            // Not the model. A hardpoint arc hangs on a fire bone inside the hull, where every walk
            // over the model's meshes reaches it - see `isOverlay`.
            mesh.userData.aetOverlay = true;
            this.applyArcScale(mesh);

            // A CHILD of the fill, so one visible flag governs both and an outline can never be
            // left burning over an arc that has been switched off.
            const outline = new THREE.LineSegments(
                coneOutlineGeometry(drawn),
                (rotation
                    ? ROTATION_ARC_OUTLINE_MATERIAL
                    : FIRE_ARC_OUTLINE_MATERIAL
                ).clone(),
            );
            outline.renderOrder = 2;
            mesh.add(outline);

            parent.add(mesh);
            this.arcs.push(mesh);
        }
    }

    setFireArcsVisible(visible: boolean): void {
        this.arcsVisible = visible;
        for (const arc of this.arcs) {
            arc.visible = visible;
        }
    }

    /**
     * Poses turrets at their rest angle.
     *
     * COMPOSED on the pose the model authored, through the same `aimBone` the sweep and the
     * handles use: the game applies a turret's turn on top of the bone it computed from the file,
     * so a rest angle is a turn off the authored pose and never a replacement for it. Writing the
     * angle into the bone's Z rotation outright threw the authored yaw away and turned every turret
     * whose bone the exporter had oriented - a quarter turn on most (#389).
     *
     * Traverse is about the bone's local Z. Alamo is Z-up, and the exporter's Z-up-to-Y-up rotation
     * sits on the ROOT node, so a bone's own axes still follow the file's convention - the same one
     * the fire bones showed: X forward, Y lateral, Z up.
     */
    setTurretRestAngles(
        poses: readonly {
            partId: string;
            turretBone: string;
            barrelBone: string | null;
            rest: TurretAim;
        }[],
    ): void {
        this.turretRests.clear();
        for (const pose of poses) {
            this.turretRests.set(
                restKey(pose.partId, pose.turretBone),
                pose.rest,
            );
            const part = this.context.parts.get(pose.partId);
            const turret = part?.bones.get(pose.turretBone.toLowerCase());
            if (turret === undefined) {
                continue;
            }
            const barrel =
                pose.barrelBone === null
                    ? undefined
                    : part?.bones.get(pose.barrelBone.toLowerCase());
            this.poseTurretBones(turret, barrel, pose.rest);
        }
        // A held or shown handle is an aim off the rest pose, so it follows the rest.
        this.applyTurretAims();
    }

    /**
     * Sweeps the turrets through the traverse their XML declares.
     *
     * The pose is recomputed per frame from `sweepAngles`, so what is on screen is always the
     * declared extent rather than an accumulated one - an incremental turn drifts, and a turret that
     * drifts past its own end stops is exactly the thing this exists to disprove.
     *
     * Traverse is about the bone's local Z and elevation about its X. Alamo is Z-up and the
     * exporter's Z-up-to-Y-up rotation sits on the ROOT node, so a bone's own axes still follow the
     * file's convention - the same one the fire bones showed.
     */
    setTurretSweep(
        sweeps: readonly {
            partId: string;
            turretBone: string;
            barrelBone: string | null;
            turret: PreviewTurret;
        }[],
    ): void {
        this.turretSweeps = [...sweeps];

        // Anything that has stopped sweeping goes back to the pose the model authored. Only bones
        // this class actually moved are in the map, so a part that has not loaded yet is not
        // "missing" here - it was never touched.
        const swinging = new Set<THREE.Object3D>();

        for (const sweep of sweeps) {
            const part = this.context.parts.get(sweep.partId);
            const turret = part?.bones.get(sweep.turretBone.toLowerCase());
            const barrel =
                sweep.barrelBone === null
                    ? undefined
                    : part?.bones.get(sweep.barrelBone.toLowerCase());

            if (turret !== undefined) {
                swinging.add(turret);
            }

            if (barrel !== undefined) {
                swinging.add(barrel);
            }
        }

        for (const [bone, rest] of [...this.sweepRest]) {
            if (!swinging.has(bone)) {
                bone.quaternion.copy(rest);
                this.sweepRest.delete(bone);
            }
        }

        if (sweeps.length === 0) {
            this.sweepPhase = 0;
        }
    }

    /**
     * Puts drag handles on the turrets whose arcs are shown, and takes them off the rest.
     *
     * Two axes and no third: the engine has no roll on a turret -
     * the turret-angle calculation returns `Vector3(0.0, pitch, yaw)` with X a literal zero.
     *
     * Each track spans only what the engine allows, so the stop is visible before it is reached.
     * Which turrets these are is {@link turretHandles}' decision, off the arc toggle, and not this
     * class's.
     */
    setTurretHandles(handles: readonly TurretHandle[]): void {
        this.turretHandles = [...handles];

        const wanted = new Set(handles.map((handle) => handle.id));

        for (const [id, nodes] of [...this.turretHandleNodes]) {
            if (!wanted.has(id)) {
                disposeTurretHandle(nodes.yaw);
                disposeTurretHandle(nodes.pitch);
                this.turretHandleNodes.delete(id);
            }
        }

        // The aim OUTLIVES the handle being taken off and put back: switching an arc off and on
        // again is not a request to re-centre a turret the reader deliberately pointed somewhere.
        // Same rule the arc toggles already follow. A SUBJECT SWITCH is the case where the aims
        // really should go, and `clearTurretHandles` is where that happens.
        if (handles.length === 0) {
            this.hoveredTurretAxis = null;
            this.turretDrag = null;
        }

        const radius = trackRadius(this.context.cameraRig.modelSpan());

        for (const handle of handles) {
            if (this.turretHandleNodes.has(handle.id)) {
                continue;
            }

            const yawBone = this.turretBoneNode(
                handle.partId,
                handle.turretBone,
            );
            // The barrel carries elevation when the model separates the two; 2 of 19 do not, and
            // then one bone does both - exactly as the sweep already handles it.
            const pitchBone =
                handle.barrelBone === null
                    ? yawBone
                    : (this.turretBoneNode(handle.partId, handle.barrelBone) ??
                      yawBone);

            if (yawBone === undefined || pitchBone === undefined) {
                continue;
            }

            this.turretHandleNodes.set(handle.id, {
                yaw: this.buildTurretHandle(handle, "yaw", yawBone, radius),
                // One bone doing both - the model names no barrel, or names one it does not carry -
                // so the pitch track has to follow the yaw itself. With a barrel, the hierarchy
                // carries it.
                pitch: this.buildTurretHandle(
                    handle,
                    "pitch",
                    pitchBone,
                    radius,
                    pitchBone === yawBone,
                ),
            });
        }

        this.applyTurretAims();
    }

    /** Where the reader has pointed each turret, for the shell to read back out. */
    turretAim(id: string): TurretAim {
        return this.turretAims.get(id) ?? TURRET_AT_REST;
    }

    // ── damage ────────────────────────────────────────────────────────────────

    /**
     * Puts one turret back where the model authored it.
     *
     * Needed because releasing a drag deliberately does NOT spring back - a handle that reset
     * itself would be a momentary readout rather than a control - so there has to be a way home.
     */
    resetTurretAim(id: string): void {
        this.turretAims.delete(id);
        this.applyTurretAims();
        this.host.onTurretAimed?.(id, TURRET_AT_REST);
    }

    /**
     * Undoes the geometry scale on an arc that lives inside it.
     *
     * A bone-frame arc hangs on a bone of the scaled model, so it would be drawn at `range * scale`
     * - and a range is already a world distance. A hull-frame one is a child of the unscaled model
     * root and needs nothing.
     */
    private applyArcScale(arc: THREE.Mesh): void {
        const inside = arc.userData.aetArcFrame === "bone";

        arc.scale.setScalar(inside ? 1 / this.host.modelScale : 1);
    }

    clearFireArcs(): void {
        for (const arc of this.arcs) {
            arc.removeFromParent();
            arc.geometry.dispose();
            (arc.material as THREE.Material).dispose();

            // The outline belongs to the fill, so it goes with it - it holds its own geometry and
            // its own cloned material, and neither is freed by dropping the parent.
            for (const child of arc.children) {
                if (child instanceof THREE.LineSegments) {
                    child.geometry.dispose();
                    (child.material as THREE.Material).dispose();
                }
            }
        }

        this.arcs.length = 0;
        // The meshes are gone, and the muzzles may be about to be: a stale entry here would keep
        // a disposed bone alive and place a removed cone every frame.
        this.hullArcs.length = 0;
    }

    /**
     * Puts every hull-frame arc back on its muzzle, after the clip has moved the bones this frame.
     *
     * Position only. The orientation is the hull's facing, set once when the arc was built, and a
     * wing that rotates as it deploys must not carry the cone round with it - the engine discards
     * the muzzle's rotation too.
     */
    followHullArcs(): void {
        if (this.hullArcs.length === 0) {
            return;
        }

        this.context.modelRoot.updateWorldMatrix(true, false);

        for (const { mesh, muzzle } of this.hullArcs) {
            // Fresh, because the mixer has moved the bone but world matrices are only recomputed at
            // render time.
            muzzle.updateWorldMatrix(true, false);
            mesh.position.copy(
                this.context.modelRoot.worldToLocal(
                    muzzle.getWorldPosition(this.muzzleScratch),
                ),
            );
        }
    }

    /**
     * Everything this class holds about turrets, for a SUBJECT SWITCH.
     *
     * A handle's track is parented to a bone of the part about to be disposed, and its aim is a
     * pose for a bone the next subject does not have. `setTurretHandles([])` alone would not do:
     * that keeps the aims on purpose, so switching an arc off and on again does not re-centre a
     * turret the reader deliberately pointed somewhere. A new subject is the case where they SHOULD
     * go - the same distinction `clear()` already draws for the fading particles.
     */
    clearTurretHandles(): void {
        for (const nodes of this.turretHandleNodes.values()) {
            disposeTurretHandle(nodes.yaw);
            disposeTurretHandle(nodes.pitch);
        }

        this.turretHandleNodes.clear();
        this.turretHandles = [];
        this.turretAims.clear();
        this.hoveredTurretAxis = null;
        this.turretDrag = null;
    }

    /**
     * One track plus its stops and its knob, parented to the bone it turns.
     *
     * Parented, so the yaw track rides the hull and the pitch track rides the traverse that is
     * already under it - which is what makes the two axes compose the way the bones do without this
     * class having to compose them.
     */
    private buildTurretHandle(
        handle: TurretHandle,
        axis: TurretAxis,
        bone: THREE.Object3D,
        radius: number,
        followsYaw = false,
    ): TurretHandleParts {
        const range = axis === "yaw" ? handle.yaw : handle.pitch;

        const track = new THREE.LineSegments(
            positionGeometry(
                trackSegments(range, radius, TURRET_TRACK_SEGMENTS),
            ),
            TURRET_TRACK_MATERIAL,
        );
        track.renderOrder = TURRET_HANDLE_RENDER_ORDER;

        const stops = new THREE.LineSegments(
            positionGeometry(
                stopTicks(range, radius, radius * TURRET_STOP_TICK_FRACTION),
            ),
            TURRET_STOP_MATERIAL,
        );
        stops.renderOrder = TURRET_HANDLE_RENDER_ORDER;

        const knob = new THREE.Mesh(
            new THREE.SphereGeometry(radius * TURRET_KNOB_FRACTION, 12, 8),
            TURRET_KNOB_MATERIAL,
        );
        knob.renderOrder = TURRET_HANDLE_RENDER_ORDER;

        // A CHILD of the track, so one parent carries the whole handle and nothing can be left
        // behind when it is removed.
        track.add(stops);
        track.add(knob);

        // The bone is inside the scaled geometry and the radius is a world length - it comes from
        // the subject's world span - so the handle undoes the scale exactly as a bone-frame arc
        // does. Without this a 0.5-scale ship's handles would be drawn at half their size.
        track.scale.setScalar(1 / this.host.modelScale);

        // On an ANCHOR beside the bone, not on the bone: the track shows the stops, and the stops do
        // not move when the turret does. A bone always has a parent inside a loaded model; the
        // fallback only keeps a malformed tree from dropping the handle altogether.
        const anchor = new THREE.Object3D();
        // Not the model, and the flag sits on the anchor so the whole handle - track, stops, knob -
        // is pruned in one place. See `isOverlay`.
        anchor.userData.aetOverlay = true;
        anchor.add(track);
        (bone.parent ?? bone).add(anchor);

        const parts = {
            anchor,
            bone,
            followsYaw,
            track,
            stops,
            knob,
            radius,
            range,
        };
        this.placeTurretAnchor(parts);

        return parts;
    }

    /**
     * Every handle's anchors, re-placed from where their bones are now. Called every frame.
     *
     * Every frame rather than only when a knob moves, because three other things move turret bones:
     * the sweep, a clip, and the rest angle the XML declares. A track that only caught up on a drag
     * would lag behind all three.
     */
    placeTurretAnchors(): void {
        for (const nodes of this.turretHandleNodes.values()) {
            this.placeTurretAnchor(nodes.yaw);
            this.placeTurretAnchor(nodes.pitch);
        }
    }

    /**
     * One anchor, in the frame its axis is measured against.
     *
     * The bone's POSITION and scale, which clips may move, and its REST orientation - the pose
     * `aimBone` measures every angle from - never its live one. That is what holds a yaw track
     * still while the turret yaws.
     *
     * A pitch track needs one thing more when there is no barrel and the turret bone carries both
     * axes (2 of 19 models). Its rest frame is the same as the yaw track's, but elevation turns
     * WITH the turret, so the track has to swing round by the yaw and still not tilt with the pitch.
     * The yaw is recovered from the bone's live pose rather than from the reader's aim, so a sweep
     * or a clip turning that bone is followed too: the twist of `rest^-1 * pose` about the traverse
     * axis. `aimBone` composes `Rz(yaw) * Ry(pitch)`, and the twist of that product about Z is
     * exactly `Rz(yaw)`.
     *
     * With a barrel none of that is needed. The barrel's rest frame already hangs below the turret
     * bone, so the yaw reaches the pitch track through the hierarchy, and the pitch - which is the
     * barrel's own rotation - does not.
     */
    private placeTurretAnchor(parts: TurretHandleParts): void {
        const { anchor, bone } = parts;
        const rest = this.sweepRest.get(bone) ?? bone.quaternion;

        anchor.position.copy(bone.position);
        anchor.scale.copy(bone.scale);
        anchor.quaternion.copy(rest);

        if (parts.followsYaw) {
            const live = this.twistScratch
                .copy(rest)
                .invert()
                .multiply(bone.quaternion);
            // Swing-twist about Z: keep only the Z component and renormalise. Guarded, because a
            // pure 180-degree swing leaves nothing to normalise.
            const length = Math.hypot(live.w, live.z);

            if (length > 1e-6) {
                live.set(0, 0, live.z / length, live.w / length);
                anchor.quaternion.multiply(live);
            }
        }
    }

    /**
     * The bone an axis turns about, looking on the hull before the attached model.
     *
     * Same rule as `PartAssembly.fireBoneAttachment` and for the same measured reason: a hardpoint's
     * model carries a copy of the hull's skeleton, and resolving a bone there applies the attachment
     * offset twice.
     */
    private turretBoneNode(
        partId: string,
        bone: string,
    ): THREE.Object3D | undefined {
        return (
            this.context.parts.get("hull")?.bones.get(bone.toLowerCase()) ??
            this.context.parts.get(partId)?.bones.get(bone.toLowerCase())
        );
    }

    /** Poses every handled turret from its stored aim, and moves the knobs to match. */
    private applyTurretAims(): void {
        for (const handle of this.turretHandles) {
            const aim = this.turretAim(handle.id);
            const nodes = this.turretHandleNodes.get(handle.id);

            if (nodes !== undefined) {
                nodes.yaw.knob.position.set(
                    ...knobPoint(handle.yaw, aim.yaw, nodes.yaw.radius),
                );
                nodes.pitch.knob.position.set(
                    ...knobPoint(handle.pitch, aim.pitch, nodes.pitch.radius),
                );
            }

            this.poseTurret(handle, aim);
        }
    }

    /**
     * Turns one turret's bones to an aim.
     *
     * The same composition the sweep uses, and deliberately the same method: two things posing one
     * bone is the bug pattern this repo already paid for once, with a resting pose fighting a clip.
     * Dragging pauses the sweep for exactly this reason - see {@link advanceTurretSweep}.
     */
    private poseTurret(handle: TurretHandle, aim: TurretAim): void {
        const turret = this.turretBoneNode(handle.partId, handle.turretBone);

        if (turret === undefined) {
            return;
        }

        const barrel =
            handle.barrelBone === null
                ? undefined
                : this.turretBoneNode(handle.partId, handle.barrelBone);

        // Off the rest pose, which is what the handle reads out and what the sweep swings about.
        const rest =
            this.turretRests.get(restKey(handle.partId, handle.turretBone)) ??
            TURRET_AT_REST;
        this.poseTurretBones(turret, barrel, {
            yaw: rest.yaw + aim.yaw,
            pitch: rest.pitch + aim.pitch,
        });
    }

    /**
     * Turns a turret's bones to an aim measured from the pose the model authored.
     *
     * The barrel carries the elevation when the model separates the two; one bone does both
     * otherwise, the elevation composed after the traverse.
     */
    private poseTurretBones(
        turret: THREE.Object3D,
        barrel: THREE.Object3D | undefined,
        aim: TurretAim,
    ): void {
        // NEGATIVE elevation, so a positive pitch is nose up: rotating about +Y takes local +X,
        // which is forward, towards -Z, and Z is up.
        if (barrel !== undefined) {
            this.aimBone(turret, TRAVERSE_AXIS, aim.yaw);
            this.aimBone(barrel, ELEVATION_AXIS, -aim.pitch);
        } else {
            this.aimBone(
                turret,
                TRAVERSE_AXIS,
                aim.yaw,
                ELEVATION_AXIS,
                -aim.pitch,
            );
        }
    }

    // ── frame loop ────────────────────────────────────────────────────────────

    /** Its authored pose, remembered the first time it is asked for. */
    private sweepRestOf(bone: THREE.Object3D): THREE.Quaternion {
        let rest = this.sweepRest.get(bone);

        if (rest === undefined) {
            rest = bone.quaternion.clone();
            this.sweepRest.set(bone, rest);
        }

        return rest;
    }

    /** Advances the sweep. Called from the render loop; a no-op when nothing is sweeping. */
    advanceTurretSweep(deltaSeconds: number): void {
        if (this.turretSweeps.length === 0) {
            return;
        }

        // Twelve seconds end to end. Slow enough to follow a barrel with the eye, which is what a
        // reader checking a traverse is actually doing.
        this.sweepPhase = (this.sweepPhase + deltaSeconds / 12) % 1;

        // A bone the reader has taken hold of is theirs until they let go. Two things posing one
        // bone is the bug pattern this repo already paid for, with a resting pose fighting a clip -
        // and here the sweep would simply win every frame, so the handle would not appear to move.
        const held = this.turretDrag?.id ?? null;

        for (const sweep of this.turretSweeps) {
            if (held !== null && this.sweepIsHandled(sweep, held)) {
                continue;
            }

            const pose = sweepAngles(sweep.turret, this.sweepPhase);
            const part = this.context.parts.get(sweep.partId);

            const turret = part?.bones.get(sweep.turretBone.toLowerCase());
            if (turret !== undefined) {
                this.aimBone(turret, TRAVERSE_AXIS, pose.rotate);
            }

            // The barrel carries the elevation, when the model separates the two. A turret with no
            // barrel bone elevates on its own bone instead - 19 objects name a turret bone and only
            // 17 a barrel, so the pair is genuinely optional.
            const barrel =
                sweep.barrelBone === null
                    ? undefined
                    : part?.bones.get(sweep.barrelBone.toLowerCase());

            // NEGATIVE, so a positive elevation is nose UP. Rotating about +Y by a positive angle
            // takes local +X - which is forward - towards -Z, and Z is up.
            if (barrel !== undefined) {
                this.aimBone(barrel, ELEVATION_AXIS, -pose.elevate);
            } else if (turret !== undefined) {
                // One bone doing both: the traverse is already on it, so the elevation composes
                // after it rather than replacing it.
                this.aimBone(
                    turret,
                    TRAVERSE_AXIS,
                    pose.rotate,
                    ELEVATION_AXIS,
                    -pose.elevate,
                );
            }
        }
    }

    /**
     * Whether a sweep would move the BONES a held handle is holding.
     *
     * Matched on the bone rather than on the id, because the two lists are keyed differently: a
     * sweep carries the weapon or hardpoint that declared it, and one hardpoint's turret bone can
     * be reached through both. Same part, same bone, same bone node.
     */
    private sweepIsHandled(
        sweep: { partId: string; turretBone: string },
        heldId: string,
    ): boolean {
        const handle = this.turretHandles.find((one) => one.id === heldId);

        return (
            handle !== undefined &&
            handle.partId === sweep.partId &&
            handle.turretBone.toLowerCase() === sweep.turretBone.toLowerCase()
        );
    }

    /**
     * Turns a bone by one or two angles, measured from the pose the model authored.
     *
     * Degrees in, because that is what the XML declares and what the panel reads out.
     */
    private aimBone(
        bone: THREE.Object3D,
        axis: THREE.Vector3,
        degrees: number,
        secondAxis?: THREE.Vector3,
        secondDegrees?: number,
    ): void {
        const turn = new THREE.Quaternion().setFromAxisAngle(
            axis,
            degrees * (Math.PI / 180),
        );

        if (secondAxis !== undefined && secondDegrees !== undefined) {
            turn.multiply(
                new THREE.Quaternion().setFromAxisAngle(
                    secondAxis,
                    secondDegrees * (Math.PI / 180),
                ),
            );
        }

        bone.quaternion.copy(this.sweepRestOf(bone)).multiply(turn);
    }

    /** Picks the nearest joint to the pointer, so clicking a bone selects it. */
    /**
     * The knob nearest the pointer, if one is near enough to mean it.
     *
     * Projected to the screen and measured in pixels, exactly as the joints are picked: a knob is a
     * small sphere the reader aims at with a cursor, so screen distance is the honest metric and a
     * ray through a 12-segment sphere is not. It also lets a knob be grabbed through the hull,
     * which is the whole reason the handle draws with `depthTest: false`.
     */
    private turretKnobAt(
        x: number,
        y: number,
    ): { id: string; axis: TurretAxis } | null {
        const rect = this.context.canvas.getBoundingClientRect();
        const world = new THREE.Vector3();
        let best: { id: string; axis: TurretAxis; distance: number } | null =
            null;

        for (const [id, nodes] of this.turretHandleNodes) {
            for (const axis of TURRET_AXES) {
                const knob = nodes[axis].knob;

                knob.getWorldPosition(world);
                const ndc = world.project(this.context.camera);

                // Behind the camera. Projection mirrors such a point to the front, so without this
                // a turret on the far side of the ship is pickable at a place it is not.
                if (ndc.z > 1) {
                    continue;
                }

                const distance = Math.hypot(
                    (ndc.x * 0.5 + 0.5) * rect.width - x,
                    (-ndc.y * 0.5 + 0.5) * rect.height - y,
                );

                if (best === null || distance < best.distance) {
                    best = { id, axis, distance };
                }
            }
        }

        return best !== null && best.distance <= TURRET_KNOB_PICK_RADIUS
            ? { id: best.id, axis: best.axis }
            : null;
    }

    /** The hovered or dragged axis in its bright materials, everything else in its quiet ones. */
    private paintTurretHandles(): void {
        const active = this.turretDrag ?? this.hoveredTurretAxis;

        for (const [id, nodes] of this.turretHandleNodes) {
            for (const axis of TURRET_AXES) {
                const on =
                    active !== null && active.id === id && active.axis === axis;

                nodes[axis].track.material = on
                    ? TURRET_TRACK_HOVER_MATERIAL
                    : TURRET_TRACK_MATERIAL;
                nodes[axis].knob.material = on
                    ? TURRET_KNOB_HOVER_MATERIAL
                    : TURRET_KNOB_MATERIAL;
            }
        }
    }

    /** Moves the dragged turret to follow the pointer, clamped to what the engine allows. */
    private dragTurret(event: PointerEvent): void {
        const drag = this.turretDrag;
        const handle = this.turretHandles.find((one) => one.id === drag?.id);

        if (drag === undefined || drag === null || handle === undefined) {
            return;
        }

        const aim = dragToAim(
            handle,
            drag.startAim,
            drag.axis === "yaw" ? event.clientX - drag.startX : 0,
            drag.axis === "pitch" ? event.clientY - drag.startY : 0,
            TURRET_DEGREES_PER_PIXEL,
        );

        this.turretAims.set(drag.id, aim);
        this.applyTurretAims();
        this.host.onTurretAimed?.(drag.id, aim);
    }
}
