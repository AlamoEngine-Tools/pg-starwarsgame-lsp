// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model tree's rows: the items the tree lists, what each row stands for in the scene and its parent row,
// the facts it shows (levels, decals, stealth, skinning, animated visibility), the inspection a row opens, and
// the reader's show and hide overrides. It reads the parts, the skeleton and the particle systems; nothing else.

import * as THREE from "three";
import { type BoneId } from "./boneIds";
import { type VisibilityTrack, clipsSay, trackSays } from "./boneVisibility";
import {
    type RowOverride,
    type Resolution,
    type OverrideChange,
    resolveRow,
    type RowFacts,
    castsShadow,
    fileState,
    meshSwitches,
    subtreeHiddenByReader,
    restingClip,
} from "./visibility";
import {
    type TreeItem,
    boneRowId,
    meshPlacement,
    effectPlacement,
} from "./previewTree";
import { alamoBoneName, alamoNodeName } from "./skeleton";
import {
    type MaterialExtras,
    resolveMaterial,
    isVisibleAt,
    isShieldMesh,
    isStealthMesh,
} from "./materials";
import { listedInModelTree } from "./particleScene";
import { meshDrawn, setMeshDrawn } from "./meshVisibility";
import { treeKeyOf } from "./boneNodes";
import { type Inspection } from "./inspector";
import { billboardTypeOf } from "./billboards";
import { type AbilityOwnership } from "./abilityRows";
import { type ParticleEntry } from "./particleEntry";
import { type LoadedPart } from "./loadedPart";
import { skinVisibilityBone } from "./skinBone";
import { triangleCount } from "./modelBounds";
import { type ViewportContext } from "./viewportContext";

/**
 * The scene node an effect is described by: the bone it is anchored on.
 *
 * NOT the system's own root, which is attached to the simulation space and so is owned by no bone at
 * all - see `ParticleSystemInstance.attachTo`. Falls back to the root for a system with no anchor,
 * which is a particle file opened on its own.
 */
function anchorNodeOf(entry: ParticleEntry): THREE.Object3D {
    return entry.instance.anchorNode ?? entry.instance.root;
}

/**
 * What one tree row controls.
 *
 * `boneIndex` means the row IS that bone; `ownerBone` is the hull bone a row that is not merged with
 * its bone is on: the bone a mesh rides, whose track is that mesh's animation switch, or the bone an
 * effect is anchored on. The rules that name a bone read either.
 */
interface RowTarget {
    boneIndex?: number;
    ownerBone?: number;
    mesh?: THREE.Mesh;
    particleId?: string;
}

export class ModelRows {
    /**
     * What one tree row controls.
     *
     * Explicit, because the alternative was inferring it from the row's id - and a merged row is
     * anchored on its BONE's id while standing for a mesh as well. Reading the mesh's state and
     * writing the bone's is what made a hidden mesh impossible to show again.
     */
    readonly rowTargets = new Map<string, RowTarget>();
    /**
     * Every bone a skinned mesh is weighted to, worked out once.
     *
     * Weak, so a mesh dropped when the subject changes takes its entry with it - this map is never
     * cleared by hand and must not be the thing that keeps a discarded model alive.
     */
    private readonly skinBoneIds = new WeakMap<THREE.Mesh, readonly BoneId[]>();
    /**
     * The bone each skinned mesh's visibility follows - see `skinVisibilityBone`. Weak and worked
     * out once, like {@link skinBoneIds}: asked on every frame of a playing clip, fixed at load.
     */
    private readonly skinSpeakers = new WeakMap<
        THREE.Mesh,
        BoneId | undefined
    >();
    /** The clip map {@link restingClipName} last answered for, and its answer. */
    private restingClipFor: Map<string, VisibilityTrack> | null = null;
    private restingClipCached: string | undefined;
    /** Each row's parent, so a hidden ancestor can veto without walking the scene graph. */
    readonly rowParents = new Map<string, string | null>();
    /** Each row's display name, so `because` can say WHICH ancestor hid it. */
    private readonly rowNames = new Map<string, string>();
    /** The reader's word, one per ROW. The only thing a tick writes. */
    private readonly rowOverrides = new Map<string, RowOverride>();
    /** The last resolution per row, so the tree can report why a row looks as it does. */
    private rowResolutions = new Map<string, Resolution>();
    /**
     * What each tree row stands for, so the inspector can describe it.
     *
     * `boneIndex` means the row IS that bone; `ownerBone` means the row is a mesh that rides one.
     * A merged row carries both a bone and a mesh, and gets a panel describing each.
     */
    private readonly inspectSources = new Map<
        string,
        {
            mesh?: THREE.Mesh;
            boneIndex?: number;
            ownerBone?: number;
            systemId?: string;
        }
    >();

