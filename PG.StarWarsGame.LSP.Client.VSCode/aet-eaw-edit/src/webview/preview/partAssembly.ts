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
import { type MaterialExtras, resolveMaterial } from "./materials";
import { clipsSay, poseSays } from "./boneVisibility";
import { runningClips } from "./clipEnd";
import { skinVisibilityBone } from "./skinBone";
import {
    type RowFacts,
    castsShadow,
    meshSwitches,
    resolveRow,
} from "./visibility";
import {
    splitGeometryFromBone,
    cancelRootCorrection,
    stampTreeKeys,
    outermostRoots,
} from "./boneNodes";
import { isOverlay } from "./modelBounds";
import { setMeshDrawn } from "./meshVisibility";
import { type AddPartOptions } from "./viewportTypes";
import { type ViewportContext } from "./viewportContext";
import { bufferOf } from "./payloadBytes";

/**
 * The bone whose track speaks for a mesh: for a skinned mesh the bone of `skinVisibilityBone`, else
 * the nearest of the part's bone nodes above it. Undefined for a mesh on no bone of the part.
 */
function ownBoneOf(
    mesh: THREE.Mesh,
    part: LoadedPart,
    boneNodes: ReadonlySet<THREE.Object3D>,
): THREE.Object3D | undefined {
    if ((mesh as THREE.SkinnedMesh).isSkinnedMesh === true) {
        return skinVisibilityBone(mesh, part.root, part.bonesByIndex.get(0));
    }

    for (let at = mesh.parent; at !== null; at = at.parent) {
        if (boneNodes.has(at)) {
            return at;
        }

        if (at === part.root) {
            break;
        }
    }

    return undefined;
}

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

export class PartAssembly {
    /** Every named attachment a caller has asked for, so {@link unresolvedAttachments} can check. */
    private readonly attachRequests = new Map<string, AttachmentRequest>();
    /** Parts broken off by a destroyed hardpoint. */
    readonly hiddenParts = new Set<string>();
    /**
     * Parts that hang on a part not loaded yet, by the part they hang on: the bone each wants, kept
     * out of the scene until that part arrives.
     *
     * Models arrive as bytes in whatever order the server answers, and a small turret model routinely
     * lands before the hull it hangs on. Put at the model root instead, it sat inside the hull at the
     * origin - with the hull's axis correction already taken off, so rotated down - and nothing moved
     * it once the hull came. Out of the scene it draws nothing until it can be drawn where it belongs.
     */
    private readonly waiting = new Map<
        string,
        { id: string; bone: string | undefined }[]
    >();

    constructor(private readonly context: ViewportContext) {}

    /** Takes every part out of the scene and releases its GPU memory. The map still names them. */
    disposeParts(): void {
        for (const part of this.context.parts.values()) {
            part.root.removeFromParent();
            disposeTree(part.root);
        }
    }

