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
import { type HardpointDeath, type HardpointWrites } from "./damage";
import { cloakWrite, hiddenOnTypeLoad } from "./cloakWrites";
import {
    createdWrite,
    EFFECT_GROUP,
    inGroup,
    switchWrite,
} from "./effectGroups";
import { type MaterialExtras, isShieldMesh } from "./materials";
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

/** A unit with no hardpoint: nothing written on creation, no death. */
const NO_HARDPOINTS: HardpointWrites = {
    decals: new Set(),
    damageBones: new Set(),
    deaths: [],
    enginesOffline: false,
};

/** The behaviour whose object hides its `pi` effects until it is ion stunned. */
const ION_STUN_BEHAVIOR = "ION_STUN_EFFECT";
/** The behaviour that moves a ship, and keeps its engine effects hidden while its engines are offline. */
const SPACE_LOCOMOTOR_BEHAVIOR = "SIMPLE_SPACE_LOCOMOTOR";

const NO_NAMES: ReadonlySet<string> = new Set();

/** One thing that happened to the unit after it was created, in the order it happened. */
type UnitEvent =
    | { kind: "death"; death: HardpointDeath }
    | { kind: "ability"; type: string; on: boolean };

export class UnitRules {
    /** Null until the first scene says which preview this is; then fixed. */
    private rules: PreviewRules | null = null;

    /**
     * What the hardpoints write to the state switch of the unit's parts: on creation, and in the
     * order they died. See {@link setHardpoints}.
     */
    hardpoints: HardpointWrites = NO_HARDPOINTS;
    /**
     * Whether an active ability is holding the SHIELD MESH visible.
     *
     * The mesh ships hidden - `MeshShield.fx` carries `alamoHidden: true` - so this is the unit being
     * told it does draw it after all, which is why it writes the state switch.
     */
    shieldRevealed = false;
    /**
     * Whether the unit's shadow VOLUMES are off because of its cloak - measured, and only the volumes,
     * not the shadow map. Written by whatever steps the cloak - see `cloakFade.ts` for when they
     * come back.
     */
    cloakShadowsOff = false;
    /**
     * The abilities and power modes switched on. Each writes the effect groups it switches when it
     * goes on and again when it goes off - see `effectGroups.ts`.
     */
    activeAbilities: ReadonlySet<string> = new Set();
    /**
     * Whether the unit's type is a missile-shield object - its `Passive_Missile_Shield_Radius` is
     * above 0 - which shows its `prs` effects once it is created.
     */
    missileShieldObject = false;
    /**
     * The behaviours the unit's type lists, upper-cased. What the game does to a unit depends on
     * them: see {@link effectState} for the ion stun effects and the engines going offline.
     */
    private behaviors: ReadonlySet<string> = new Set();

    /**
     * When each death and each ability switch happened, on one clock, so the effect writes replay in
     * the order they happened. A repaired hardpoint's death leaves; an ability keeps only its LAST
     * switch, because a later switch of the same ability writes every group the earlier one did.
     */
    private clock = 0;
    private readonly deathAt = new Map<string, number>();
    private readonly abilityAt = new Map<string, { on: boolean; at: number }>();

    /**
     * The lowercased names of each hull bone and of every bone above it, worked out once per hull:
     * what a rule naming a bone reaches. Asked once per row on every frame of a playing clip.
     */
    private namesAbove: {
        hull: unknown;
        names: ReadonlyMap<number, ReadonlySet<string>>;
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
        this.hardpoints = NO_HARDPOINTS;
        this.namesAbove = null;
        this.shieldRevealed = false;
        this.cloakShadowsOff = false;
        this.activeAbilities = new Set();
        this.missileShieldObject = false;
        this.behaviors = new Set();
        this.clock = 0;
        this.deathAt.clear();
        this.abilityAt.clear();
    }