    constructor(private readonly context: ViewportContext) {}

    /**
     * Everything the model is made of, as flat items for ONE tree: bones, the meshes attached to
     * them, and the particle emitters attached to them.
     *
     * A mesh's origin IS a bone and an emitter is attached to one, so the skeleton is already the
     * structure all three live in. Ids are prefixed by kind so they cannot collide.
     */
    treeItems(): TreeItem[] {
        const items: TreeItem[] = [];

        // Rebuilt with the rows, so the inspector can never describe something the tree no longer
        // shows. Deriving it separately would mean two places deciding what a row IS - and the
        // merge rule is exactly the sort of thing that drifts when it lives twice.
        this.inspectSources.clear();

        const hull = this.context.assembly.hull();
        if (hull === null) {
            return items;
        }

        const boneId = boneRowId;

        // Which bone owns each object. A mesh is normally parented to the bone that is its origin;
        // a SKINNED mesh is parented at the skeleton root instead, because it spans many bones, and
        // then lists under its own first joint.
        const boneOf = new Map<THREE.Object3D, number>();
        for (const [index, node] of hull.bonesByIndex) {
            boneOf.set(node, index);
        }

        const rootBone =
            [...hull.bonesByIndex.keys()].sort((a, b) => a - b)[0] ?? 0;

        // From the object ITSELF, not from its parent: an effect names its owning bone directly
        // rather than being attached to it, so the answer is usually the very node it was handed.
        const ownerOf = (object: THREE.Object3D): number => {
            for (
                let at: THREE.Object3D | null = object;
                at !== null;
                at = at.parent
            ) {
                const index = boneOf.get(at);
                if (index !== undefined) {
                    return index;
                }
            }

            if (object instanceof THREE.SkinnedMesh) {
                const first = object.skeleton.bones[0];
                const index =
                    first === undefined ? undefined : boneOf.get(first);

                if (index !== undefined) {
                    return index;
                }
            }

            return rootBone;
        };

        const meshByBone = new Map<number, THREE.Mesh>();
        const extraMeshes: { mesh: THREE.Mesh; owner: number }[] = [];

        // The effect a proxy bone IS, rather than one attached to it - see `effectPlacement`. Held
        // by bone so the bone loop below can claim both halves as one row, the way it already does
        // for a mesh of the same name.
        const effectByBone = new Map<number, string>();

        // Rebuilt from scratch: a row that no longer exists must not keep answering for a scene
        // object that has been unloaded.
        this.rowTargets.clear();
        this.rowParents.clear();
        this.rowNames.clear();

        const bones = this.context.queries.skeleton();
        const roots = new Set(
            bones.filter((bone) => bone.parent < 0).map((bone) => bone.index),
        );

        const boneName = (index: number): string => {
            const node = hull.bonesByIndex.get(index);
            return node === undefined
                ? ""
                : (alamoBoneName(alamoNodeName(node))?.name ??
                      alamoNodeName(node));
        };

        this.context.assembly.forEachPartMesh((mesh) => {
            // The loader guarantees a mesh is never a bone node itself, so its owner is simply the
            // nearest bone above it. `meshPlacement` decides on the two NAMES from there.
            const owner = ownerOf(mesh);
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

            const placement = meshPlacement(owner, roots, {
                bone: boneName(owner),
                mesh: extras.alamoMesh ?? alamoNodeName(mesh),
            });

            // A bone carrying two meshes keeps the first as its own row; the second stays a child
            // rather than being dropped.
            if ("merge" in placement && !meshByBone.has(placement.merge)) {
                meshByBone.set(placement.merge, mesh);
            } else {
                extraMeshes.push({
                    mesh,
                    owner:
                        "merge" in placement
                            ? placement.merge
                            : placement.childOf,
                });
            }
        });

        // The same question for the effects, before any row is claimed: a system whose proxy bone
        // carries its name IS that row. A bone with two of them keeps the first, exactly as it
        // keeps the first of two meshes.
        for (const [systemId, entry] of this.context.particles
            .particleSystems) {
            if (!listedInModelTree(entry.origin)) {
                continue;
            }

            const owner = ownerOf(anchorNodeOf(entry));
            const placement = effectPlacement(owner, roots, {
                bone: boneName(owner),
                effect: entry.system.name,
            });

            if ("merge" in placement && !effectByBone.has(placement.merge)) {
                effectByBone.set(placement.merge, systemId);
            }
        }

        const meshItem = (
            mesh: THREE.Mesh,
        ): { name: string; visible: boolean; gatedOff: boolean } => {
            const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

            return {
                // The ALO's own mesh name, not the glTF node name.
                name: extras.alamoMesh ?? alamoNodeName(mesh),
                visible: meshDrawn(mesh),
                gatedOff:
                    resolveMaterial(extras).hidden ||
                    !isVisibleAt(
                        extras,
                        this.context.levels.alt,
                        this.context.levels.lod,
                    ),
            };
        };

        for (const bone of bones) {
            const mesh = meshByBone.get(bone.index);
            const effect = effectByBone.get(bone.index);
            const parentId = bone.parent < 0 ? null : boneId(bone.parent);

            // Every half this row stands for. A particle proxy is all three at once - the bone, the
            // marker mesh the file hides, and the effect itself - and the panel describes each.
            this.inspectSources.set(boneId(bone.index), {
                boneIndex: bone.index,
                ...(mesh === undefined ? {} : { mesh }),
                ...(effect === undefined ? {} : { systemId: effect }),
            });

            // Merged: ONE row for a bone, the mesh that shares its name and the effect that shares
            // it, anchored on the BONE's id. That is what the bone's children already name and what
            // survives a reload, so the row needs no alias - and the alias is what cost three
            // defects, from a hull labelled COLLISION to checkboxes that did nothing.
            //
            // EVERY half is claimed, so the row writes everything it reports on. The old code
            // anchored this row on the bone's id, reported the MESH's state and wrote the BONE's -
            // which is why a hidden mesh could never be shown again.
            const details = mesh === undefined ? undefined : meshItem(mesh);
            const name =
                effect === undefined
                    ? (details?.name ?? bone.name)
                    : this.effectRowName(effect);

            this.claimRow(boneId(bone.index), name, parentId, {
                boneIndex: bone.index,
                ...(mesh === undefined ? {} : { mesh }),
                ...(effect === undefined ? {} : { particleId: effect }),
            });

            items.push({
                id: boneId(bone.index),
                // The effect is what a reader acts on, so a proxy row reads as one - the bone and
                // its marker mesh are the plumbing that carries it.
                kind:
                    effect !== undefined
                        ? "particle"
                        : mesh !== undefined
                          ? "mesh"
                          : "bone",
                name,
                parentId,
                visible: details?.visible ?? bone.visible,
                gatedOff: details?.gatedOff ?? false,
                ...(effect === undefined ? {} : { systemId: effect }),
            });
        }

        for (const { mesh, owner } of extraMeshes) {
            const details = meshItem(mesh);
            // `ownerBone`, not `boneIndex`: this row is the MESH. The bone it rides has a row of its
            // own, and describing it here too would say it twice.
            this.inspectSources.set(`mesh:${treeKeyOf(mesh)}`, {
                mesh,
                ownerBone: owner,
            });
            this.claimRow(
                `mesh:${treeKeyOf(mesh)}`,
                details.name,
                boneId(owner),
                { mesh, ownerBone: owner },
            );

            items.push({
                id: `mesh:${treeKeyOf(mesh)}`,
                kind: "mesh",
                name: details.name,
                parentId: boneId(owner),
                visible: details.visible,
                gatedOff: details.gatedOff,
            });
        }

        // One row per particle SYSTEM, not per emitter. In a model preview the switchable thing is
        // the whole effect - a proxy is either playing on this hull or it is not - and the emitters
        // a system is built from are the particle editor's business, not this one's. Listing them
        // here put twenty dead rows under a Star Destroyer's damage bones.
        for (const [systemId, entry] of this.context.particles
            .particleSystems) {
            // The Gameplay lens's own. A death explosion and a wreck's trailing fire are asked for
            // by NAME at the moment they are needed, so they are in no proxy list and hang off no
            // bone - `ownerOf` falls back to the root and filed them there, giving the reader a
            // `p_explosion_big00` under Root whose tick did nothing. See `listedInModelTree`.
            if (!listedInModelTree(entry.origin)) {
                continue;
            }

            const owner = ownerOf(anchorNodeOf(entry));

            // Already folded into its proxy bone's row above. Listing it again would put the same
            // effect on screen twice, with two checkboxes writing one thing.
            if (effectByBone.get(owner) === systemId) {
                continue;
            }

            this.inspectSources.set(`particle:${systemId}`, { systemId });
            this.claimRow(
                `particle:${systemId}`,
                entry.system.name,
                boneId(owner),
                { particleId: systemId, ownerBone: owner },
            );

            items.push({
                id: `particle:${systemId}`,
                kind: "particle",
                name: this.effectRowName(systemId),
                parentId: boneId(owner),
                visible:
                    entry.state.shown && entry.altVisible && entry.lodVisible,
                gatedOff: !(entry.altVisible && entry.lodVisible),
                systemId,
            });
        }

        // Every row is known now, so the chain can run - parents are already ahead of children,
        // because the skeleton is walked parent-first and the meshes are attached to it.
        this.resolveRows();

        return items.map((item) => {
            const resolved = this.rowResolutions.get(item.id);

            // `gatedOff` comes from the chain as well, because it is the same question asked with
            // the reader taken out. Leaving each row to work it out for itself is what let a row
            // report one thing and draw another.
            return resolved === undefined
                ? item
                : {
                      ...item,
                      visible: resolved.visible,
                      because: resolved.because,
                      decidedBy: resolved.decidedBy,
                      gatedOff: !resolved.authored,
                  };
        });
    }

