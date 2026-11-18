// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's overlay: bone labels, targeting marks, selection boxes, the skeleton and the selected
// bones' axes. Everything here only READS the model - part transforms, bones and bounds - and draws on top
// of it, which is why it is the first layer cut out of the viewport.

import * as THREE from "three";
import {
    type LabelMode,
    alamoBoneName,
    alamoNodeName,
    labelCandidates,
} from "./skeleton";
import { type ReticleMark, reticleSizePx, healthColour } from "./reticles";
import { boxTargetFor, boxRoots, rowsInBox } from "./selectionBox";
import { type ViewportContext } from "./viewportContext";

/** Smallest gap between two bone labels, in pixels, before the further one is dropped. */
const LABEL_SPACING = 26;

/** Reused so a tick of reticle projection allocates nothing at all. */
const RETICLE_NDC = new THREE.Vector3();

/** The twelve edges of one box, as line-segment pairs, appended to a position buffer. */
function appendBoxEdges(into: number[], box: THREE.Box3): void {
    const { min, max } = box;
    const corner = (
        x: number,
        y: number,
        z: number,
    ): [number, number, number] => [
        x === 0 ? min.x : max.x,
        y === 0 ? min.y : max.y,
        z === 0 ? min.z : max.z,
    ];

    // Each edge joins two corners differing in exactly one coordinate, which is what this walk
    // enumerates - four edges along each axis, twelve in all, no duplicates.
    for (let axis = 0; axis < 3; axis++) {
        for (let pair = 0; pair < 4; pair++) {
            const others = [pair & 1, (pair >> 1) & 1];
            const at = (end: number): [number, number, number] => {
                const coords = [...others];
                coords.splice(axis, 0, end);

                return corner(coords[0], coords[1], coords[2]);
            };

            into.push(...at(0), ...at(1));
        }
    }
}

/** Replaces a buffer geometry's positions, reallocating only when the length changes. */
function setPositions(geometry: THREE.BufferGeometry, values: number[]): void {
    const existing = geometry.getAttribute("position") as
        THREE.BufferAttribute | undefined;

    if (existing !== undefined && existing.array.length === values.length) {
        (existing.array as Float32Array).set(values);
        existing.needsUpdate = true;
        return;
    }

    geometry.setAttribute(
        "position",
        new THREE.Float32BufferAttribute(values, 3),
    );
}

/** How often the label overlay is repositioned. Labels do not need the frame rate. */
const LABEL_INTERVAL_MS = 80;

/** What OverlayLayer still asks of the viewport itself: what only the viewport has. */
export interface OverlayHost {
    readonly onReticleClicked: ((hardpointId: string) => void) | null;
}

/** How close to a joint a click must land, in pixels, to count as selecting it. */
const JOINT_PICK_RADIUS = 10;

export class OverlayLayer {
    /** Joints and the lines between them, rebuilt each frame because animation moves bones. */
    readonly joints: THREE.Points;
    readonly jointLines: THREE.LineSegments;
    /** One wireframe box per selected row, so the tree can point at something in the viewport. */
    readonly selectionBoxes: THREE.LineSegments;
    /** The selected bone's local axes, so its FACING is visible and not just its position. */
    private readonly boneAxes: THREE.LineSegments;
    /** The rows the reader has selected in the tree, in the order they picked them. */
    private selectedRows: readonly string[] = [];
    /** Parts outlined whole - a hardpoint's own model. See `setSelectedHardpoint`. */
    private selectedParts: readonly string[] = [];
    /** A selected row that must not get its own box, because its part is outlined instead. */
    private unboxedRow: string | null = null;
    private skeletonVisible = false;
    private labelMode: LabelMode = "none";
    /**
     * Bones drawn with their own axes, by index.
     *
     * A SET, because the tree and the fire-bone buttons both select several at once - and comparing
     * two fire bones is the whole reason for pointing at either of them. It was one index, so the
     * second pick quietly erased the first one's triad.
     */
    private selectedBones: ReadonlySet<number> = new Set();
    private lastLabelUpdate = 0;
    private readonly labelPool: HTMLElement[] = [];
    /** The targeting marks currently being drawn, and the pool of images drawing them. */
    private reticles: ReticleMark[] = [];
    private readonly reticlePool: HTMLElement[] = [];
    private reticleScreenSize: number | null | undefined = null;
    /** The hardpoint the pointer is over, so its mark can show the tracked art. */
    private hoveredReticle: string | null = null;
    /** Bones a weapon fires from, lowercased. Their aim axis is drawn as an arrow. */
    private fireBones: ReadonlySet<string> = new Set();