    /** Forgets every part, the hidden ones and the attachments asked for, for a new subject. */
    forgetParts(): void {
        this.context.parts.clear();
        this.hiddenParts.clear();
        this.waiting.clear();
        // A new subject asks for its own bones. Keeping the old subject's requests would report a
        // Star Destroyer's missing hardpoint against a Mon Calamari Cruiser for the rest of the session.
        this.attachRequests.clear();
    }

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
        glb: Uint8Array,
        attachToPartId?: string,
        attachBone?: string,
        options: AddPartOptions = {},
    ): Promise<THREE.Object3D> {
        const subjectId = options.subjectId ?? ACTIVE_SUBJECT;

        // Its own file's word decides how it is drawn, because the row chain speaks only for the
        // active subject and will never speak for this one.
        const passive = subjectId !== ACTIVE_SUBJECT;

        const gltf = await this.context.loader.parseAsync(bufferOf(glb), "");
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
                this.context.materials.applyMaterial(node);
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
        //
        // An attached part whose host has not arrived hangs nowhere yet - see `waiting`.
        const waitsForHost =
            options.pose === undefined &&
            attachToPartId !== undefined &&
            !this.context.parts.has(attachToPartId);
        const parent =
            options.pose !== undefined
                ? this.context.modelRoot
                : attachToPartId !== undefined
                  ? this.attachmentFor(attachToPartId, attachBone)
                  : passive
                    ? this.context.modelRoot
                    : this.context.scaledRoot;

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

        if (waitsForHost) {
            const host = attachToPartId ?? "";
            this.waiting.set(host, [
                ...(this.waiting.get(host) ?? []),
                { id, bone: attachBone },
            ]);
        } else {
            parent.add(root);
        }

        if (options.pose !== undefined) {
            root.position.copy(options.pose.position);
            root.quaternion.copy(options.pose.quaternion);
        }

        // A key per mesh that survives the file being opened again, which the three.js uuid it
        // replaces did not - see `stampTreeKeys`.
        stampTreeKeys(root, bonesByIndex, id);

        const part: LoadedPart = {
            id,
            root,
            bones,
            bonesByIndex,
            subjectId,
            attached:
                attachToPartId !== undefined || options.pose !== undefined,
        };
        this.context.parts.set(id, part);

        // Whatever was waiting for THIS part can hang on its bones now.
        this.mountWaitingOn(id);

        // Its own file's word, because the row chain will never speak for it - see
        // `LoadedPart.subjectId`. Debris needs exactly this too: outside the chain, nothing else
        // hides its collision hull and its shadow volume, and it dropped showing both. Applied
        // again on every level change, from `applyLevels`.
        if (passive) {
            this.applyOwnFileVisibility(part);

            // The host answers a texture name ONCE, and a wreck is the same ship - so every map it
            // needs was already sent for the hull and its own request comes back silent.
            this.context.materials.bindLoadedTextures(root);
        }

        // AFTER the part is registered, never before: `applyWireframe` sweeps the parts, so
        // running it a few lines earlier - where the materials are built - swept everything except
        // the part that had just been given them, and a turret arriving late came in solid while
        // the hull beside it was edges.
        this.context.materials.applyWireframe();

        // And the render state every already-translated effect declares. A material built now gets
        // three's DEFAULTS, not the effect's - and `RSkinBumpColorize.fx` declares its cull mode
        // there, so a part arriving after its shader had was drawn with every triangle facing away
        // and rendered NOTHING. That is what made a death clone invisible with shaders switched on
        // and perfect with them off. `addBreakoff` has always done this; `addPart` never did.
        this.context.materials.applyStoredState();
        this.context.environment.collectShadowVolumes(root);

        // The handful of bones that turn to face something. Collected once per part rather than
        // searched for each frame: across the shipped models 268 bones out of 22866 declare a mode
        // at all, so a per-frame traversal would be almost entirely wasted.
        this.context.cameraRig.collectBillboards(root);

        // Geometry can arrive after its hardpoint was destroyed; a part loading late must not
        // reappear intact.
        root.visible = !this.hiddenParts.has(id);

        // Everything the animation needs goes to THIS PART'S SUBJECT, never to the scene. One path
        // for the hull, an attached turret, a death clone and a piece of wreckage alike - what used
        // to differ between them was only which of these maps they were allowed into.
        this.context.animation.registerPart(subjectId, root, gltf);

        this.context.levels.applyLevels();

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
            const parent = this.context.parts.get(request.partId);
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

        const part = this.context.parts.get(partId);
        if (part !== undefined) {
            part.root.visible = !hidden;
        }

        this.context.particles.holdEffectsOf(partId);
    }

    /**
     * Removes one loaded part and frees its geometry.
     *
     * For parts that come and go with a STATE rather than with the subject - a death clone appears
     * when the ship dies and goes again when it is repaired. A scene part is never removed this
     * way; it lives as long as the subject does and is hidden instead.
     */
    removePart(partId: string): void {
        const part = this.context.parts.get(partId);
        if (part === undefined) {
            return;
        }

        // Its effects first, and through the ordinary route so `leaveParticles` still has its say.
        // They no longer live inside the part's subtree, so nothing else would ever take them:
        // before, disposing the part quietly freed their buffers underneath them, which left every
        // entry in `particleSystems` still being stepped against disposed geometry.
        for (const [id, entry] of [...this.context.particles.particleSystems]) {
            if (entry.attachToPartId === partId) {
                this.context.particles.removeParticleSystem(id);
            }
        }

        this.dropRegistrations(part.subjectId, part.root);
        part.root.removeFromParent();
        disposeTree(part.root);
        this.context.parts.delete(partId);
        this.hiddenParts.delete(partId);

        // Nor is it hung on its host later, if it was still waiting for one.
        for (const [host, waiting] of this.waiting) {
            this.waiting.set(
                host,
                waiting.filter((w) => w.id !== partId),
            );
        }

        // The subject goes with its last part. A repair removes the wreck, and a mixer left behind
        // would keep ticking a clip whose nodes have been disposed. The ACTIVE subject stays
        // whatever happens - it is the scene, and only `clear` ends it.
        if (
            part.subjectId !== ACTIVE_SUBJECT &&
            this.rootPartOf(part.subjectId) === null
        ) {
            this.context.animation.forgetSubject(part.subjectId);
        }
    }

    /** Whether a part's geometry has arrived, so something can be attached to its bones. */
    hasPart(partId: string): boolean {
        return this.context.parts.has(partId);
    }

    /** Hangs every part that was waiting for `hostId` on the bone it asked for - see `waiting`. */
    private mountWaitingOn(hostId: string): void {
        const waiting = this.waiting.get(hostId);
        if (waiting === undefined) {
            return;
        }

        this.waiting.delete(hostId);

        for (const { id, bone } of waiting) {
            const part = this.context.parts.get(id);

            if (part !== undefined) {
                this.attachmentFor(hostId, bone).add(part.root);
            }
        }
    }

    /** The object a part is attached to: a named bone of another part, or the scene root. */
    attachmentFor(
        partId?: string,
        bone?: string,
        boneIndex?: number,
    ): THREE.Object3D {
        if (partId === undefined) {
            return this.context.modelRoot;
        }

        this.noteAttachment(partId, bone, boneIndex);

        const parent = this.context.parts.get(partId);
        if (parent === undefined) {
            return this.context.modelRoot;
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
        for (const part of this.context.parts.values()) {
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
        return [...this.context.parts.values()]
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
            [...this.context.parts.values()].map((part) => part.root),
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
            ? this.context.modelRoot
            : (this.context.parts.get(partId)?.root ?? null);
    }

    /**
     * The part the skeleton view describes: the ACTIVE subject's root.
     *
     * Everything that describes "the model" - the tree, the stats, the skeleton, the row chain, the
     * bone labels, the camera bindings - asks for this one. It used to be the first part in the
     * scene, full stop, which was the hull only by the luck of load order; now it is the first part
     * OF THE SUBJECT that hangs on no other part, so neither a wreck or a clone arriving nor a turret
     * model landing before its hull can be mistaken for it.
     */
    hull(): LoadedPart | null {
        return this.rootPartOf(ACTIVE_SUBJECT);
    }

    /**
     * One subject's hull: its first-loaded part that hangs on no other part, else its first-loaded
     * part.
     */
    private rootPartOf(subjectId: string): LoadedPart | null {
        let first: LoadedPart | null = null;

        for (const part of this.context.parts.values()) {
            if (part.subjectId !== subjectId) {
                continue;
            }

            if (part.attached !== true) {
                return part;
            }

            first ??= part;
        }

        return first;
    }

    /** Every part belonging to one subject. */
    partsOf(subjectId: string): LoadedPart[] {
        return [...this.context.parts.values()].filter(
            (part) => part.subjectId === subjectId,
        );
    }

    /**
     * Draws a passive subject's geometry by its OWN file's rules, at the current levels and frame.
     *
     * The row chain speaks only for the ACTIVE subject, so this is the whole of what decides
     * whether a wreck's mesh is on screen. It runs when the geometry loads AND whenever the levels
     * change, and on every frame of the subject's own clip: applied once and never again, a death
     * clone kept whatever detail level happened to be set at the moment it arrived, and moving the
     * control afterwards left it behind. Measured over the shipped tree that is one ship - the
     * Broadside Class Cruiser, whose clone carries a `_LOD1` mesh and whose hull declares the levels
     * that make the control live - but the engine plainly supports it and a mod authoring an
     * ALT-tagged wreck is exactly who would be confused.
     *
     * Each mesh by the same four switches as the subject's rows - `meshSwitches` and `resolveRow` -
     * with its animation switch from the track of its OWN bone in the running clip, or of the first
     * bone it is skinned to. No bone node is hidden, so nothing below a bone the clip hides goes with
     * it; and a mesh the file hides is never shown by a clip.
     *
     * Through {@link setMeshDrawn}, never `visible`: a mesh node is very often a bone node too, and
     * `visible` prunes the whole subtree under it. See `meshVisibility.ts`.
     */
    applyOwnFileVisibility(part: LoadedPart): void {
        const subject = this.context.animation.subjectAnimation(part.subjectId);
        const action = subject.action;
        const running = runningClips(subject);
        const boneNodes = new Set<THREE.Object3D>(part.bonesByIndex.values());

        part.root.traverse((node) => {
            if (
                !(node instanceof THREE.Mesh) ||
                node.userData.aetShadowVolume === true
            ) {
                return;
            }

            const extras = (node.userData.alamo ?? {}) as MaterialExtras;
            const bone = ownBoneOf(node, part, boneNodes);
            const said =
                bone === undefined
                    ? undefined
                    : clipsSay(running, alamoNodeName(bone));
            const facts: RowFacts = {
                ...meshSwitches(
                    extras,
                    this.context.levels.alt,
                    this.context.levels.lod,
                ),
                masters: [],
                // A running clip decides; with none, the base pose, where the file's bone flag does.
                animation:
                    action === null
                        ? poseSays(bone)
                        : said === undefined
                          ? undefined
                          : { shown: said, source: "clip" },
            };

            if (resolveMaterial(extras).archetype === "shadow-volume") {
                this.context.environment.shadowVolumes.setCounting(
                    node,
                    castsShadow(facts),
                );
            }

            setMeshDrawn(node, resolveRow(facts).visible);
        });
    }

    /**
     * Gives up everything the scene registered for a subtree that is about to be disposed.
     *
     * The counterpart of what {@link addPart} records, and it did not exist while only the hull
     * and its hardpoints came through there - a scene part lives as long as the subject does. Now that
     * a death clone and a piece of wreckage take the same route, geometry comes and goes inside a
     * standing scene, and every one of those registrations outlived it: the stencil pass went on
     * counting a repaired ship's death clone, and its `bySource` map held the whole disposed
     * subtree alive through the mesh it was keyed by.
     *
     * The nodes are collected before anything is dropped, because `ShadowVolumePass.remove`
     * unparents the counting meshes it owns - mutating the children of a node being traversed.
     */
    dropRegistrations(subjectId: string, root: THREE.Object3D): void {
        const inside: THREE.Object3D[] = [];
        root.traverse((node) => inside.push(node));

        this.context.environment.dropShadowVolumes(inside);
        this.context.animation.unregisterPart(subjectId, inside);
        this.context.cameraRig.dropBillboards(new Set(inside));
    }

    /**
     * The node a fire bone names, looking on the HULL before the attached model.
     *
     * A hardpoint model is cut from the hull and keeps its whole skeleton, so a bone like
     * `FP_F-L_00` exists on BOTH - and the copy inside the attached model still sits at its
     * HULL-space position. Resolving it there placed it twice over, because the attached model is
     * itself attached at the hardpoint's bone. Measured on `Calamari_Cruiser`: the hull's
     * `FP_F-L_00` is at (28, 3, 275), a few units ahead of its hardpoint at (30, 2, 249) - and the
     * attached model's copy is at (59, 6, 523), almost exactly double. Every one of its six weapons
     * had its cone out in open space instead of at the muzzle.
     *
     * The fallback still matters: a hardpoint that carries a muzzle bone the hull does not have is
     * resolved on the hardpoint, which is the only place it exists.
     */
    fireBoneAttachment(partId: string, bone: string): THREE.Object3D {
        return (
            this.context.parts.get("hull")?.bones.get(bone.toLowerCase()) ??
            this.attachmentFor(partId, bone)
        );
    }
}