    /**
     * What the hardpoints write: every `Damage_Decal` and `Damage_Particles` bone, and each death in
     * the order the hardpoints died - see `hardpointWrites`.
     *
     * Measured in the game, as writes of each part's state switch, the last of which is what the part
     * has:
     *
     * - When the unit's type loads, every `Damage_Decal` part is written hidden.
     * - When a hardpoint is created, every part on its `Damage_Particles` bone or at any depth below
     *   it - meshes, effects, attached models - is written hidden.
     * - When a hardpoint dies: its decal is written shown, then every part on or below its
     *   `Damage_Particles` bone shown, whatever the file says, then - with
     *   `Engine_Death_Hide_Engine_Particles` - every part on or below its `Engine_Particles` bone
     *   hidden. Nothing writes them again, so where two hardpoints' bones overlap the later death
     *   decides.
     */
    setHardpoints(writes: HardpointWrites): void {
        this.hardpoints = writes;

        // A death is placed on the clock when it is first seen, and leaves it with its repair.
        const dead = new Set(writes.deaths.map((death) => death.hardpoint));
        for (const id of [...this.deathAt.keys()]) {
            if (!dead.has(id)) {
                this.deathAt.delete(id);
            }
        }
        for (const death of writes.deaths) {
            if (!this.deathAt.has(death.hardpoint)) {
                this.deathAt.set(death.hardpoint, ++this.clock);
            }
        }

        this.context.levels.applyLevels();
    }

    /** Shows or hides the shield mesh, for the abilities that reveal it. */
    setShieldRevealed(revealed: boolean): void {
        this.shieldRevealed = revealed;
        this.context.levels.applyLevels();
    }

    /**
     * The abilities switched on. Each one that went on or off since the last call is placed on the
     * unit's clock now; their effect groups follow - see {@link effectState}.
     */
    setActiveAbilities(active: ReadonlySet<string>): void {
        for (const type of this.activeAbilities) {
            if (!active.has(type)) {
                this.abilityAt.set(type, { on: false, at: ++this.clock });
            }
        }
        for (const type of active) {
            if (!this.activeAbilities.has(type)) {
                this.abilityAt.set(type, { on: true, at: ++this.clock });
            }
        }

        this.activeAbilities = active;
        this.context.levels.applyLevels();
    }

    /** Whether the unit's type is a missile-shield object. See {@link missileShieldObject}. */
    setMissileShieldObject(on: boolean): void {
        this.missileShieldObject = on;
        this.context.levels.applyLevels();
    }

    /** The behaviours the unit's type lists, however cased. See {@link behaviors}. */
    setBehaviors(behaviors: readonly string[]): void {
        this.behaviors = new Set(
            behaviors.map((behavior) => behavior.trim().toUpperCase()),
        );
        this.context.levels.applyLevels();
    }

    /**
     * Whether the unit's shadow volumes are off because of its cloak. Through the level pass, because
     * the row chain decides what each shadow volume counts.
     */
    setCloakShadowsOff(off: boolean): void {
        if (this.cloakShadowsOff === off) {
            return;
        }

        this.cloakShadowsOff = off;
        this.context.levels.applyLevels();
    }

    /** Whether the unit's shadow volumes are off. Never in a model preview, which applies no ability. */
    shadowsOff(): boolean {
        return this.appliesGameRules() && this.cloakShadowsOff;
    }

    /**
     * A mesh's state switch with the game's rules written over the file's write, in the order the
     * preview applies them; the last write is what the mesh has.
     *
     * A mesh inside a model a hardpoint attaches is evaluated PER MODEL. Measured in the game: the
     * attached model is one part of the unit, a model inside the model. A rule that names a part -
     * `Damage_Decal` - is compared with the unit model's own parts and never looks inside it; a
     * rule that reaches it - a hardpoint's bone it hangs on or below, a cloak - writes the MODEL's
     * one switch; and a part inside it is drawn only while the model is, as its own switches say.
     * So nothing the unit writes can show what the attached model's file hides, a collision hull
     * among them. Reported on a mod's star destroyer: its bow weapons are attached models whose
     * one drawn mesh is named like the hardpoint's decal on the hull, and the decal rule, reaching
     * inside, hid the weapons.
     *
     * @param bone The hull bone the mesh is on - for a mesh of an attached model, the bone the
     *     model hangs on - for the rules that name a bone.
     * @param attached Whether the mesh belongs to a model a hardpoint attaches.
     */
    meshState(
        state: StateSwitch,
        extras: MaterialExtras,
        bone?: number,
        attached = false,
    ): StateSwitch {
        if (!this.appliesGameRules()) {
            return state;
        }

        const meshName = extras.alamoMesh ?? "";
        const name = meshName.toLowerCase();
        let written = state;

        // By the MESH, not the archetype: the shield shader is the engine glow's too on some
        // models, and archetype alone lit the engines with the ability.
        if (isShieldMesh(extras)) {
            written = ruleState(this.shieldRevealed);
        }

        // Loading the type hides every part named for a cloak or a blast - see `hiddenOnTypeLoad`.
        // The stealth shell ships VISIBLE on all nine models that carry one; a cloak going on shows
        // it. Its LOD variants - `Ui_tyber` and `Ui_urai_fen` ship `Stealth_LOD0/1/2` - are still
        // gated by the detail level.
        if (hiddenOnTypeLoad(meshName)) {
            written = ruleState(false);
        }

        // What the hardpoints write on creation, then every death and every cloak in the one order
        // they happened: to the part itself in the unit's own model, to the model it is in when a
        // hardpoint attaches that - which starts out shown, and takes no write that names a part.
        const above = this.bonesAbove(bone);
        let model: StateSwitch = ruleState(true);

        if (attached) {
            model = this.creationHardpointWrite(model, above, null);
        } else {
            written = this.creationHardpointWrite(written, above, name);
        }

        for (const event of this.timeline()) {
            if (event.kind === "death") {
                if (attached) {
                    model = this.deathWrite(model, event.death, above, null);
                } else {
                    written = this.deathWrite(
                        written,
                        event.death,
                        above,
                        name,
                    );
                }
            } else {
                // By its name, the part itself; as an attached model, the model.
                const cloaked = cloakWrite(
                    event.type,
                    event.on,
                    meshName,
                    false,
                    undefined,
                );
                if (cloaked !== undefined) {
                    written = ruleState(cloaked);
                }

                const hidesModel = attached
                    ? cloakWrite(event.type, event.on, "", true, undefined)
                    : undefined;
                if (hidesModel !== undefined) {
                    model = ruleState(hidesModel);
                }
            }
        }

        // A hidden model draws none of its parts, whatever they say themselves.
        return attached && !model.shown ? model : written;
    }