    constructor(
        private readonly context: ViewportContext,
        private readonly host: OverlayHost,
    ) {
        // Drawn on top of the model rather than inside it: a joint buried in a hull is not a joint
        // you can click, and the point of the skeleton view is reaching the bones.
        this.joints = new THREE.Points(
            new THREE.BufferGeometry(),
            new THREE.PointsMaterial({
                size: 7,
                sizeAttenuation: false,
                depthTest: false,
            }),
        );
        this.jointLines = new THREE.LineSegments(
            new THREE.BufferGeometry(),
            new THREE.LineBasicMaterial({
                color: 0x6fb3ff,
                depthTest: false,
                transparent: true,
                opacity: 0.7,
            }),
        );

        // Amber rather than the skeleton's blue: the two are shown together, and a selection has
        // to be tellable from a joint at a glance. Drawn through the model on purpose - a box only
        // visible when the thing it points at is already in front is no help finding it.
        this.selectionBoxes = new THREE.LineSegments(
            new THREE.BufferGeometry(),
            new THREE.LineBasicMaterial({ color: 0xffb347, depthTest: false }),
        );
        this.selectionBoxes.renderOrder = 999;
        this.selectionBoxes.frustumCulled = false;
        this.selectionBoxes.visible = false;
        context.scene.add(this.selectionBoxes);

        // The selected bone's own AXES. A box says where a bone is and nothing about which way it
        // faces, which is the question that actually matters here - a fire bone aims along its
        // local X, and a hardpoint's whole placement is its rotation. Vertex-coloured red/green/blue
        // for X/Y/Z, the convention every 3D tool shares.
        this.boneAxes = new THREE.LineSegments(
            new THREE.BufferGeometry(),
            new THREE.LineBasicMaterial({
                vertexColors: true,
                depthTest: false,
            }),
        );
        this.boneAxes.renderOrder = 1000;
        this.boneAxes.frustumCulled = false;
        this.boneAxes.visible = false;
        context.scene.add(this.boneAxes);

        this.joints.renderOrder = 999;
        this.jointLines.renderOrder = 999;
        this.joints.visible = false;
        this.jointLines.visible = false;
        this.joints.frustumCulled = false;
        this.jointLines.frustumCulled = false;
        context.scene.add(this.joints, this.jointLines);
    }

    /**
     * The per-frame overlay work that must run BEFORE the camera is prepared: the boxes and axes
     * that follow an animated bone, and the skeleton with its throttled labels.
     *
     * `animating` is whether a clip is playing - the only time a selected bone's triad can move.
     */
    beforeCamera(animating: boolean): void {
        if (this.selectedRows.length > 0) {
            this.updateSelectionBoxes();
        }

        // An animation moves the bone under the triad, so it follows on the same terms as the box.
        if (this.selectedBones.size > 0 && animating) {
            this.updateBoneAxes();
        }

        if (this.skeletonVisible) {
            // Rebuilt per frame because animation moves the bones; cheap for the hundreds a model has.
            this.updateSkeletonGeometry();

            const now = performance.now();
            if (now - this.lastLabelUpdate > LABEL_INTERVAL_MS) {
                this.lastLabelUpdate = now;
                this.updateLabels();
            }
        }
    }

    /**
     * Puts the targeting marks on the hardpoints they mark.
     *
     * Not under the skeleton switch: a reticle is a targeting mark, not a bone label, and it has to
     * stay on screen when the skeleton is off - which is how anyone actually looks at a ship.
     *
     * EVERY frame, and AFTER `prepareForCamera`. Both halves of that are the fix for marks that
     * visibly lagged the ship while the camera moved, and neither was a performance problem -
     * the work per mark is two map lookups and a projection. Sharing the labels' 80ms throttle
     * ran them at 12Hz under a 60Hz canvas, so an orbiting camera left them up to five frames
     * behind the hull they are painted on; running before the camera's matrix was updated added
     * one more frame on top, because they were projected through the PREVIOUS frame's view.
     * A bone label a few pixels behind is invisible. A reticle is drawn on the thing it marks,
     * so the same error reads as the mark sliding off the hardpoint.
     */
    afterCamera(): void {
        if (this.reticles.length > 0) {
            this.updateReticles();
        }
    }

