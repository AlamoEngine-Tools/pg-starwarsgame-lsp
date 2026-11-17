// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's parts as an assembly: a loaded GLB turned into a part and hung on the bone it attaches
// to (or held until that bone arrives), hidden, shown and removed again, the hull and each subject's parts,
// and the walks over their meshes every other layer uses. It reads the loader, the scene roots and the other
// layers it hands a new part to; nothing else.

import { type AttachmentRequest, attachmentProblem } from "./attachments";
import * as THREE from "three";
import {
    ACTIVE_SUBJECT,
    BREAKOFF_ATTACHMENT,
    type LoadedPart,
} from "./loadedPart";
import { alamoBoneName, alamoNodeName } from "./skeleton";
import { type MaterialExtras, resolveMaterial, isVisibleAt } from "./materials";
import {
    splitGeometryFromBone,
    cancelRootCorrection,
    stampTreeKeys,
    outermostRoots,
} from "./boneNodes";
import { billboardTypeOf } from "./billboards";
import { visibilityTracks } from "./boneVisibility";
import { isOverlay } from "./modelBounds";
import { setMeshDrawn } from "./meshVisibility";
import { type EnvironmentRig } from "./environmentRig";
import { type GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";
import { type MaterialBinder } from "./materialBinder";
import { type ParticleHost } from "./particleHost";
import { type NormalisedColour } from "./colour";
import { type SubjectAnimation } from "./subjectAnimation";
import { type AddPartOptions } from "./viewport";

/** Every mesh a model's own materials say is not drawn - collision hulls, shadow volumes. */
function hiddenMeshes(root: THREE.Object3D): Set<THREE.Object3D> {
    const hidden = new Set<THREE.Object3D>();

    root.traverse((node) => {
        if (
            node instanceof THREE.Mesh &&
            resolveMaterial((node.userData.alamo ?? {}) as MaterialExtras)
                .hidden
        ) {
            hidden.add(node);
        }
    });

    return hidden;
}

function decodeBase64(value: string): ArrayBuffer {
    const binary = atob(value);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
}

/** What PartAssembly reads from the viewport it belongs to. */
/** Releases GPU memory for a subtree; the renderer does not do it on removal. */
export function disposeTree(root: THREE.Object3D): void {
    root.traverse((node) => {
        if (!(node instanceof THREE.Mesh)) {
            return;
        }

        node.geometry.dispose();
        for (const material of Array.isArray(node.material)
            ? node.material
            : [node.material]) {
            material.dispose();
        }
    });
}

/** What PartAssembly reads from the viewport it belongs to. */
export interface AssemblyViewport {
    readonly alt: number;
    applyLevels(): void;
    readonly billboards: THREE.Object3D[];
    bindLoadedTextures(root: THREE.Object3D): void;
    dropRegistrations(subjectId: string, root: THREE.Object3D): void;
    readonly environment: EnvironmentRig;
    readonly loader: GLTFLoader;
    readonly lod: number;
    readonly materials: MaterialBinder;
    readonly modelRoot: THREE.Group;
    readonly particles: ParticleHost;
    readonly parts: Map<string, LoadedPart>;
    removeParticleSystem(id: string, immediate?: boolean): void;
    readonly scaledRoot: THREE.Group;
    setColorization(colour: NormalisedColour | null): void;
    subjectAnimation(subjectId: string): SubjectAnimation;
    readonly subjects: Map<string, SubjectAnimation>;
}

export class PartAssembly {
    /** Every named attachment a caller has asked for, so {@link unresolvedAttachments} can check. */
    readonly attachRequests = new Map<string, AttachmentRequest>();
    /** Parts broken off by a destroyed hardpoint. */
    readonly hiddenParts = new Set<string>();

    constructor(private readonly host: AssemblyViewport) {}

    /**
     * Adds a model, attached to a bone of an already-loaded part when asked.
     *
     * Attaching to the bone's Object3D rather than copying its transform is what makes an attached
     * turret follow the hull's animation for free.
     *
     * THE one loader. Every model in the scene comes through here - the hull, its hardpoint
     * models, a death clone, a piece of wreckage - because the alternative was measured and it is
     * a parallel pipeline that converges on this one a bug at a time. `addBreakoff` had hand
     * copies of six of the steps below, four of them added on a single day off user reports, and
     * was still missing nine others; the wreck cast no stencil shadow, had no tree keys, ignored
     * the detail levels and could not carry a clip. What differs between one model and the next is
     * which SUBJECT it belongs to, which is {@link AddPartOptions}, not which loader it uses.
     *
     * Hands back the root it built, for a caller that has its own state to keep beside it.
     */
    async addPart(
        id: string,
        glbBase64: string,
        attachToPartId?: string,
        attachBone?: string,
        options: AddPartOptions = {},
    ): Promise<THREE.Object3D> {
        const subjectId = options.subjectId ?? ACTIVE_SUBJECT;

        // Its own file's word decides how it is drawn, because the row chain speaks only for the
        // active subject and will never speak for this one.
        const passive = subjectId !== ACTIVE_SUBJECT;

        const gltf = await this.host.loader.parseAsync(
            decodeBase64(glbBase64),
            "",
        );
        const root = gltf.scene;

        const bones = new Map<string, THREE.Object3D>();
        const bonesByIndex = new Map<number, THREE.Object3D>();

        // Pass one: who owns each bone index.
        //
        // `<name>#<n>` is TWO namespaces sharing one syntax: on a bone node `n` is the bone index,
        // on a mesh node of its own it is the SUB-MESH index. Neither exclusion works - a rigid
        // mesh is attached to the bone's own node, so that node really is both, while a skinned or
        // collision mesh gets a node of its own that is not a bone at all.
        //
        // So it is settled by PRECEDENCE: a node without geometry always wins the index, and one
        // with geometry only takes it if nothing better claims it. Letting a mesh overwrite a bone
        // made `Ei_trooper` report its root as `Storm_Trooper_LOD2` when bone 0 is `Root`, and
        // every bone count, parent and attachment was then read off the wrong object.
        root.traverse((node) => {
            const parsed = alamoBoneName(alamoNodeName(node));
            if (parsed === null) {
                return;
            }

            const held = bonesByIndex.get(parsed.index);
            const better =
                held === undefined ||
                (held instanceof THREE.Mesh && !(node instanceof THREE.Mesh));

            if (better) {
                bonesByIndex.set(parsed.index, node);

                const key = parsed.name.toLowerCase();
                if (!bones.has(key) || bones.get(key) === held) {
                    bones.set(key, node);
                }
            }
        });

        // Pass two: no bone node carries geometry from here on.
        //
        // Only the nodes that actually WON an index are split, which is why this waits for the
        // precedence pass - a skinned mesh's name parses as a bone name too (`Storm_Trooper_LOD1#0`
        // claims bone 0) and splitting one of those would manufacture a second node competing for
        // an index the real `Root` already holds. See `boneNodes.ts` for what the split buys.
        for (const [index, node] of [...bonesByIndex]) {
            if (!(node instanceof THREE.Mesh)) {
                continue;
            }

            // Off the MATERIAL, which is where the exporter puts them and where
            // `MaterialBinder.applyMaterial` reads them from - that has not run yet, so
            // `node.userData.alamo` is still empty.
            const source = Array.isArray(node.material)
                ? node.material[0]
                : node.material;
            const extras = (node.userData.alamo ??
                source?.userData ??
                {}) as MaterialExtras;
            const bone = splitGeometryFromBone(node, extras.alamoMesh);

            bonesByIndex.set(index, bone);
            for (const [key, held] of bones) {
                if (held === node) {
                    bones.set(key, bone);
                }
            }
        }

        // Pass three: materials, over the graph as it now stands.
        root.traverse((node) => {
            if (node instanceof THREE.Mesh) {
                this.host.materials.applyMaterial(node);
            }
        });

        // A posed part is placed under the model root at a world transform it captured itself -
        // and stays OUTSIDE the scale, because a wreck is its own object with its own scale and
        // its drift is a world velocity. An unattached part of the ACTIVE subject is the hull,
        // which is the geometry the scale is for; an attached one inherits it through the bone it
        // hangs on, which is right because a hardpoint declares no scale of its own - 0 shipped
        // hardpoints do.
        //
        // An unattached PASSIVE part - a death clone - is neither. It is spawned as its own object,
        // so it goes beside the scaled geometry and carries its OWN scale. It used to fall through
        // to the hull's branch and take the SHIP's, which is wrong for 59 of 265 shipped pairs.
        const parent =
            options.pose !== undefined
                ? this.host.modelRoot
                : attachToPartId !== undefined
                  ? this.attachmentFor(attachToPartId, attachBone)
                  : passive
                    ? this.host.modelRoot
                    : this.host.scaledRoot;

        if (
            options.scale !== undefined &&
            Number.isFinite(options.scale) &&
            options.scale > 0
        ) {
            root.scale.setScalar(options.scale);
        }

        // A part attached to a BONE of another part is already inside that part's Z-up-to-Y-up
        // correction, so its own copy would apply the rotation twice - see `cancelRootCorrection`.
        // The hull goes under the scaled geometry node, which - like the model root above it - is
        // uncorrected, and keeps its own.
        //
        // A posed part is in the same position for the same reason: the world transform it copies
        // ALREADY contains the correction of the part it was taken from, so its own copy is the
        // identical mirrored 90-degree roll an attached hardpoint used to have.
        if (attachToPartId !== undefined || options.pose !== undefined) {
            cancelRootCorrection(root);
        }

        parent.add(root);

        if (options.pose !== undefined) {
            root.position.copy(options.pose.position);
            root.quaternion.copy(options.pose.quaternion);
        }

        // A key per mesh that survives the file being opened again, which the three.js uuid it
        // replaces did not - see `stampTreeKeys`.
        stampTreeKeys(root, bonesByIndex, id);

        this.host.parts.set(id, { id, root, bones, bonesByIndex, subjectId });

        // Its own file's word, because the row chain will never speak for it - see
        // `LoadedPart.subjectId`. Debris needs exactly this too: outside the chain, nothing else
        // hides its collision hull and its shadow volume, and it dropped showing both. Applied
        // again on every level change, from `applyLevels`.
        if (passive) {
            this.applyOwnFileVisibility(subjectId, root);

            // The host answers a texture name ONCE, and a wreck is the same ship - so every map it
            // needs was already sent for the hull and its own request comes back silent.
            this.host.bindLoadedTextures(root);
        }

        // AFTER the part is registered, never before: `applyWireframe` sweeps `this.parts`, so
        // running it a few lines earlier - where the materials are built - swept everything except
        // the part that had just been given them, and a turret arriving late came in solid while
        // the hull beside it was edges.
        this.host.materials.applyWireframe();

        // And the render state every already-translated effect declares. A material built now gets
        // three's DEFAULTS, not the effect's - and `RSkinBumpColorize.fx` declares its cull mode
        // there, so a part arriving after its shader had was drawn with every triangle facing away
        // and rendered NOTHING. That is what made a death clone invisible with shaders switched on
        // and perfect with them off. `addBreakoff` has always done this; `addPart` never did.
        for (const [shader, state] of this.host.materials.shaderStates) {
            this.host.materials.applyStoredShaderState(shader, state);
        }

        if (this.host.materials.colorization !== null) {
            this.host.setColorization(this.host.materials.colorization);
        }
        this.host.environment.collectShadowVolumes(root);

        // The handful of bones that turn to face something. Collected once per part rather than
        // searched for each frame: across the shipped models 268 bones out of 22866 declare a mode
        // at all, so a per-frame traversal would be almost entirely wasted.
        root.traverse((node) => {
            if (billboardTypeOf(node) !== null) {
                this.host.billboards.push(node);
            }
        });

        // Geometry can arrive after its hardpoint was destroyed; a part loading late must not
        // reappear intact.
        root.visible = !this.hiddenParts.has(id);

        // Everything the animation needs goes to THIS PART'S SUBJECT, never to the scene. One path
        // for the hull, an attached turret, a death clone and a piece of wreckage alike - what used
        // to differ between them was only which of these maps they were allowed into.
        const subject = this.host.subjectAnimation(subjectId);

        // Before any clip can touch it. Recorded for the whole part, not only for the bones this
        // GLB's own clips drive - a later part of the same subject can reach in here too.
        root.traverse((node) => {
            if (!subject.restPose.has(node)) {
                subject.restPose.set(node, {
                    position: node.position.clone(),
                    quaternion: node.quaternion.clone(),
                    scale: node.scale.clone(),
                });
            }

            // First wins. A visibility track names a bone, and a bone name is unique within one
            // model because the exporter suffixes it with the bone index. Keyed by the name the
            // exporter wrote, which is the one a track names - three's sanitised copy is not.
            const name = alamoNodeName(node);
            if (name !== "" && !subject.nodeByName.has(name)) {
                subject.nodeByName.set(name, node);
            }
        });

        // What its own file says is not drawn - a collision hull, a shadow volume. Held so a clip
        // can never bring one back: a shadow volume is not geometry that happens to be switched off
        // this frame.
        for (const mesh of hiddenMeshes(root)) {
            subject.fileHidden.add(mesh);
        }

        if (gltf.animations.length > 0) {
            // Rooted at the first of this subject's parts to bring clips. A clip binds to nodes BY
            // NAME from the root it was given, so a mixer rooted anywhere else drives the wrong
            // model - which is exactly what a clone's `_die` did on the scene-wide mixer.
            subject.mixer ??= new THREE.AnimationMixer(root);
            subject.clips = [...subject.clips, ...gltf.animations];

            // Off the raw glTF JSON: three's loader builds AnimationClips and does not carry an
            // animation's extras onto them, so the parser's own document is the only source.
            for (const [name, track] of visibilityTracks(gltf.parser.json)) {
                subject.visibility.set(name, track);
            }
        }

        this.host.applyLevels();

        return root;
    }

    /**
     * Every named attachment that still does not resolve, in words.
     *
     * RECOMPUTED rather than logged at the moment of the miss, because most misses are temporary:
     * geometry arrives asynchronously and a hardpoint routinely asks for its bone before the hull
     * carrying it has landed. A request that resolves later simply stops being reported, so this
     * can be read at any time without a stale backlog.
     */
    unresolvedAttachments(): string[] {
        const problems: string[] = [];

        for (const request of this.attachRequests.values()) {
            const parent = this.host.parts.get(request.partId);
            const found = {
                part: parent !== undefined,
                bone:
                    request.bone === undefined || parent === undefined
                        ? false
                        : (request.boneIndex !== undefined &&
                              parent.bonesByIndex.has(request.boneIndex)) ||
                          parent.bones.has(request.bone.toLowerCase()),
            };

            const problem = attachmentProblem(request, found);
            if (problem !== null && !problems.includes(problem)) {
                problems.push(problem);
            }
        }

        return problems;
    }

    /** Breaks an attached part off, or puts it back. */
    setPartHidden(partId: string, hidden: boolean): void {
        if (hidden) {
            this.hiddenParts.add(partId);
        } else {
            this.hiddenParts.delete(partId);
        }

        const part = this.host.parts.get(partId);
        if (part !== undefined) {
            part.root.visible = !hidden;
        }

        this.host.particles.holdEffectsOf(partId);
    }

    /**
     * Removes one loaded part and frees its geometry.
     *
     * For parts that come and go with a STATE rather than with the subject - a death clone appears
     * when the ship dies and goes again when it is repaired. A scene part is never removed this
     * way; it lives as long as the subject does and is hidden instead.
     */
    removePart(partId: string): void {
        const part = this.host.parts.get(partId);
        if (part === undefined) {
            return;
        }

        // Its effects first, and through the ordinary route so `leaveParticles` still has its say.
        // They no longer live inside the part's subtree, so nothing else would ever take them:
        // before, disposing the part quietly freed their buffers underneath them, which left every
        // entry in `particleSystems` still being stepped against disposed geometry.
        for (const [id, entry] of [...this.host.particles.particleSystems]) {
            if (entry.attachToPartId === partId) {
                this.host.removeParticleSystem(id);
            }
        }

        this.host.dropRegistrations(part.subjectId, part.root);
        part.root.removeFromParent();
        disposeTree(part.root);
        this.host.parts.delete(partId);
        this.hiddenParts.delete(partId);

        // The subject goes with its last part. A repair removes the wreck, and a mixer left behind
        // would keep ticking a clip whose nodes have been disposed. The ACTIVE subject stays
        // whatever happens - it is the scene, and only `clear` ends it.
        if (
            part.subjectId !== ACTIVE_SUBJECT &&
            this.rootPartOf(part.subjectId) === null
        ) {
            this.host.subjects.delete(part.subjectId);
        }
    }

    /** Whether a part's geometry has arrived, so something can be attached to its bones. */
    hasPart(partId: string): boolean {
        return this.host.parts.has(partId);
    }

    /** The object a part is attached to: a named bone of another part, or the scene root. */
    attachmentFor(
        partId?: string,
        bone?: string,
        boneIndex?: number,
    ): THREE.Object3D {
        if (partId === undefined) {
            return this.host.modelRoot;
        }

        this.noteAttachment(partId, bone, boneIndex);

        const parent = this.host.parts.get(partId);
        if (parent === undefined) {
            return this.host.modelRoot;
        }

        if (bone === undefined) {
            return parent.root;
        }

        // By INDEX where the caller has one, because bone names repeat: Boba Fett's two jetpack
        // jets are two bones both called `p_boba_jetpack`, and a name-keyed map holds one of them -
        // so both effects were attached to the same jet and he fired from one.
        const byIndex =
            boneIndex === undefined
                ? undefined
                : parent.bonesByIndex.get(boneIndex);

        // A hardpoint naming a bone the hull does not have is already reported as a problem by the
        // server; here it just means the part sits at the hull's origin rather than vanishing.
        const anchor =
            byIndex ?? parent.bones.get(bone.toLowerCase()) ?? parent.root;

        return this.mountable(anchor);
    }

    /**
     * An anchor a mount cannot be hidden BY.
     *
     * Reported: two of the Gargantuan's turrets never drew, and the model tree showed why - the
     * whole attached model, collision mesh and all, sat as CHILDREN of a hull node that was itself a
     * hidden mesh. Turning that row on brought the turret with it. Three prunes a hidden subtree, so
     * anything mounted on a hidden node is gone whatever its own state says.
     *
     * `splitGeometryFromBone` exists to stop that - after it a bone node never carries geometry, so
     * hiding the geometry cannot hide the mount. But the split only runs over nodes that WON a bone
     * index during load, and a hull whose exporter emitted a separate mesh node for the same name
     * leaves a Mesh in the bone map. That is what these two hardpoints found.
     *
     * So the split is applied HERE too, at the moment something is about to hang on the node. It is
     * idempotent: a node that is not a Mesh is already what it needs to be.
     *
     * The engine agrees from its own side - an attached thing does not inherit its host's
     * visibility. Hiding a bone's geometry is a statement about GEOMETRY.
     */
    private mountable(anchor: THREE.Object3D): THREE.Object3D {
        if (!(anchor instanceof THREE.Mesh)) {
            return anchor;
        }

        const extras = anchor.userData.alamo as MaterialExtras | undefined;
        const bone = splitGeometryFromBone(anchor, extras?.alamoMesh);

        // The maps still point at the mesh. Both are re-pointed, or the next lookup hands out the
        // node that was just taken apart - and the mount would hang on it again.
        for (const part of this.host.parts.values()) {
            for (const [key, held] of part.bones) {
                if (held === anchor) {
                    part.bones.set(key, bone);
                }
            }

            for (const [index, held] of part.bonesByIndex) {
                if (held === anchor) {
                    part.bonesByIndex.set(index, bone);
                }
            }
        }

        return bone;
    }

    /**
     * Remembers that something asked to attach to this bone.
     *
     * Separate from resolving it, because a caller can legitimately refuse to place anything - see
     * {@link bonePosition} - and its request still has to be reported. Both of `attachmentFor`'s
     * fallbacks draw something recoverable and say nothing, and two separate bugs this session hid
     * behind them: fire cones resolving on the wrong model, reticles piling up at the origin.
     *
     * Breakoff roots are left out on purpose - a wreck is meant to stop existing, so a request for
     * one that has been cleared is not a fault.
     */
    noteAttachment(partId: string, bone?: string, boneIndex?: number): void {
        if (partId.startsWith(BREAKOFF_ATTACHMENT)) {
            return;
        }

        this.attachRequests.set(
            `${partId} / ${bone ?? ""} / ${boneIndex ?? ""}`,
            { partId, bone, boneIndex },
        );
    }

    /**
     * Every mesh belonging to the ACTIVE subject.
     *
     * Not `modelRoot.traverse`: particle systems are parented under the same root - under a bone,
     * so their smoke follows the hull as it animates - so a blanket traversal reaches their
     * geometry too. Handing a particle billboard a model material replaces the material that makes
     * it a particle at all, which is exactly what happened when the shader toggle rebuilt
     * everything under `modelRoot`.
     */
    forEachPartMesh(visit: (mesh: THREE.Mesh) => void): void {
        // Passive subjects are left out: this is what the row chain, the tree and the subject's
        // stats are built on, and a death clone is not part of the subject it replaces, any more
        // than a piece of wreckage is. See `LoadedPart.subjectId`.
        this.walkModelMeshes(this.activeSubjectRoots(), visit);
    }

    /** The roots of the subject that opened the scene. */
    activeSubjectRoots(): THREE.Object3D[] {
        return [...this.host.parts.values()]
            .filter((part) => part.subjectId === ACTIVE_SUBJECT)
            .map((part) => part.root);
    }

    /**
     * Every mesh that is DRAWN as model geometry, every subject's.
     *
     * The separation matters in both directions. A passive subject has no tree rows and no place
     * in a coverage count, so the queries that DESCRIBE the subject stay on
     * {@link forEachPartMesh}. But it is drawn by the same shaders as the hull it fell off, so
     * everything that decides HOW a mesh draws has to reach it - and for wreckage none of it did.
     *
     * That is what drew a wreck's `MeshAdditive` light strip as a black slab: `applyShaderState`
     * puts an effect's declared blend, depth and cull onto the materials using it, a translated
     * material gets its blend from nowhere else, and the sweep walked `parts` - which a breakoff
     * root was deliberately not in. The wireframe, the team tint and the shadow-casting decision
     * were all missing it for the same reason. Wreckage IS a part now, so this is simply every
     * one of them.
     */
    forEachDrawnMesh(visit: (mesh: THREE.Mesh) => void): void {
        // Every subject: how a mesh draws - its blend, its wireframe, its team tint - is the same
        // question whoever owns it. Only WHETHER it draws differs.
        this.walkModelMeshes(
            [...this.host.parts.values()].map((part) => part.root),
            visit,
        );
    }

    private walkModelMeshes(
        roots: readonly THREE.Object3D[],
        visit: (mesh: THREE.Mesh) => void,
    ): void {
        // A manual walk rather than `traverse`, because this has to PRUNE. A particle system is
        // parented to a BONE so its smoke follows the hull as it animates - which puts it inside the
        // part's own root, where `traverse` still finds it. Scoping to the part roots was not enough
        // on its own: the billboards were still being handed model materials, and still turning up
        // in the mesh list as if they were geometry.
        const walk = (node: THREE.Object3D): void => {
            if (node.userData.aetParticleSystem === true || isOverlay(node)) {
                return;
            }

            // The stencil volumes' counting meshes are this class's own scaffolding, not the
            // model's geometry. Left in, they took model materials, turned up as phantom tree rows
            // and were swept by the wireframe - the same shape of bug the particle prune fixes.
            if (
                node instanceof THREE.Mesh &&
                node.userData.aetShadowVolume !== true
            ) {
                visit(node);
            }

            for (const child of node.children) {
                walk(child);
            }
        };

        // Only the OUTERMOST roots. A hardpoint's model is attached to a bone of the hull, so it is
        // already inside the hull's root - walking every part in turn visited each attached mesh
        // twice. See `outermostRoots`.
        for (const root of outermostRoots(roots)) {
            walk(root);
        }
    }

    /** Where the climb for an emission mesh has to stop: the model the system was attached to. */
    partRootOf(partId?: string): THREE.Object3D | null {
        return partId === undefined
            ? this.host.modelRoot
            : (this.host.parts.get(partId)?.root ?? null);
    }

    /**
     * The part the skeleton view describes: the ACTIVE subject's root.
     *
     * Everything that describes "the model" - the tree, the stats, the skeleton, the row chain, the
     * bone labels, the camera bindings - asks for this one. It used to be the first part in the
     * scene, full stop, which was the hull only by the luck of load order; now it is the first part
     * OF THE SUBJECT, and a wreck or a clone arriving cannot be mistaken for it.
     */
    hull(): LoadedPart | null {
        return this.rootPartOf(ACTIVE_SUBJECT);
    }

    /** The first-loaded part of one subject - its hull. */
    private rootPartOf(subjectId: string): LoadedPart | null {
        for (const part of this.host.parts.values()) {
            if (part.subjectId === subjectId) {
                return part;
            }
        }

        return null;
    }

    /** Every part belonging to one subject. */
    partsOf(subjectId: string): LoadedPart[] {
        return [...this.host.parts.values()].filter(
            (part) => part.subjectId === subjectId,
        );
    }

    /**
     * Draws a passive subject's geometry by its OWN file's rules, at the current levels.
     *
     * The row chain speaks only for the ACTIVE subject, so this is the whole of what decides
     * whether a wreck's mesh is on screen. It runs when the geometry loads AND whenever the levels
     * change: applied once and never again, a death clone kept whatever detail level happened to be
     * set at the moment it arrived, and moving the control afterwards left it behind. Measured over
     * the shipped tree that is one ship - the Broadside Class Cruiser, whose clone carries a `_LOD1`
     * mesh and whose hull declares the levels that make the control live - but the engine plainly
     * supports it and a mod authoring an ALT-tagged wreck is exactly who would be confused.
     *
     * Through {@link setMeshDrawn}, never `visible`: a mesh node is very often a bone node too, and
     * `visible` prunes the whole subtree under it. See `meshVisibility.ts`.
     *
     * A node the RUNNING CLIP names is left alone. The clip is the model's own word about this
     * frame and it sits above the level gate, which is the same precedence `resolveRow` gives the
     * active subject.
     */
    applyOwnFileVisibility(subjectId: string, root: THREE.Object3D): void {
        const subject = this.host.subjectAnimation(subjectId);
        const action = subject.action;
        const track =
            action === null
                ? undefined
                : subject.visibility.get(action.getClip().name);

        root.traverse((node) => {
            if (
                !(node instanceof THREE.Mesh) ||
                node.userData.aetShadowVolume === true
            ) {
                return;
            }

            if (track?.bones.has(alamoNodeName(node)) === true) {
                return;
            }

            const extras = (node.userData.alamo ?? {}) as MaterialExtras;

            setMeshDrawn(
                node,
                !resolveMaterial(extras).hidden &&
                    isVisibleAt(extras, this.host.alt, this.host.lod),
            );
        });
    }
}
