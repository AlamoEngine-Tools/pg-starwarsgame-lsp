// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's levels and gates: the ALT (damage) and LOD (detail) level the model is shown at, the damage
// decals a destroyed hardpoint opens, the collision meshes, the revealed shield and stealth, and the one
// answer per mesh of whether it is drawn at all. It owns those values; every other layer reads them here.

import { proxyVisibleAt } from "./levels";
import { ACTIVE_SUBJECT } from "./loadedPart";
import * as THREE from "three";
import { type MaterialExtras, resolveMaterial, isVisibleAt } from "./materials";
import { meshDrawn } from "./meshVisibility";
import { type ViewportContext } from "./viewportContext";

export class LevelGates {
    alt = 0;
    lod = 0;
    /**
     * Whether the last ALT change was downwards.
     *
     * A property of the transition, not of the state - the engine recomputes it on every SetALT and
     * passes false for LOD changes.
     */
    altDescending = false;
    /**
     * Damage decals, lowercased.
     *
     * `knownDecals` is every mesh some hardpoint names through `Damage_Decal`; `shownDecals` is the
     * subset whose hardpoint is destroyed. Without the first, a level change would re-show a decal
     * the model ships visible and every capital ship would open pre-scorched.
     */
    knownDecals: ReadonlySet<string> = new Set();
    shownDecals: ReadonlySet<string> = new Set();
    /**
     * Meshes a hardpoint claims as its collision hull.
     *
     * Kept beside the decals because they are the same kind of fact - hardpoint XML deciding what a
     * mesh IS - and settled in the same pass of the chain. Unlike a decal there is no "shown" half:
     * the engine never draws one, whatever the damage state.
     */
    collisionMeshes: ReadonlySet<string> = new Set();
    /**
     * Whether an active ability is holding the SHIELD MESH visible.
     *
     * The mesh ships hidden - `MeshShield.fx` carries `alamoHidden: true` - so this is the model
     * being told it does draw it after all, which is why it flips `inFile` rather than adding a
     * gate. The reader's own tick still sits above it.
     */
    shieldRevealed = false;
    /**
     * Whether a cloak ability is engaged, in which case the STEALTH SHELL is the only thing drawn.
     *
     * A swap, not a reveal, and that is what makes it different from the shield above. The shell
     * ships VISIBLE on all nine models that carry one, so with nothing to hide it the preview drew
     * the cloak over the hull permanently - the see-through Vengeance. And when the cloak IS on,
     * the hull, its effects and its shadow all go, because there is nothing else to see.
     *
     * False is the permanent answer for a unit that declares no cloak, so this one flag is also
     * what keeps the shell off the models whose object never declares the ability.
     */
    stealthed = false;

    constructor(private readonly context: ViewportContext) {}

    /** Drops the decals, collision meshes and shield state of a model that is no longer shown. */
    forgetModel(): void {
        this.knownDecals = new Set();
        this.shownDecals = new Set();
        this.collisionMeshes = new Set();
        this.shieldRevealed = false;
    }

    /**
     * Which decal meshes exist, and which of them are showing.
     *
     * Both together, because a decal that is not showing has to be actively hidden: the model ships
     * these meshes VISIBLE - measured on Ev_stardestroyer.alo, where HP_F-L_Blast is both a bone and
     * a mesh - so the engine hides them until the hardpoint is destroyed, and so must this.
     */
    setDecals(known: ReadonlySet<string>, shown: ReadonlySet<string>): void {
        this.knownDecals = known;
        this.shownDecals = shown;
        this.applyLevels();
    }

    /**
     * Which meshes the hardpoints claim as collision hulls.
     *
     * Gated OFF rather than hidden outright, so the reader can still tick one on in the tree and
     * look at it - which is exactly what someone opens a hull for. The material rules already catch
     * most of these by name; this is the author's own word, for the ones they do not.
     */
    setCollisionMeshes(names: ReadonlySet<string>): void {
        this.collisionMeshes = names;
        this.applyLevels();
    }

    /** Shows or hides the shield mesh, for the abilities that reveal it. */
    setShieldRevealed(revealed: boolean): void {
        this.shieldRevealed = revealed;
        this.applyLevels();
    }

    /**
     * Cloaks the unit, or uncloaks it.
     *
     * Through `applyLevels` like the shield, because the row chain is the one writer of drawn state
     * and this has to go through it rather than around it. Setting `visible` on the meshes here
     * would be a second writer, and the chain would put them back on its next run.
     */
    setStealthed(on: boolean): void {
        this.stealthed = on;
        this.applyLevels();
    }

    /** Whether a cloak is engaged, which every other visibility rule defers to. */
    isStealthed(): boolean {
        return this.stealthed;
    }