    /** Forgets every mark, for a scene that is about to be cleared. See `PreviewViewport.clear`. */
    dropReticles(): void {
        this.reticles = [];
        this.hoveredReticle = null;
        this.clearReticles();
    }

    /** Whether the skeleton is drawn, so a click can pick a joint. */
    get skeletonShown(): boolean {
        return this.skeletonVisible;
    }

    setSkeletonVisible(visible: boolean): void {
        this.skeletonVisible = visible;
        this.joints.visible = visible;
        this.jointLines.visible = visible;

        if (!visible) {
            this.clearLabels();
        }
    }

    setLabelMode(mode: LabelMode): void {
        this.labelMode = mode;
        // Immediately rather than on the next throttled tick: a mode change the user just made must
        // not appear to have been ignored.
        this.lastLabelUpdate = 0;
    }

    /**
     * Which bones a weapon fires from, so the triad can mark the aim axis.
     *
     * By NAME, because that is what a weapon declares. The axis itself is local X and that is
     * measured rather than assumed - see `AlamoFireBone.AimDirection`.
     */
    setFireBones(names: ReadonlySet<string>): void {
        this.fireBones = names;
        this.updateBoneAxes();
    }

    setSelectedBones(indices: ReadonlySet<number>): void {
        this.selectedBones = indices;
        this.lastLabelUpdate = 0;
        this.updateBoneAxes();
    }

    /**
     * Draws a box round what the tree has selected.
     *
     * Selecting a bone boxes its whole SUBTREE, not its origin: a bone is where something is
     * attached, so a box around the point itself would be a dot in the middle of the thing being
     * pointed at. A row whose ancestor is selected too gets no box of its own - one box around the
     * arm beats a second one nested inside it around the hand.
     */
    setSelectedRows(ids: readonly string[]): void {
        this.selectedRows = [...ids];
        this.updateSelectionBoxes();
    }

    /**
     * Parts to outline whole, beside whatever rows are boxed.
     *
     * A hardpoint that attaches a model is one of these: its geometry is its own part, and boxing
     * the hull bone it is attached to gathers that bone's whole subtree instead - the turret's meshes and
     * every empty fire-point bone under it, which on the Executor stretched the box to 2211 units
     * on a 5094 hull. See `boxTargetFor`.
     */
    setSelectedHardpoint(partId: string | null, boneRow: string | null): void {
        // Decided HERE rather than by the caller, because the answer depends on which parts are in
        // the scene - and that is this object's own state, arriving one GLB at a time.
        const target = boxTargetFor(partId, new Set(this.context.parts.keys()));

        this.selectedParts = target.kind === "part" ? [target.id] : [];

        // The attach bone stays SELECTED - it lights the tree row and draws the axes - but it stops
        // being BOXED once its part is outlined. Both at once draws the outline inside a second box
        // around the bone's whole subtree, which is the 2211-unit smear this replaced.
        this.unboxedRow = target.kind === "part" ? boneRow : null;
        this.updateSelectionBoxes();
    }

    /**
     * The targeting marks to draw, and how big.
     *
     * Replaced wholesale rather than diffed: the list is one per targetable hardpoint - ten on the Star
     * Destroyer, and never more than a few hundred - and it changes only when the scene, the state
     * or the damage does.
     */
    setReticles(
        marks: readonly ReticleMark[],
        screenSize: number | null | undefined,
    ): void {
        this.reticles = [...marks];
        // The FRACTION, not a pixel count. Resizing the panel has to change the mark's size, and a
        // number computed in React would be stale until something else happened to re-render.
        this.reticleScreenSize = screenSize;

        if (this.reticles.length === 0) {
            this.clearReticles();
        }
    }