    /**
     * The row that owns one effect.
     *
     * A merged proxy row is anchored on its bone, so an effect's row id is not derivable from its
     * system id. Asked of the row table, which is the only thing that knows.
     */
    rowForSystem(systemId: string): string {
        for (const [rowId, target] of this.rowTargets) {
            if (target.particleId === systemId) {
                return rowId;
            }
        }

        return `particle:${systemId}`;
    }

    /**
     * What a tree row actually is, for the inspector.
     *
     * A list because a merged row is two things at once. Empty for a row the tree no longer has -
     * a selection can outlive a reload or a level change.
     */
    inspectionOf(id: string): Inspection[] {
        const source = this.inspectSources.get(id);
        if (source === undefined) {
            return [];
        }

        const found: Inspection[] = [];
        const bones = this.context.queries.skeleton();
        const nameOf = (index: number): string =>
            bones.find((bone) => bone.index === index)?.name ?? "";

        if (source.mesh !== undefined) {
            const extras = (source.mesh.userData.alamo ?? {}) as MaterialExtras;
            const geometry = source.mesh.geometry;
            const vertices = geometry.getAttribute("position")?.count ?? 0;

            found.push({
                kind: "mesh",
                name: extras.alamoMesh ?? alamoNodeName(source.mesh),
                boneName: nameOf(source.ownerBone ?? source.boneIndex ?? -1),
                vertexCount: vertices,
                triangleCount: triangleCount(geometry),
                drawn: meshDrawn(source.mesh),
                extras,
                meshIndex:
                    typeof extras.alamoMeshIndex === "number"
                        ? extras.alamoMeshIndex
                        : undefined,
                subMeshIndex:
                    typeof extras.alamoSubMeshIndex === "number"
                        ? extras.alamoSubMeshIndex
                        : undefined,
            });
        }

        if (source.boneIndex !== undefined) {
            const bone = bones.find(
                (entry) => entry.index === source.boneIndex,
            );
            const node = this.context.assembly
                .hull()
                ?.bonesByIndex.get(source.boneIndex);

            if (bone !== undefined) {
                found.push({
                    kind: "bone",
                    index: bone.index,
                    name: bone.name,
                    parentIndex: bone.parent,
                    parentName: bone.parent < 0 ? "" : nameOf(bone.parent),
                    visible: bone.visible,
                    billboard:
                        node === undefined ? null : billboardTypeOf(node),
                });
            }
        }

        if (source.systemId !== undefined) {
            const entry = this.context.particles.particleSystems.get(
                source.systemId,
            );

            if (entry !== undefined) {
                found.push({
                    kind: "particle",
                    name: entry.system.name,
                    boneName:
                        entry.attachBoneIndex === undefined
                            ? (entry.attachBone ?? "")
                            : nameOf(entry.attachBoneIndex),
                    emitterCount: entry.system.emitters.length,
                    playing:
                        this.rowResolutions.get(
                            this.rowForSystem(source.systemId),
                        )?.visible ??
                        (entry.state.shown &&
                            entry.altVisible &&
                            entry.lodVisible),
                });
            }
        }

        return found;
    }

