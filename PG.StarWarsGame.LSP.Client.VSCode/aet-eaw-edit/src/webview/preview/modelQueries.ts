// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Questions about the model in the scene, answered without changing it: its skeleton as a flat list, what
// is attached to each bone, where a bone sits on screen and in the world, the stats readout, and which
// damage and detail levels it defines with what each costs. It reads the parts and the other layers; nothing
// else.

import {
    definedLevels,
    type DefinedLevels,
    type LevelTagged,
    type LevelCost,
    costByLevel,
} from "./levels";
import * as THREE from "three";
import { type MaterialExtras } from "./materials";
import { triangleCount } from "./modelBounds";
import { type ViewportStats } from "./viewportTypes";
import {
    type FlatBone,
    alamoBoneName,
    alamoNodeName,
    type BoneAttachment,
} from "./skeleton";
import { type ViewportContext } from "./viewportContext";

/** A three vector as the plain shape the wire and the arithmetic use. */
function vectorOf(at: THREE.Vector3): { x: number; y: number; z: number } {
    return { x: at.x, y: at.y, z: at.z };
}

export class ModelQueries {
    constructor(private readonly context: ViewportContext) {}

    /** The ALT and LOD levels the loaded parts and effects actually define. */
    definedLevels(): DefinedLevels {
        const tagged: LevelTagged[] = [];

        this.context.modelRoot.traverse((node) => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            const extras = node.userData.alamo as MaterialExtras | undefined;
            if (extras !== undefined) {
                tagged.push({
                    alt: extras.alamoAlt ?? null,
                    lod: extras.alamoLod ?? null,
                    altDecreaseStayHidden: false,
                });
            }
        });

        for (const entry of this.context.particles.particleSystems.values()) {
            tagged.push(entry.levels);
        }