    /**
     * Draws the selected bone's own axes at its own orientation.
     *
     * Scaled to the SUBJECT, like everything else that has to be legible from a two-unit trooper to
     * a thousand-unit command centre. Rebuilt per selection rather than per frame: a bone only moves
     * under an animation, and the box beside it is already rebuilt on that path.
     */
    private updateBoneAxes(): void {
        const hull = this.context.assembly.hull();
        const nodes = [...this.selectedBones]
            .map((index) => hull?.bonesByIndex.get(index))
            .filter((node): node is THREE.Object3D => node !== undefined);

        if (nodes.length === 0) {
            this.boneAxes.visible = false;
            return;
        }

        // The SUBJECT's size, cached. This runs per frame while a clip plays, and
        // `Box3.setFromObject` walks every vertex of the hull - recomputing it there was a full
        // geometry traversal at sixty frames a second.
        const length = Math.max(1, this.context.cameraRig.modelSpan() * 0.06);

        const positions: number[] = [];
        const colours: number[] = [];

        // X red, Y green, Z blue. X first and drawn no differently: it is the one that matters most
        // here - an Alamo fire bone aims along its local X - but colouring it specially would make
        // the triad mean something other than what every other tool means by it.
        const axes: [THREE.Vector3, [number, number, number]][] = [
            [new THREE.Vector3(1, 0, 0), [1, 0.25, 0.25]],
            [new THREE.Vector3(0, 1, 0), [0.25, 1, 0.25]],
            [new THREE.Vector3(0, 0, 1), [0.35, 0.5, 1]],
        ];

        // One triad per selected bone, all of them in the same geometry: the whole point of the set
        // is comparing two fire bones, and two draws would be two things to keep in step.
        for (const node of nodes) {
            const origin = node.getWorldPosition(new THREE.Vector3());
            const basis = new THREE.Matrix4().extractRotation(node.matrixWorld);

            // Whether THIS bone is somewhere a weapon fires from. A fire bone aims along its local X -
            // measured, see `AlamoFireBone.AimDirection` - so that axis gets an arrowhead and a longer
            // reach. Nothing else on the triad says which way the gun points, which was the complaint.
            const firing = this.fireBones.has(
                (
                    alamoBoneName(alamoNodeName(node))?.name ??
                    alamoNodeName(node)
                ).toLowerCase(),
            );

            for (const [direction, colour] of axes) {
                const aim = firing && direction.x === 1;
                const reach = aim ? length * 2.2 : length;
                const tip = direction
                    .clone()
                    .applyMatrix4(basis)
                    .multiplyScalar(reach)
                    .add(origin);

                positions.push(
                    origin.x,
                    origin.y,
                    origin.z,
                    tip.x,
                    tip.y,
                    tip.z,
                );
                colours.push(...colour, ...colour);

                if (!aim) {
                    continue;
                }

                // A four-barb head, drawn back down the shaft from the tip. Lines rather than a cone so
                // it stays one draw with the rest of the triad and cannot be occluded differently.
                const back = direction
                    .clone()
                    .applyMatrix4(basis)
                    .multiplyScalar(-reach * 0.22);
                const spread = reach * 0.09;

                for (const side of [
                    new THREE.Vector3(0, 1, 0),
                    new THREE.Vector3(0, 0, 1),
                ]) {
                    const out = side
                        .clone()
                        .applyMatrix4(basis)
                        .multiplyScalar(spread);

                    for (const sign of [1, -1]) {
                        const barb = tip
                            .clone()
                            .add(back)
                            .addScaledVector(out, sign);

                        positions.push(
                            tip.x,
                            tip.y,
                            tip.z,
                            barb.x,
                            barb.y,
                            barb.z,
                        );
                        colours.push(...colour, ...colour);
                    }
                }
            }
        }

        setPositions(this.boneAxes.geometry, positions);
        this.boneAxes.geometry.setAttribute(
            "color",
            new THREE.Float32BufferAttribute(colours, 3),
        );
        this.boneAxes.visible = true;
    }

    /**
     * Rebuilds the boxes from where the geometry currently is.
     *
     * Per frame while something is selected, because an animation moves the meshes underneath the
     * box and a box left where the mesh used to be is worse than none at all.
     */
    private updateSelectionBoxes(): void {
        const points: number[] = [];

        const boxable = this.selectedRows.filter(
            (row) => row !== this.unboxedRow,
        );

        for (const root of boxRoots(boxable, this.context.rows.rowParents)) {
            const box = this.boxAround(root);

            if (box !== null) {
                appendBoxEdges(points, box);
            }
        }

        for (const partId of this.selectedParts) {
            const box = this.boxAroundPart(partId);

            if (box !== null) {
                appendBoxEdges(points, box);
            }
        }

        setPositions(this.selectionBoxes.geometry, points);
        this.selectionBoxes.visible = points.length > 0;
    }