    /**
     * The rows the reader has switched off by hand.
     *
     * The OVERRIDES, not what happens to be invisible: a mesh gated off by the current damage level
     * is not something anyone chose, and writing it down would turn a level's own rule into an
     * explicit hide that outlives the level. Restorable now that a mesh key survives a reload.
     */
    hiddenItemIds(): string[] {
        return [...this.rowOverrides]
            .filter(([, override]) => override === "hidden")
            .map(([row]) => row);
    }

    /**
     * The rows the reader has switched ON by hand.
     *
     * Both directions are carried now. A mesh is usually drawn until someone hides it, but a
     * shadow volume or a collision hull is gated off until someone asks for it - and asking for it
     * is exactly the deliberate act worth surviving a reopen.
     */
    shownItemIds(): string[] {
        return [...this.rowOverrides]
            .filter(([, override]) => override === "shown")
            .map(([row]) => row);
    }

    /**
     * Shows or hides any mix of bones, meshes and particle systems at once.
     *
     * One entry point, because the tree selects across kinds and a multi-select toggle has to move
     * all of them together. A bone carries what is attached to it, so switching one off takes its
     * subtree with it - which is what the reader is asking for when they untick a limb.
     */
    setItemsVisible(ids: readonly string[], visible: boolean): void {
        this.applyRowOverrides(
            ids.map((row) => ({
                row,
                override: visible ? ("shown" as const) : ("hidden" as const),
            })),
        );
    }

