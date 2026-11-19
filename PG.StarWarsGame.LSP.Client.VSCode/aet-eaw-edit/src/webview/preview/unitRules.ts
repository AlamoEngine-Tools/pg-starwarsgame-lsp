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
// only WRITES the state switch over it, the way the game's rules do - last write wins - and in a
// model preview it writes nothing. It never touches the AND of the four switches in `visibility.ts`;
// a rule added later is one more writer here. Which of the two a preview is, is decided once, by
// what was opened, and does not change for its life.

import { PREVIEW_SCENE_KIND } from "../../protocol/modelPreview";
import { type MaterialExtras, isShieldMesh, isStealthMesh } from "./materials";
import {
    type AnimationSwitch,
    type StateSwitch,
    ruleState,
} from "./visibility";
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
     * Each hardpoint's `Damage_Particles` bone, lowercased, and whether a hardpoint naming it is
     * destroyed. See {@link setDamageParticles}.
     */
    damageParticles: ReadonlyMap<string, boolean> = new Map();
    /**
     * Whether an active ability is holding the SHIELD MESH visible.
     *
     * The mesh ships hidden - `MeshShield.fx` carries `alamoHidden: true` - so this is the unit being
     * told it does draw it after all, which is why it writes the state switch.
     */
    shieldRevealed = false;
    /**
     * Whether a cloak ability is engaged, in which case the STEALTH SHELL is the only thing drawn.
     *
     * A swap, not a reveal. The shell ships VISIBLE on all nine models that carry one, so a unit
     * that is not cloaked has it hidden by this layer, and a cloaked one has everything else hidden.
     */
    stealthed = false;

    /**
     * What the `Damage_Particles` rule writes per hull bone, worked out once per hull and set of
     * bones: true for shown, false for hidden, absent for a bone no such bone is at or above. This is
     * asked once per row on every frame of a playing clip, and the answer only changes with those two.
     */
    private damageWrites: {
        hull: unknown;
        bones: ReadonlyMap<string, boolean>;
        writes: ReadonlyMap<number, boolean>;
    } | null = null;

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

    /** Drops the hardpoint and shield state of a model that is no longer shown. */
    forgetModel(): void {
        this.knownDecals = new Set();
        this.shownDecals = new Set();
        this.collisionMeshes = new Set();
        this.damageParticles = new Map();
        this.damageWrites = null;
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

    /**
     * Each hardpoint's `Damage_Particles` bone, lowercased, and whether a hardpoint naming it is
     * destroyed - see `damageParticleBones`.
     *
     * Measured in the game: when a hardpoint is created, every part on its `Damage_Particles` bone or
     * at any depth below it - meshes, effects, attached models - has its state written hidden; when the
     * hardpoint is destroyed they are all written shown, whatever the model file says about them, and
     * nothing hides them again. A bone, not a name: `Damage_Decal` is the rule for one named part.
     */
    setDamageParticles(bones: ReadonlyMap<string, boolean>): void {
        this.damageParticles = bones;
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
     * A mesh's state switch with the game's rules written over the file's write, in the order the
     * preview applies them; the last write is what the mesh has.
     *
     * @param bone The hull bone the mesh is on, for the rules that name a bone.
     */
    meshState(
        state: StateSwitch,
        extras: MaterialExtras,
        bone?: number,
    ): StateSwitch {
        if (!this.appliesGameRules()) {
            return state;
        }

        const name = (extras.alamoMesh ?? "").toLowerCase();
        let written = state;

        // By the MESH, not the archetype: the shield shader is the engine glow's too on some
        // models, and archetype alone lit the engines with the ability.
        if (isShieldMesh(extras)) {
            written = ruleState(this.shieldRevealed);
        }

        // The shell ships VISIBLE, so the cloak says everything about it. Its LOD variants -
        // `Ui_tyber` and `Ui_urai_fen` ship `Stealth_LOD0/1/2` - are still gated by the detail level.
        const stealth = isStealthMesh(extras);
        if (stealth) {
            written = ruleState(this.stealthed);
        }

        // A damage decal is off while its hardpoint is whole and on once it is destroyed, whatever
        // the file says - the shipped art marks none of them hidden.
        if (this.knownDecals.has(name)) {
            written = ruleState(this.shownDecals.has(name));
        }

        // After the decal: a hardpoint is created after its type loaded, and both rules show on
        // destruction anyway.
        written = this.damageParticlesWrite(written, bone);

        if (this.collisionMeshes.has(name)) {
            written = ruleState(false);
        }

        // Last, because a cloaked unit draws its shell and nothing else.
        if (this.stealthed && !stealth) {
            written = ruleState(false);
        }

        return written;
    }

    /**
     * An effect's state switch with the game's rules written over it.
     *
     * A cloaked unit shows its stealth shell and nothing else - not its engines, not its damage
     * smoke, not the proxies of the ability doing the cloaking. Written here, over the effect's own
     * state, rather than into it: the effect keeps what the effect rules last decided, so uncloaking
     * restores exactly what was playing.
     *
     * @param bone The hull bone the effect is anchored on, for the rules that name a bone.
     */
    effectState(state: StateSwitch, bone?: number): StateSwitch {
        if (!this.appliesGameRules()) {
            return state;
        }

        return this.stealthed
            ? ruleState(false)
            : this.damageParticlesWrite(state, bone);
    }

    /** The `Damage_Particles` rule's write over a part on `bone`, or `state` when it writes nothing. */
    private damageParticlesWrite(
        state: StateSwitch,
        bone: number | undefined,
    ): StateSwitch {
        if (bone === undefined || this.damageParticles.size === 0) {
            return state;
        }

        const write = this.damageWritesByBone().get(bone);

        return write === undefined ? state : ruleState(write);
    }

    /**
     * Per hull bone, what the `Damage_Particles` rule writes: shown when any such bone at or above it
     * belongs to a destroyed hardpoint, hidden when every one does to a living hardpoint.
     *
     * Shown wins because only a destruction writes after creation, and it writes shown. The bone is
     * matched by name, without case, as the hardpoint's tag names it.
     */
    private damageWritesByBone(): ReadonlyMap<number, boolean> {
        const hull = this.context.assembly.hull();

        if (
            this.damageWrites !== null &&
            this.damageWrites.hull === hull &&
            this.damageWrites.bones === this.damageParticles
        ) {
            return this.damageWrites.writes;
        }

        const skeleton = this.context.queries.skeleton();
        const byIndex = new Map(skeleton.map((bone) => [bone.index, bone]));
        const writes = new Map<number, boolean>();

        for (const bone of skeleton) {
            let write: boolean | undefined;
            // Bounded by the skeleton's size: a file-controlled parent chain that loops must not hang
            // the preview.
            let steps = skeleton.length;

            for (
                let at = byIndex.get(bone.index);
                at !== undefined && steps-- > 0;
                at = at.parent < 0 ? undefined : byIndex.get(at.parent)
            ) {
                const named = this.damageParticles.get(at.name.toLowerCase());

                if (named !== undefined) {
                    write = (write ?? false) || named;
                }
            }

            if (write !== undefined) {
                writes.set(bone.index, write);
            }
        }

        this.damageWrites = { hull, bones: this.damageParticles, writes };

        return writes;
    }

    /**
     * The animation switch of a unit standing in its idle clip.
     *
     * A unit that is not doing anything plays its idle, and the preview reads that clip's first
     * frame while nothing else plays. A model file opened on its own plays nothing it is not asked
     * to, so a model preview takes no idle: the file is the answer there.
     *
     * @param idle What the idle clip's first frame says about the row, or undefined when the model
     *     ships no idle, the idle says nothing about it, or another clip is playing.
     */
    idle(idle: boolean | undefined): AnimationSwitch | undefined {
        return this.appliesGameRules() && idle !== undefined
            ? { shown: idle, source: "idle" }
            : undefined;
    }
}