    /**
     * A whole part's outline, in world space, or null when it holds nothing to enclose.
     *
     * Only what is DRAWN. `Box3.setFromObject` walks every descendant regardless of visibility, so
     * a hidden collision hull - which this panel hides by default and which is routinely larger
     * than the turret it belongs to - would push the outline out past the thing the reader sees.
     */
    private boxAroundPart(partId: string): THREE.Box3 | null {
        const part = this.context.parts.get(partId);

        if (part === undefined) {
            return null;
        }

        const box = new THREE.Box3();
        let found = false;

        part.root.updateWorldMatrix(true, true);
        part.root.traverseVisible((node) => {
            if ((node as THREE.Mesh).isMesh !== true) {
                return;
            }

            box.union(new THREE.Box3().setFromObject(node));
            found = true;
        });

        return found && !box.isEmpty() ? box : null;
    }

    /** What one box encloses, in world space, or null when the row owns nothing locatable. */
    private boxAround(root: string): THREE.Box3 | null {
        const box = new THREE.Box3();
        const hull = this.context.assembly.hull();
        let found = false;

        for (const row of rowsInBox(root, this.context.rows.rowParents)) {
            const target = this.context.rows.rowTargets.get(row);

            if (target?.mesh !== undefined) {
                // `setFromObject` walks the world matrices, which is what puts the box where an
                // animated mesh is NOW rather than where it was authored.
                box.union(new THREE.Box3().setFromObject(target.mesh));
                found = true;
            }

            const node =
                target?.boneIndex === undefined
                    ? undefined
                    : hull?.bonesByIndex.get(target.boneIndex);

            if (node !== undefined) {
                box.expandByPoint(node.getWorldPosition(new THREE.Vector3()));
                found = true;
            }
        }

        if (!found || box.isEmpty()) {
            return null;
        }

        // A bone with no geometry under it collapses to a point, and a box with no volume draws
        // nothing at all. Sized against the SUBJECT so the marker reads the same on a trooper and
        // on a command centre.
        const size = box.getSize(new THREE.Vector3());

        if (Math.max(size.x, size.y, size.z) < 1e-4) {
            box.expandByScalar(
                Math.max(
                    this.context.cameraRig.boundingSphere().radius * 0.02,
                    1e-3,
                ),
            );
        }

        return box;
    }

    /** Rebuilds the joint and line buffers from where the bones currently are. */
    private updateSkeletonGeometry(): void {
        const hull = this.context.assembly.hull();
        if (hull === null) {
            return;
        }

        const indexOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            indexOf.set(node, index);
        }

        const points: number[] = [];
        const lines: number[] = [];
        const colours: number[] = [];
        const world = new THREE.Vector3();
        const parentWorld = new THREE.Vector3();

        for (const [index, node] of hull.bonesByIndex) {
            node.getWorldPosition(world);
            points.push(world.x, world.y, world.z);

            const selected = this.selectedBones.has(index);
            colours.push(
                selected ? 1 : 0.44,
                selected ? 0.62 : 0.7,
                selected ? 0.16 : 1,
            );

            if (node.parent !== null && indexOf.has(node.parent)) {
                node.parent.getWorldPosition(parentWorld);
                lines.push(
                    parentWorld.x,
                    parentWorld.y,
                    parentWorld.z,
                    world.x,
                    world.y,
                    world.z,
                );
            }
        }