    /**
     * Records the reader's word on a set of rows and re-runs the chain.
     *
     * `null` gives a row back to the model. One entry point and one map, because the old code kept
     * three - meshes by tree key, bones by index, systems by id - and a row anchored on a bone's id
     * while standing for a mesh wrote one and reported the other.
     */
    applyRowOverrides(changes: readonly OverrideChange[]): void {
        for (const { row, override } of changes) {
            if (override === null) {
                this.rowOverrides.delete(row);
            } else {
                this.rowOverrides.set(row, override);
            }
        }

        this.resolveRows();
    }

    /** What the reader has said about each row, for carrying across a reopen. */
    rowOverrideEntries(): { row: string; override: RowOverride }[] {
        return [...this.rowOverrides].map(([row, override]) => ({
            row,
            override,
        }));
    }

    /**
     * Hands every row back to the model.
     *
     * Called when a clip starts: watching an animation is checking whether the animation works, and
     * that can only be answered against the model as authored. The clip already resets the pose
     * each time it runs; the visibilities are part of that pose. Master toggles are untouched - see
     * `clearedForPlayback`.
     */
    clearRowOverrides(): void {
        this.rowOverrides.clear();
        this.resolveRows();
    }

    /**
     * Hands the rows ONE ability speaks for back to the model.
     *
     * Every channel an ability drives - a proxy's state, the shield mesh's and the shell's - is a
     * write of the state switch, which the reader's word sits above. So one tick on such a row
     * outranked the ability for good: the switch fired, the chain ignored it, and nothing but a
     * whole-model Reset could undo it. Switching the ability now takes its own rows back, which is
     * the same rule a starting clip already follows.
     *
     * Scoped, unlike {@link clearRowOverrides}. A clip may be global because it resets the entire
     * pose; an ability is one statement about a handful of rows, and a mesh the reader hid for an
     * unrelated reason has to survive it.
     */
    releaseAbilityRows(ownership: AbilityOwnership): void {
        const released: string[] = ownership.systemIds.map((id) =>
            this.rowForSystem(id),
        );

        if (ownership.shieldMesh || ownership.stealthShell) {
            for (const [rowId, target] of this.rowTargets) {
                if (target.mesh === undefined) {
                    continue;
                }

                const extras = (target.mesh.userData.alamo ??
                    {}) as MaterialExtras;

                if (
                    (ownership.shieldMesh && isShieldMesh(extras)) ||
                    (ownership.stealthShell && isStealthMesh(extras))
                ) {
                    released.push(rowId);
                }
            }
        }

        // Only the rows that actually carry one. Every ability click would otherwise re-resolve the
        // whole skeleton to delete nothing, and most abilities own no row the reader has touched.
        const carrying = released.filter((row) => this.rowOverrides.has(row));

        if (carrying.length === 0) {
            return;
        }

        // One re-resolve at the end, not one per row.
        this.applyRowOverrides(
            carrying.map((row) => ({ row, override: null })),
        );
    }

