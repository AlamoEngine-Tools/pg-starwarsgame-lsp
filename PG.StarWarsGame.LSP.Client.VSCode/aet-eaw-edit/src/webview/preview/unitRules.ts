// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The game's rules, as a layer over the model file's own answer.
//
// Two previews look at a model. The MODEL preview - a model file opened directly - shows the file
// exactly as it is: its mesh and proxy hidden flags, and the tracks of the clip being played. A wrong
// default in the file is then something the author can see. The UNIT preview - a game object opened
// from its XML - shows what the game draws, which is that same answer with the game's rules written
// over it: the hardpoint rules, the abilities, the XML gates and the state a unit is created in.
//
// One implementation, never two. `ModelRows` works out the file's answer for every row; this layer
// takes that answer and returns the unit's, and in a model preview it returns it untouched. Which of
// the two a preview is, is decided once, by what was opened, and does not change for its life.

import { PREVIEW_SCENE_KIND } from "../../protocol/modelPreview";
import { type MaterialExtras, isShieldMesh, isStealthMesh } from "./materials";
import { type RowFacts, damageMeshFacts } from "./visibility";
import { type ViewportContext } from "./viewportContext";

/** Whose rules decide what a preview draws: the model file's alone, or the game's on top of it. */
export type PreviewRules = "file" | "game";

/**
 * The rules a preview takes, from what was opened.
 *
 * A game object is the game's subject, so it takes the game's rules. Everything opened as a file -
 * a model, an animation, a particle system - is the file's, and shows it as it is.
 */
export function rulesFor(sceneKind: string): PreviewRules {
    return sceneKind === PREVIEW_SCENE_KIND.object ? "game" : "file";
}

export class UnitRules {
    /** Null until the first scene says which preview this is; then fixed. */
    private rules: PreviewRules | null = null;

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
     * mesh IS. Unlike a decal there is no "shown" half.
     */
    collisionMeshes: ReadonlySet<string> = new Set();
    /**
     * Whether an active ability is holding the SHIELD MESH visible.
     *
     * The mesh ships hidden - `MeshShield.fx` carries `alamoHidden: true` - so this is the unit being
     * told it does draw it after all, which is why it writes `inFile` rather than adding a gate.
     */
    shieldRevealed = false;
    /**
     * Whether a cloak ability is engaged, in which case the STEALTH SHELL is the only thing drawn.
     *
     * A swap, not a reveal. The shell ships VISIBLE on all nine models that carry one, so a unit
     * that is not cloaked has it hidden by this layer, and a cloaked one has everything else hidden.
     */
    stealthed = false;

    constructor(private readonly context: ViewportContext) {}

    /**
     * Fixes which preview this is. The first call decides; any later one is ignored, because a
     * preview is a model preview or a unit preview for its whole life.
     */
    setRules(rules: PreviewRules): void {
        if (this.rules !== null) {
            return;
        }

        this.rules = rules;
        this.context.levels.applyLevels();
    }

    /** Whether the game's rules are written over the file's answer here. */
    appliesGameRules(): boolean {
        return this.rules === "game";
    }

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
     * a mesh.
     */
    setDecals(known: ReadonlySet<string>, shown: ReadonlySet<string>): void {
        this.knownDecals = known;
        this.shownDecals = shown;
        this.context.levels.applyLevels();
    }

    /** Which meshes the hardpoints claim as collision hulls. */
    setCollisionMeshes(names: ReadonlySet<string>): void {
        this.collisionMeshes = names;
        this.context.levels.applyLevels();
    }

    /** Shows or hides the shield mesh, for the abilities that reveal it. */
    setShieldRevealed(revealed: boolean): void {
        this.shieldRevealed = revealed;
        this.context.levels.applyLevels();
    }

    /**
     * Cloaks the unit, or uncloaks it.
     *
     * Through the level pass like the shield, because the row chain is the one writer of drawn
     * state. Setting `visible` on the meshes here would be a second writer.
     */
    setStealthed(on: boolean): void {
        this.stealthed = on;
        this.context.levels.applyLevels();
    }

    /** Whether a cloak is engaged. Never in a model preview, which applies no ability. */
    isStealthed(): boolean {
        return this.appliesGameRules() && this.stealthed;
    }

    /**
     * A mesh row's facts with the game's rules written over the file's answer.
     *
     * @param levelGated What the damage and detail levels alone say, without the file's own hidden
     *     flag. The shield is the one mesh an ability can talk into being drawn, so the flag it ships
     *     with must not veto it a second time from the level link.
     */
    meshFacts(
        facts: RowFacts,
        extras: MaterialExtras,
        levelGated: boolean,
    ): RowFacts {
        if (!this.appliesGameRules()) {
            return facts;
        }

        const name = (extras.alamoMesh ?? "").toLowerCase();

        // By the MESH, not the archetype: the shield shader is the engine glow's too on some
        // models, and archetype alone lit the engines with the ability.
        const shield = isShieldMesh(extras);

        // The level gate is left alone for the shell, unlike the shield's - `Ui_tyber` and
        // `Ui_urai_fen` ship `Stealth_LOD0/1/2`, and a shell exempted from LOD would draw all three.
        const stealth = isStealthMesh(extras);

        const unit: RowFacts = {
            ...facts,
            inFile: shield
                ? this.shieldRevealed
                : stealth
                  ? this.stealthed
                  : facts.inFile,
            gated:
                (shield ? levelGated : facts.gated) ||
                this.collisionMeshes.has(name) ||
                (this.stealthed && !stealth),
        };

        // A damage decal's bottom of the chain is the DAMAGE RULE, not the file - see
        // `damageMeshFacts`.
        return this.knownDecals.has(name)
            ? damageMeshFacts(unit, this.shownDecals.has(name))
            : unit;
    }

    /**
     * An effect row's facts with the game's rules written over the file's answer.
     *
     * A cloaked unit shows its stealth shell and nothing else - not its engines, not its damage
     * smoke, not the proxies of the ability doing the cloaking. `gated` and not `inFile` because it
     * is a state, and it leaves the effect's own gate holding what the effect rules last decided, so
     * uncloaking restores exactly what was playing.
     */
    effectFacts(facts: RowFacts): RowFacts {
        return this.isStealthed() ? { ...facts, gated: true } : facts;
    }

    /**
     * A row's facts with the unit standing in its idle clip.
     *
     * A unit that is not doing anything plays its idle, and the preview reads that clip's first
     * frame while nothing else plays. A model file opened on its own plays nothing it is not asked
     * to, so a model preview takes no idle: the file is the answer there.
     *
     * @param idle What the idle clip's first frame says about the row, or undefined when the model
     *     ships no idle, the idle says nothing about it, or another clip is playing.
     */
    idleFacts(facts: RowFacts, idle: boolean | undefined): RowFacts {
        return this.appliesGameRules() ? { ...facts, resting: idle } : facts;
    }
}