    /**
     * An effect's state switch with the game's rules written over it.
     *
     * In the order the game writes them: what creating the unit writes - its name group (`pte`,
     * `pptw` and `prs` hidden, a missile-shield object's `prs` shown, `pi` hidden on an object with
     * the ion stun behaviour) and every Damage_Particles bone - then every hardpoint death and
     * every ability switch, the cloaks among them, in the one order they happened.
     *
     * And last, while the unit's engines are offline, its engine effects hidden. Measured in the
     * game: an engine hardpoint dying takes the engines offline when the unit has none left
     * undestroyed, and the space locomotor then hides the engine group it is on - `pe`, or `pte`
     * under turbo - on every service, so nothing written after it shows them. Turbo is switched off
     * with the engines, which hides `pte`: both groups are off. A hardpoint's `Engine_Particles`
     * plays no part in this - on the Star Destroyer the glow is not below that bone - and neither
     * does a unit the space locomotor does not move.
     *
     * @param bone The hull bone the effect is anchored on, for the rules that name a bone.
     * @param name The effect's proxy name, for the rules that read it; none for an effect the model
     *     does not carry.
     * @param attached Whether the effect belongs to a model a hardpoint attaches.
     */
    effectState(
        state: StateSwitch,
        bone?: number,
        name?: string,
        attached = false,
    ): StateSwitch {
        if (!this.appliesGameRules()) {
            return state;
        }

        // Loading the type first, then creating the unit - see `hiddenOnTypeLoad`.
        let written =
            name !== undefined && hiddenOnTypeLoad(name)
                ? ruleState(false)
                : state;
        written = this.createdWrite(
            written,
            name,
            this.missileShieldObject,
            this.behaviors.has(ION_STUN_BEHAVIOR),
        );
        const above = this.bonesAbove(bone);

        written = this.creationHardpointWrite(written, above, null);

        for (const event of this.timeline()) {
            if (event.kind === "death") {
                written = this.deathWrite(written, event.death, above, null);
            } else {
                const switched = cloakWrite(
                    event.type,
                    event.on,
                    name ?? "",
                    attached,
                    name === undefined
                        ? undefined
                        : switchWrite(name, event.type, event.on, undefined),
                );
                if (switched !== undefined) {
                    written = ruleState(switched);
                }
            }
        }

        if (
            name !== undefined &&
            this.hardpoints.enginesOffline &&
            this.behaviors.has(SPACE_LOCOMOTOR_BEHAVIOR) &&
            (inGroup(name, EFFECT_GROUP.engines) ||
                inGroup(name, EFFECT_GROUP.turboEngines))
        ) {
            written = ruleState(false);
        }

        return written;
    }