    /**
     * Rebuilds the rows and re-runs the visibility chain over them.
     *
     * Building the rows is what discovers which scene objects each one owns, so the two happen
     * together - a chain run against stale rows would answer for meshes that are no longer loaded.
     */
    refreshRows(): void {
        this.treeItems();
    }

    /**
     * Runs the visibility chain over every row and puts the answers on the scene.
     *
     * Parent before child, so a row can be told whether an ancestor already hid it without walking
     * anything twice. One pass, every time something changes - which is the point: the old model
     * had three override maps and three places that applied them, and they disagreed.
     */
    resolveRows(): void {
        const resolutions = new Map<string, Resolution>();

        // Whether the READER has switched each row's subtree off - the only thing that reaches the
        // rows below one. No switch of the model is inherited; see `subtreeHiddenByReader`.
        const readerHidden = new Map<string, boolean>();

        // What the MESH half of a row does, which is a different question once an effect is merged
        // in: the marker mesh of a particle proxy is hidden in the file, and showing the effect
        // must not put that plate on screen.
        const meshAnswers = new Map<string, Resolution>();

        for (const [rowId, target] of this.rowTargets) {
            const facts = this.factsFor(rowId, target, readerHidden);
            const answer = resolveRow(
                target.particleId === undefined
                    ? facts
                    : this.effectFacts(facts, target),
            );

            resolutions.set(rowId, answer);
            meshAnswers.set(rowId, resolveRow(facts));
            readerHidden.set(rowId, subtreeHiddenByReader(facts));

            // A shadow volume is never drawn, but it casts by the same switches as any part - see
            // `castsShadow`. The rows are the one writer of that for the subject; a passive
            // subject's volumes are its own file's - see `PartAssembly.applyOwnFileVisibility`.
            if (
                target.mesh !== undefined &&
                resolveMaterial(
                    (target.mesh.userData.alamo ?? {}) as MaterialExtras,
                ).archetype === "shadow-volume"
            ) {
                this.context.environment.shadowVolumes.setCounting(
                    target.mesh,
                    castsShadow(facts),
                );
            }
        }

        this.rowResolutions = resolutions;

        for (const [rowId, target] of this.rowTargets) {
            const visible = resolutions.get(rowId)?.visible ?? true;

            if (target.mesh !== undefined) {
                setMeshDrawn(
                    target.mesh,
                    meshAnswers.get(rowId)?.visible ?? visible,
                );
            }

            if (target.boneIndex !== undefined) {
                const node = this.context.assembly
                    .hull()
                    ?.bonesByIndex.get(target.boneIndex);

                // `visible` on a bone PRUNES ITS SUBTREE, so it follows the reader alone. What the
                // model says about a bone is said to the parts on it, each through its own
                // switches, and never to the parts below it.
                if (node !== undefined) {
                    node.visible = readerHidden.get(rowId) !== true;
                }
            }

            if (target.particleId !== undefined) {
                this.context.particles.setParticleSystemDrawn(
                    target.particleId,
                    visible,
                );
            }
        }
    }

    /** Everything the chain needs to know about one row. */
    private factsFor(
        rowId: string,
        target: RowTarget,
        readerHidden: ReadonlyMap<string, boolean>,
    ): RowFacts {
        const parent = this.rowParents.get(rowId) ?? null;

        const facts: RowFacts = {
            state: fileState(true),
            alt: true,
            lod: true,
            // No masters here. The effects master belongs to the EFFECT half of a row and is added
            // by `effectFacts` - put on the base facts it would reach the marker mesh, so switching
            // effects off would hide geometry.
            masters: [],
            override: this.rowOverrides.get(rowId),
            // Only a row the READER switched off above this one. A bone the model hides says
            // nothing about the parts below it.
            ancestorHidden:
                parent !== null && readerHidden.get(parent) === true
                    ? (this.rowNames.get(parent) ?? "")
                    : undefined,
        };

        // What the idle clip a unit stands in says about this row - see `UnitRules.idle`.
        let idle: boolean | undefined;

        if (target.mesh !== undefined) {
            const extras = (target.mesh.userData.alamo ?? {}) as MaterialExtras;
            const switches = meshSwitches(
                extras,
                this.context.levels.alt,
                this.context.levels.lod,
            );

            Object.assign(facts, switches);

            // The state switch: the file writes it from its hidden flag, and in a unit preview the
            // game's rules write over it - the shield, the stealth shell, the collision hulls, the
            // damage decals, a hardpoint's bones. See `UnitRules.meshState`. A skinned mesh sits on
            // the model's root for the rules that name a bone, whatever it is skinned to.
            facts.state = this.context.rules.meshState(
                switches.state,
                extras,
                this.skinBonesOf(target.mesh).length > 0
                    ? this.rootBoneIndex()
                    : (target.boneIndex ?? target.ownerBone),
            );
        }

        // The bone whose track is this row's animation switch - see `speakingBone`.
        const id = this.speakingBone(target);

        if (id !== undefined) {
            const clip = this.animatedVisibilityOf(id);
            facts.animation =
                clip === undefined
                    ? undefined
                    : { shown: clip, source: "clip" };

            // Only when nothing is playing, which is what a resting pose MEANS and what the method
            // above it has always claimed. Computed unconditionally, the idle's opinion reached the
            // chain from below whenever the playing clip happened to be silent about a bone - so a
            // clip that shows a part the idle hides lost to the idle. Yoda's hilt is that: his
            // `attack_00` shows `b_hilt` and his `idle_00` hides it, and the hilt stayed away.
            idle =
                this.context.animation.active.action === null
                    ? this.restingVisibilityOf(id)
                    : undefined;
        }

        facts.animation ??= this.context.rules.idle(idle);

        return facts;
    }

