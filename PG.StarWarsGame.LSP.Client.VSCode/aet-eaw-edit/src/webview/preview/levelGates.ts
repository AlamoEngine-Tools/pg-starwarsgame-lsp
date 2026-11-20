// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The viewport's levels: the ALT (damage) and LOD (detail) level the model is shown at - the levels the
// file itself tags, which both previews select among - and the one answer per mesh of whether it is drawn
// at all. The game's rules over the file (decals, collision hulls, shield, stealth) are `UnitRules`'.

import { ACTIVE_SUBJECT } from "./loadedPart";
import * as THREE from "three";
import { meshDrawn } from "./meshVisibility";
import { type ViewportContext } from "./viewportContext";

export class LevelGates {
    alt = 0;
    lod = 0;
    /**
     * Whether the last ALT change was downwards.
     *
     * A property of the transition, not of the state - it is worked out on every damage-level change,
     * and a LOD change counts as not downwards.
     */
    altDescending = false;

    constructor(private readonly context: ViewportContext) {}

    /**
     * Sets the damage state and detail level, the way the engine gates geometry.
     *
     * The DIRECTION matters, not just the value: a proxy marked `altDecreaseStayHidden` is held back
     * while the damage level winds down. A LOD change counts as not downwards, because winding detail
     * down is not repairing anything.
     */
    setLevels(alt: number, lod: number): void {
        // Measured: a detail level change gives a past stealth's shadow volumes back - see
        // `cloakFade.ts`.
        if (lod !== this.lod) {
            this.context.animation.cloak.releaseVolumes();
        }

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
        this.context.particles.gateByLevel(
            this.alt,
            this.lod,
            this.altDescending,
        );

        // A passive subject has no rows, so `refreshRows` below will never speak for it. Its own
        // file's rules are re-applied instead - the same sweep that ran when it loaded. Both write
        // what the stencil pass counts as well, by each volume's own switches - see `castsShadow`.
        for (const part of this.context.parts.values()) {
            if (part.subjectId !== ACTIVE_SUBJECT) {
                this.context.assembly.applyOwnFileVisibility(part);
            }
        }

        this.context.rows.refreshRows();
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