    /**
     * The state switch of an effect of ANOTHER object the preview shows - a death clone - with what
     * creating that object writes over it. None of the subject's hardpoint or ability writes reach
     * it: their bones and switches are the subject's.
     */
    createdEffectState(state: StateSwitch, name?: string): StateSwitch {
        return this.appliesGameRules()
            ? this.createdWrite(state, name, false, false)
            : state;
    }

    /** What creating an object writes to an effect of this name. See `effectGroups.createdWrite`. */
    private createdWrite(
        state: StateSwitch,
        name: string | undefined,
        missileShieldObject: boolean,
        ionStunObject: boolean,
    ): StateSwitch {
        const created =
            name === undefined
                ? undefined
                : createdWrite(name, missileShieldObject, ionStunObject);

        return created === undefined ? state : ruleState(created);
    }

    /** The deaths and the ability switches, in the order they happened. */
    private timeline(): UnitEvent[] {
        const placed: { at: number; event: UnitEvent }[] = [];

        for (const death of this.hardpoints.deaths) {
            placed.push({
                at: this.deathAt.get(death.hardpoint) ?? 0,
                event: { kind: "death", death },
            });
        }

        for (const [type, { on, at }] of this.abilityAt) {
            placed.push({ at, event: { kind: "ability", type, on } });
        }

        return placed.sort((a, b) => a.at - b.at).map((entry) => entry.event);
    }

    /**
     * The lowercased names of a hull bone and every bone above it; none without a bone. A hardpoint's
     * bone is matched by name, without case, and reaches every part on it or at any depth below it.
     */
    private bonesAbove(bone: number | undefined): ReadonlySet<string> {
        const { decals, damageBones, deaths } = this.hardpoints;

        if (
            bone === undefined ||
            (decals.size === 0 && damageBones.size === 0 && deaths.length === 0)
        ) {
            return NO_NAMES;
        }

        return this.namesAboveBones().get(bone) ?? NO_NAMES;
    }

    /** What the hardpoints write when the unit is created: its decals and Damage_Particles bones hidden. */
    private creationHardpointWrite(
        state: StateSwitch,
        above: ReadonlySet<string>,
        name: string | null,
    ): StateSwitch {
        const { decals, damageBones } = this.hardpoints;
        let written = state;

        // The unit's type loads: every decal hidden.
        if (name !== null && decals.has(name)) {
            written = ruleState(false);
        }

        // Each hardpoint is created: every part on or below its Damage_Particles bone hidden.
        for (const at of above) {
            if (damageBones.has(at)) {
                written = ruleState(false);
                break;
            }
        }

        return written;
    }

    /** What one hardpoint's death writes over a part on a bone below `above`, named `name`. */
    private deathWrite(
        state: StateSwitch,
        death: HardpointDeath,
        above: ReadonlySet<string>,
        name: string | null,
    ): StateSwitch {
        let written = state;

        if (name !== null && death.decal === name) {
            written = ruleState(true);
        }

        if (death.damageBone !== null && above.has(death.damageBone)) {
            written = ruleState(true);
        }

        if (death.engineBone !== null && above.has(death.engineBone)) {
            written = ruleState(false);
        }

        return written;
    }

    /** See {@link namesAbove}. */
    private namesAboveBones(): ReadonlyMap<number, ReadonlySet<string>> {
        const hull = this.context.assembly.hull();

        if (this.namesAbove !== null && this.namesAbove.hull === hull) {
            return this.namesAbove.names;
        }

        const skeleton = this.context.queries.skeleton();
        const byIndex = new Map(skeleton.map((bone) => [bone.index, bone]));
        const names = new Map<number, ReadonlySet<string>>();

        for (const bone of skeleton) {
            const above = new Set<string>();
            // Bounded by the skeleton's size: a file-controlled parent chain that loops must not hang
            // the preview.
            let steps = skeleton.length;

            for (
                let at = byIndex.get(bone.index);
                at !== undefined && steps-- > 0;
                at = at.parent < 0 ? undefined : byIndex.get(at.parent)
            ) {
                above.add(at.name.toLowerCase());
            }

            names.set(bone.index, above);
        }

        this.namesAbove = { hull, names };

        return names;
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

    /**
     * Whether the unit stands in an idle clip while nothing else plays - which is a clip RUNNING, so
     * the file's bone flags do not apply. A unit whose model ships no idle stands in its base pose,
     * and a model preview always opens in it.
     */
    standsInIdle(modelHasIdle: boolean): boolean {
        return this.appliesGameRules() && modelHasIdle;
    }
}