    /**
     * The canonical id of the bone whose track is a row's animation switch.
     *
     * The row's own bone; for a rigid mesh the bone it rides; for a SKINNED mesh the first bone it is
     * skinned to, which is what the game measures - not the bones further down its skin, and not the
     * bone the tree files it under. An effect row that is not merged with its bone has none here: its
     * emission follows its own bone's track in the particle layer.
     *
     * Deliberately NOT a bone's `visible`: that reads the scene node this chain WRITES. The canonical
     * id off the NODE, never the display name the tree shows - two bones can share a name; only the
     * id separates them, see `boneIds.ts`.
     */
    private speakingBone(target: RowTarget): BoneId | undefined {
        const skin =
            target.mesh === undefined ? [] : this.skinBonesOf(target.mesh);
        const bone =
            target.boneIndex ??
            (target.mesh !== undefined && skin.length === 0
                ? target.ownerBone
                : undefined);

        if (bone !== undefined) {
            return this.context.assembly.hull()?.bonesByIndex.get(bone)?.name;
        }

        if (target.mesh === undefined || skin.length === 0) {
            return undefined;
        }

        if (this.skinSpeakers.has(target.mesh)) {
            return this.skinSpeakers.get(target.mesh);
        }

        // The part the mesh belongs to: its sub-meshes are siblings there, and a mesh index means
        // nothing across two model files.
        const part = this.partOf(target.mesh);
        const id =
            part === undefined
                ? undefined
                : skinVisibilityBone(
                      target.mesh,
                      part.root,
                      part.bonesByIndex.get(0),
                  );

        this.skinSpeakers.set(target.mesh, id);

        return id;
    }

    /** The loaded part whose root is nearest above `node`. */
    private partOf(node: THREE.Object3D): LoadedPart | undefined {
        const byRoot = new Map(
            [...this.context.parts.values()].map((part) => [part.root, part]),
        );

        for (
            let at: THREE.Object3D | null = node;
            at !== null;
            at = at.parent
        ) {
            const part = byRoot.get(at);

            if (part !== undefined) {
                return part;
            }
        }

        return undefined;
    }

    /** The hull's root bone: its lowest index. */
    private rootBoneIndex(): number | undefined {
        const indices = [
            ...(this.context.assembly.hull()?.bonesByIndex.keys() ?? []),
        ];

        return indices.length === 0 ? undefined : Math.min(...indices);
    }

    /**
     * The same row, asked about the EFFECT it carries.
     *
     * A proxy row is a bone, a marker mesh and a system at once, and the three do not answer alike:
     * the file hides the marker plate, while what the file says about the EFFECT is whether the
     * proxy is on. So the effect takes the row's ancestors, the reader's word and the clip from the
     * base facts, and supplies its own state and levels.
     *
     * The rules and the levels are the two the rewrite dropped once already: a plume that only
     * lights when a hardpoint is destroyed was drawing from the moment the model opened, and a
     * proxy the current ALT/LOD gates off was drawing too.
     */
    private effectFacts(facts: RowFacts, target: RowTarget): RowFacts {
        const entry =
            target.particleId === undefined
                ? undefined
                : this.context.particles.particleSystems.get(target.particleId);

        return {
            ...facts,

            // The one master anyone legitimately keeps switched off while watching a clip: effects
            // are what obstructs the view of the thing being checked.
            masters: [
                { id: "effects", on: this.context.particles.particlesVisible },
            ],

            // The cloak and the Damage_Particles bone are written in the rules layer, in the same
            // chain that writes the meshes - see `UnitRules.effectState`. Not in the caller's effect
            // decider: that put a flag in two places, and the two disagreed the moment anything drove
            // one without the other.
            state: this.context.rules.effectState(
                entry?.state ?? fileState(true),
                target.boneIndex ?? target.ownerBone,
            ),
            alt: entry?.altVisible ?? true,
            lod: entry?.lodVisible ?? true,

            // The marker mesh's material says nothing about the effect.
            nonVisual: undefined,
        };
    }