        setPositions(this.joints.geometry, points);
        this.joints.geometry.setAttribute(
            "color",
            new THREE.Float32BufferAttribute(colours, 3),
        );
        setPositions(this.jointLines.geometry, lines);
    }

    /**
     * Places the bone labels over the canvas.
     *
     * Nearest-first with a minimum pixel gap. A Star Destroyer has 71 bones and many project within a
     * few pixels of each other, so drawing every label turns the viewport into unreadable soup;
     * dropping the further of an overlapping pair keeps what is closest to the camera, which is what
     * the user is looking at.
     */
    private updateLabels(): void {
        const hull = this.context.assembly.hull();
        if (hull === null || !this.skeletonVisible) {
            this.clearLabels();
            return;
        }

        const wanted = labelCandidates(
            this.context.queries.skeleton(),
            this.labelMode,
            this.selectedBones,
        );
        if (wanted.size === 0) {
            this.clearLabels();
            return;
        }

        const width = this.context.canvas.clientWidth;
        const height = this.context.canvas.clientHeight;
        const world = new THREE.Vector3();

        const projected: {
            x: number;
            y: number;
            depth: number;
            text: string;
            selected: boolean;
        }[] = [];

        for (const [index, node] of hull.bonesByIndex) {
            if (!wanted.has(index)) {
                continue;
            }

            node.getWorldPosition(world);
            const ndc = world.clone().project(this.context.camera);

            // Behind the camera or off screen: projecting those puts labels on the wrong edge.
            if (
                ndc.z > 1 ||
                ndc.x < -1 ||
                ndc.x > 1 ||
                ndc.y < -1 ||
                ndc.y > 1
            ) {
                continue;
            }

            projected.push({
                x: (ndc.x * 0.5 + 0.5) * width,
                y: (-ndc.y * 0.5 + 0.5) * height,
                depth: ndc.z,
                text:
                    alamoBoneName(alamoNodeName(node))?.name ??
                    alamoNodeName(node),
                selected: this.selectedBones.has(index),
            });
        }

        projected.sort((a, b) => a.depth - b.depth);

        const placed: { x: number; y: number }[] = [];
        let used = 0;

        for (const label of projected) {
            // The selection always gets its label, whatever it overlaps: it is the one the user asked
            // about, and silently dropping it looks like the click did nothing.
            const crowded =
                !label.selected &&
                placed.some(
                    (p) =>
                        Math.abs(p.x - label.x) < LABEL_SPACING &&
                        Math.abs(p.y - label.y) < LABEL_SPACING,
                );

            if (crowded) {
                continue;
            }

            placed.push(label);

            const element = this.labelAt(used++);
            element.textContent = label.text;
            element.style.transform =
                "translate(" +
                Math.round(label.x) +
                "px, " +
                Math.round(label.y) +
                "px)";
            element.className = label.selected
                ? "bone-label selected"
                : "bone-label";
            element.style.display = "";
        }

        for (let i = used; i < this.labelPool.length; i++) {
            this.labelPool[i].style.display = "none";
        }
    }

    /**
     * Projects each mark and places its image over the canvas.
     *
     * DOM rather than scene geometry, exactly like the bone labels above. A reticle is HUD: the game
     * sizes it as a fraction of the SCREEN and draws it over the hull rather than in it, so a quad in
     * the scene would have to fight both perspective and the depth buffer to end up here anyway.
     *
     * Anchored on the middle of the attached model, not on the attach bone. The bone sits at the
     * hardpoint's base - a mark there hangs below the turret rather than on it - and what a player puts
     * their cursor over is the thing standing on the bone. A hardpoint with no model of its own has only
     * its bone, and that bone is the hull's.
     */
    private updateReticles(): void {
        const width = this.context.canvas.clientWidth;
        const height = this.context.canvas.clientHeight;
        const size = reticleSizePx(this.reticleScreenSize, width, height);
        const world = new THREE.Vector3();
        let used = 0;

        for (const mark of this.reticles) {
            const anchor = this.reticleAnchor(mark, world);
            if (anchor === null) {
                continue;
            }

            const ndc = RETICLE_NDC.copy(anchor).project(this.context.camera);

            // Behind the camera or off screen. Projecting those puts the mark on the wrong edge, and
            // a reticle in the corner of a ship you are looking past is worse than none.
            if (
                ndc.z > 1 ||
                ndc.x < -1 ||
                ndc.x > 1 ||
                ndc.y < -1 ||
                ndc.y > 1
            ) {
                continue;
            }

            const element = this.reticleAt(used++);
            const half = size / 2;

            element.style.width = `${size}px`;
            element.style.height = `${size}px`;
            element.style.transform =
                "translate(" +
                `${Math.round((ndc.x * 0.5 + 0.5) * width - half)}px, ` +
                `${Math.round((-ndc.y * 0.5 + 0.5) * height - half)}px)`;

            // MASKED rather than drawn, so the shape can be tinted by health. The game colours a
            // reticle bright green at full health through yellow and orange to red at nothing, and
            // an <img> cannot be recoloured - a mask plus a background can.
            const colour = healthColour(mark.healthFraction);

            // The tracked art while the pointer is on it, which is what the game does - and what
            // replaced a seven-way dropdown asking the reader to pick a state by hand.
            const art =
                this.hoveredReticle === mark.hardpointId
                    ? mark.trackedUri
                    : mark.iconUri;

            element.dataset.hardpoint = mark.hardpointId;

            if (element.dataset.icon !== art) {
                element.dataset.icon = art;
                element.style.maskImage = `url(${art})`;
                element.style.webkitMaskImage = `url(${art})`;
            }

            if (element.style.backgroundColor !== colour) {
                element.style.backgroundColor = colour;
            }

            element.title = `${mark.hardpointId}${mark.type === "" ? "" : ` - ${mark.type}`}`;
            element.style.display = "";
        }

        for (let i = used; i < this.reticlePool.length; i++) {
            this.reticlePool[i].style.display = "none";
        }
    }

    /**
     * Where one mark stands, in world space, or null when its bone is not loaded.
     *
     * The attachment BONE, and nothing else. Centring on the attached model's bounds was both wrong
     * - the game draws the mark on the bone - and slow: it asked three.js for a whole part's
     * bounding box per mark per tick, which walks every vertex of the geometry. Ten hardpoints at twelve
     * ticks a second made the viewport crawl.
     */
    private reticleAnchor(
        mark: ReticleMark,
        into: THREE.Vector3,
    ): THREE.Vector3 | null {
        if (mark.bone === null) {
            return null;
        }

        // Through `bonePosition`, which answers null for anything it cannot actually find.
        // Comparing against the model root caught only HALF of it: that is the fallback for a
        // missing PART, and a missing BONE falls back to the owning part's root instead - so a
        // mark whose bone was absent was drawn at that model's origin rather than dropped, and
        // every such mark piled up in the same place.
        const at = this.context.queries.bonePosition(mark.partId, mark.bone);

        return at === null ? null : into.set(at.x, at.y, at.z);
    }

    /** Pooled, like the labels: a moving camera must not churn the DOM every tick. */
    private reticleAt(index: number): HTMLElement {
        while (this.reticlePool.length <= index) {
            const element = document.createElement("div");
            element.className = "reticle-mark";

            // The marks are the one thing in the label layer that IS interactive: hovering shows
            // the tracked art the game would show under a cursor, and clicking picks the hardpoint.
            // The layer itself keeps `pointer-events: none` so nothing else steals the drag that
            // orbits the camera.
            element.style.pointerEvents = "auto";
            element.style.cursor = "pointer";

            element.addEventListener("pointerenter", () => {
                const id = element.dataset.hardpoint ?? "";
                this.hoveredReticle = id === "" ? null : id;
            });

            element.addEventListener("pointerleave", () => {
                this.hoveredReticle = null;
            });

            element.addEventListener("click", (event) => {
                event.stopPropagation();
                const id = element.dataset.hardpoint ?? "";

                if (id !== "") {
                    this.host.onReticleClicked?.(id);
                }
            });
            this.context.labelLayer.appendChild(element);
            this.reticlePool.push(element);
        }

        return this.reticlePool[index];
    }

    private clearReticles(): void {
        for (const element of this.reticlePool) {
            element.style.display = "none";
        }
    }

    /** Pooled, so a moving camera does not churn the DOM on every tick. */
    private labelAt(index: number): HTMLElement {
        while (this.labelPool.length <= index) {
            const element = document.createElement("div");
            element.className = "bone-label";
            this.context.labelLayer.appendChild(element);
            this.labelPool.push(element);
        }

        return this.labelPool[index];
    }

    private clearLabels(): void {
        for (const element of this.labelPool) {
            element.style.display = "none";
        }
    }

    /**
     * The hull's joint under the pointer, measured on screen, or null when none is near enough.
     *
     * Screen distance rather than a ray, as the turret knobs are picked: a joint is a dot the reader
     * aims a cursor at.
     */
    jointAt(event: PointerEvent): number | null {
        const hull = this.context.assembly.hull();
        if (hull === null) {
            return null;
        }

        const rect = this.context.canvas.getBoundingClientRect();
        const x = event.clientX - rect.left;
        const y = event.clientY - rect.top;

        let best: { index: number; distance: number } | null = null;
        const world = new THREE.Vector3();

        for (const [index, node] of hull.bonesByIndex) {
            node.getWorldPosition(world);
            const ndc = world.project(this.context.camera);
            if (ndc.z > 1) {
                continue;
            }

            const distance = Math.hypot(
                (ndc.x * 0.5 + 0.5) * rect.width - x,
                (-ndc.y * 0.5 + 0.5) * rect.height - y,
            );

            if (best === null || distance < best.distance) {
                best = { index, distance };
            }
        }

        // Only a deliberate click on a joint counts. Without a threshold, every drag of the camera
        // would also reselect whichever bone happened to be nearest the cursor.

        return best !== null && best.distance <= JOINT_PICK_RADIUS
            ? best.index
            : null;
    }
}