    /**
     * Sets the damage state and detail level, the way the engine gates geometry.
     *
     * The DIRECTION matters, not just the value: the engine computes `altdesc = alt < m_alt` inside
     * `SetALT` and hands it to `CheckAltLod`, where a proxy marked `altDecreaseStayHidden` is held
     * back. A LOD change passes false, because winding detail down is not repairing anything.
     */
    setLevels(alt: number, lod: number): void {
        this.altDescending = alt < this.alt;
        this.alt = alt;
        this.lod = lod;

        // The reader's word SURVIVES a level change. It used to be cleared, on the grounds that a
        // different damage state is a different set of geometry - but viewport visibility is now
        // deliberately independent of what the model draws, and the row that outlives the level is
        // marked as one the model does not draw, so nothing is claimed that is not true.
        this.applyLevels();
    }

    /**
     * Everything that can hide a mesh, decided together.
     *
     * Separate passes would fight each other: a level change would re-show a decal whose hardpoint
     * is intact, and re-hide one whose hardpoint had just been blown off.
     */
    applyLevels(): void {
        // What the LEVEL knows, and nothing else. Deciding visibility here as well is what let two
        // systems write the same thing: this one re-derived every mesh from the old override maps
        // and undid whatever the row chain had just settled, so a mesh hidden from the tree came
        // straight back on the next level or material refresh.
        for (const entry of this.context.particles.particleSystems.values()) {
            entry.levelVisible = proxyVisibleAt(
                entry.levels,
                this.alt,
                this.lod,
                this.altDescending,
            );
        }

        // And what the stencil pass counts, which follows the level and the cloak rather than the
        // reader. It was set at LOAD and by the pass's own setup and nowhere else, so a cloak
        // engaged after the model was standing left all eight volumes casting - a shadow on the
        // ground under a ship that is not being drawn. An ALT change had the same hole.
        this.context.assembly.forEachPartMesh((mesh) =>
            this.applyMeshVisibility(mesh),
        );

        // A passive subject has no rows, so `refreshRows` below will never speak for it. Its own
        // file's rules are re-applied instead - the same sweep that ran when it loaded.
        for (const part of this.context.parts.values()) {
            if (part.subjectId !== ACTIVE_SUBJECT) {
                this.context.assembly.applyOwnFileVisibility(
                    part.subjectId,
                    part.root,
                );
            }
        }

        this.context.rows.refreshRows();
    }

    /**
     * Applies BOTH halves of a sub-mesh's visibility: whether it is drawn, and - for a shadow
     * volume - whether it is counted.
     *
     * The two are separate questions with the same answer source. A shadow volume is never drawn,
     * so the drawn half always says no, while the level gating that decides which ALT's volume is
     * the live one still has to reach the stencil pass. Splitting them across two sweeps is what
     * let ALT1's volume mark a model showing ALT0.
     */
    applyMeshVisibility(mesh: THREE.Mesh): void {
        // NOT the drawn state - that belongs to the row chain, which is the only writer. This keeps
        // the shadow-volume counting in step, which follows the LEVEL rather than the reader.

        const extras = (mesh.userData.alamo ?? {}) as MaterialExtras;

        if (resolveMaterial(extras).archetype === "shadow-volume") {
            // A cloaked unit casts nothing. The volume is never DRAWN either way, so this is the
            // only place the cloak can reach it - and a stencil shadow lying on the ground under an
            // invisible ship is exactly the "something else is still shown" the swap rules out.
            this.context.environment.shadowVolumes.setCounting(
                mesh,
                !this.stealthed && isVisibleAt(extras, this.alt, this.lod),
            );
        }
    }

    /**
     * Whether a mesh actually reaches the screen: its own flag AND every ancestor's.
     *
     * A mesh with no ALT or LOD tag is always drawn unless it is hidden itself or is attached to a hidden
     * bone - and that second half is the part `node.visible` alone cannot answer, because three
     * stops at the first invisible ancestor without touching the flag on anything below it. Counting
     * only the node's own flag reported meshes as drawn while the screen showed nothing.
     */
    drawn(mesh: THREE.Mesh): boolean {
        // The mesh's own layer, then every ancestor's `visible`. Two mechanisms because they mean
        // different things: a mesh switches ITSELF off without taking its bones with it, a bone
        // switches off everything hanging beneath it. See `meshVisibility.ts`.
        if (!meshDrawn(mesh)) {
            return false;
        }

        for (
            let at: THREE.Object3D | null = mesh;
            at !== null;
            at = at.parent
        ) {
            if (!at.visible) {
                return false;
            }

            if (at === this.context.modelRoot) {
                break;
            }
        }

        return true;
    }
}