    /**
     * What the model's idle clip says about a bone when NOTHING is playing.
     *
     * Read at frame zero: this is a resting pose, not a performance. A model with no idle clip, or
     * an idle that says nothing about this bone, gets undefined and falls back to the file - the
     * rule the user set.
     */
    private restingVisibilityOf(id: BoneId | undefined): boolean | undefined {
        const clip = this.restingClipName();

        return clip === undefined
            ? undefined
            : trackSays(
                  this.context.animation.active.visibility.get(clip),
                  id,
                  0,
              );
    }

    /**
     * Which clip the model rests in, worked out once per set of clips.
     *
     * It was copied, sorted and searched on every call - and this is asked once per bone row
     * per frame, now once per skin bone of every skinned mesh as well. The answer cannot
     * change while the subject's clips do not, so it is memoised against the map it reads.
     */
    private restingClipName(): string | undefined {
        const clips = this.context.animation.active.visibility;

        if (this.restingClipFor !== clips) {
            this.restingClipFor = clips;
            this.restingClipCached = restingClip([...clips.keys()]);
        }

        return this.restingClipCached;
    }

    /**
     * What the clips running say about a bone on this frame, or undefined when none says anything.
     *
     * Undefined is NOT false: a clip that never mentions a bone is not asking for it to be hidden,
     * and treating silence as a hide is how an animation came to blink meshes out.
     */
    private animatedVisibilityOf(id: BoneId | undefined): boolean | undefined {
        const action = this.context.animation.active.action;

        return action === null
            ? undefined
            : clipsSay(
                  [
                      {
                          track: this.context.animation.active.visibility.get(
                              action.getClip().name,
                          ),
                          seconds: action.time,
                      },
                  ],
                  id,
              );
    }

    /**
     * The canonical ids of every bone a mesh is weighted to, or empty when it is not skinned.
     *
     * Cached per mesh: this is asked on every frame of a playing clip, once per row, and the answer
     * cannot change - a skeleton's joint list is fixed when the model loads. Rebuilding the array
     * each time is the same mistake `resolveRows` records above about `skeleton()`.
     */
    private skinBonesOf(mesh: THREE.Mesh): readonly BoneId[] {
        const cached = this.skinBoneIds.get(mesh);

        if (cached !== undefined) {
            return cached;
        }

        const skinned = mesh as THREE.SkinnedMesh;
        const ids =
            skinned.isSkinnedMesh === true
                ? (skinned.skeleton?.bones ?? []).map((bone) =>
                      alamoNodeName(bone),
                  )
                : [];

        this.skinBoneIds.set(mesh, ids);

        return ids;
    }

    /**
     * What a row carrying an effect is called.
     *
     * The `[heat]` tag goes on only when EVERY emitter is a heat distortion, which is the case that
     * draws nothing at all here. A system with one heat emitter among several still shows
     * something, and flagging it would read as a broken effect rather than an honest limitation.
     */
    private effectRowName(systemId: string): string {
        const entry = this.context.particles.particleSystems.get(systemId);
        if (entry === undefined) {
            return "";
        }

        const heat = entry.instance.emitterIsHeat();

        return (
            entry.system.name +
            (heat.length > 0 && heat.every(Boolean) ? " [heat]" : "")
        );
    }

    /**
     * Records what one row stands for.
     *
     * Called as each row is built, beside the row itself, so the two cannot drift apart. Everything
     * a row owns is applied from ONE answer - see `resolveRows`.
     */
    private claimRow(
        rowId: string,
        name: string,
        parentId: string | null,
        target: RowTarget,
    ): void {
        this.rowTargets.set(rowId, target);
        this.rowParents.set(rowId, parentId);
        this.rowNames.set(rowId, name);
    }
}