        return definedLevels(tagged);
    }

    /**
     * What each detail level draws, in meshes and triangles, at the current damage stage.
     *
     * Every mesh the model holds rather than only the ones on screen: the question is what a level
     * COSTS, which is a property of the file and not of where the slider happens to sit. The rule
     * that decides which meshes belong to a level is `costByLevel`'s, in `levels.ts`.
     */
    costByLod(levels: readonly number[]): Map<number, LevelCost> {
        const meshes: { extras: MaterialExtras; triangles: number }[] = [];

        this.context.modelRoot.traverse((node) => {
            if (!(node instanceof THREE.Mesh)) {
                return;
            }

            const extras = node.userData.alamo as MaterialExtras | undefined;
            if (extras !== undefined) {
                meshes.push({
                    extras,
                    triangles: triangleCount(node.geometry),
                });
            }
        });

        return costByLevel(meshes, this.context.levels.alt, levels);
    }

    stats(): ViewportStats {
        let meshes = 0;
        let triangles = 0;

        this.context.modelRoot.traverse((node) => {
            if (node instanceof THREE.Mesh && this.context.levels.drawn(node)) {
                meshes++;
                triangles += triangleCount(node.geometry);
            }
        });

        return {
            // The ACTIVE subject's, not the scene's. These stats describe the model being
            // previewed, and a wreck it shed or the clone it became is not another part of it.
            parts: this.context.assembly.activeSubjectRoots().length,
            meshes,
            triangles: Math.round(triangles),
            // From the same source as the tree. Counting THREE.Bone instead reported ZERO for any
            // model with no skinned mesh - the loader only creates Bone objects for skin joints, so
            // the Star Destroyer's 71 bones read as none while the tree beside it listed all of them.
            bones: this.skeleton().length,
            animations: this.context.animation.animationNames,
        };
    }

    /**
     * Where a bone sits in world space, or null when it is not loaded.
     *
     * Thin on purpose: the blast arithmetic is pure and lives in `blast.ts`, and all it needs from
     * a scene graph is a point per hardpoint. Anything more here would be geometry the tests cannot
     * reach.
     */
    bonePosition(
        partId?: string,
        bone?: string,
    ): { x: number; y: number; z: number } | null {
        if (partId === undefined) {
            return vectorOf(
                this.context.modelRoot.getWorldPosition(new THREE.Vector3()),
            );
        }

        // Recorded even though this refuses to place anything: the request was made, and it is the
        // REQUEST that gets reported. The early return below used to happen before `attachmentFor`
        // was ever reached, so a hardpoint that had not loaded left no trace anywhere.
        this.context.assembly.noteAttachment(partId, bone);

        const part = this.context.parts.get(partId);
        if (part === undefined) {
            return null;
        }

        if (bone === undefined) {
            return vectorOf(part.root.getWorldPosition(new THREE.Vector3()));
        }

        // NULL for a bone that is not there, rather than the part's origin. `attachmentFor` answers
        // the root because it has to return something to parent to; this returns a POSITION, and
        // the model's origin is not this bone's position. Handing one back put every unresolved
        // hardpoint at the same spot, which is a blast radius that catches all of them.
        const node = part.bones.get(bone.toLowerCase());

        return node === undefined
            ? null
            : vectorOf(node.getWorldPosition(new THREE.Vector3()));
    }

    /**
     * The hull's bones, flattened.
     *
     * Read off the loaded scene rather than tracked separately, so the tree can never describe a
     * skeleton that is not the one on screen. Only the FIRST part's bones: an attached turret carries
     * its own skeleton, and merging them would produce a tree whose indices match no single model.
     */
    skeleton(): FlatBone[] {
        const hull = this.context.assembly.hull();
        if (hull === null) {
            return [];
        }

        const indexOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            indexOf.set(node, index);
        }

        // The nearest ANCESTOR that is a bone, not just the immediate parent. The exporter puts a
        // synthetic node between the skeleton and the scene, and a skinned model can have others in
        // between too - so checking only `node.parent` reported a bone as parentless whenever
        // anything sat in the gap, and the tree came out with a handful of spurious roots. On the
        // rancor that made `Shadow_Feet` a root of the model, which it plainly is not.
        const boneAncestor = (node: THREE.Object3D): number => {
            for (let at = node.parent; at !== null; at = at.parent) {
                const index = indexOf.get(at);
                if (index !== undefined) {
                    return index;
                }
            }

            return -1;
        };

        return [...hull.bonesByIndex.entries()]
            .map(([index, node]) => ({
                index,
                name:
                    alamoBoneName(alamoNodeName(node))?.name ??
                    alamoNodeName(node),
                parent: boneAncestor(node),
                visible: node.visible,
            }))
            .sort((a, b) => a.index - b.index);
    }

    /** What is attached to each bone of the hull, keyed by bone index. */
    attachmentsByBone(): Map<number, BoneAttachment[]> {
        const attachments = new Map<number, BoneAttachment[]>();
        const hull = this.context.assembly.hull();
        if (hull === null) {
            return attachments;
        }

        // Which bone each object belongs to, so a mesh can be placed even when it is not a direct
        // child of one. A SKINNED mesh is parented to the skeleton root rather than attached to any single bone -
        // it spans many - and listing only direct children left most of a skinned model with no row
        // at all, which is no use for a control whose whole point is reaching every mesh.
        const boneOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            boneOf.set(node, index);
        }

        const rootBone =
            [...hull.bonesByIndex.keys()].sort((a, b) => a - b)[0] ?? 0;
        const meshesByBone = new Map<number, THREE.Mesh[]>();

        hull.root.traverse((object) => {
            if (!(object instanceof THREE.Mesh) || object.name === "") {
                return;
            }

            // The nearest ancestor that is a bone; the root otherwise.
            let owner = rootBone;
            for (let at = object.parent; at !== null; at = at.parent) {
                const index = boneOf.get(at);
                if (index !== undefined) {
                    owner = index;
                    break;
                }
            }

            meshesByBone.set(owner, [
                ...(meshesByBone.get(owner) ?? []),
                object,
            ]);
        });

        for (const [index, node] of hull.bonesByIndex) {
            const found: BoneAttachment[] = [];

            for (const mesh of meshesByBone.get(index) ?? []) {
                found.push({ kind: "mesh", label: mesh.name });
            }

            // A attached part is a separate root parented onto the bone, so it reads as a hardpoint.
            for (const part of this.context.parts.values()) {
                if (part.root.parent === node) {
                    found.push({ kind: "hardpoint", label: part.id });
                }
            }

            if (found.length > 0) {
                attachments.set(index, found);
            }
        }

        return attachments;
    }
}
