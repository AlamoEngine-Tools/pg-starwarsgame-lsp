// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
    PREVIEW_SCENE_KIND,
    type PreviewHardpoint,
} from "../../protocol/modelPreview";
import * as damage from "./damage";
import {
    type HardpointDeath,
    type HardpointWrites,
    hardpointWrites,
} from "./damage";
import { fileState, ruleState } from "./visibility";
import { type PreviewRules, UnitRules, rulesFor } from "./unitRules";
import { standInContext } from "./viewportContext.testing";

/**
 * The hull's skeleton the rules see: Root, then HP_Bone under it, EmitDamage under that and an effect
 * bone under that, and a Hull_Other bone beside HP_Bone.
 */
const SKELETON = [
    { index: 0, name: "Root", parent: -1, visible: true },
    { index: 1, name: "HP_Bone", parent: 0, visible: true },
    { index: 2, name: "EmitDamage", parent: 1, visible: true },
    { index: 3, name: "p_smoke", parent: 2, visible: true },
    { index: 4, name: "Hull_Other", parent: 0, visible: true },
];

/** The rules over a stand-in viewport, counting how often the level pass was asked to run. */
function rulesWith(rules?: PreviewRules): {
    unit: UnitRules;
    applied: () => number;
} {
    let applied = 0;
    const hull = {};
    const unit = new UnitRules(
        standInContext({
            levels: {
                applyLevels: () => {
                    applied++;
                },
            },
            queries: { skeleton: () => SKELETON },
            assembly: { hull: () => hull },
        }),
    );

    if (rules !== undefined) {
        unit.setRules(rules);
    }

    return { unit, applied: () => applied };
}

/** What the hardpoints write: nothing on creation and no death, unless a test says otherwise. */
function writes(over: Partial<HardpointWrites> = {}): HardpointWrites {
    return {
        decals: new Set(),
        damageBones: new Set(),
        deaths: [],
        enginesOffline: false,
        ...over,
    };
}

let deathCount = 0;

/** One hardpoint's death, of a hardpoint of its own, writing nothing unless a test says otherwise. */
function death(over: Partial<HardpointDeath>): HardpointDeath {
    return {
        hardpoint: `HP_${deathCount++}`,
        decal: null,
        damageBone: null,
        engineBone: null,
        ...over,
    };
}

/** The state switch of a part the file draws. */
const drawnInFile = fileState(true);

describe("rulesFor", () => {
    it("takes the game's rules for a game object and the file's for anything opened as a file", () => {
        assert.equal(rulesFor(PREVIEW_SCENE_KIND.object), "game");
        assert.equal(rulesFor(PREVIEW_SCENE_KIND.model), "file");
        assert.equal(rulesFor(PREVIEW_SCENE_KIND.particle), "file");
    });
});

describe("UnitRules", () => {
    it("applies no game rule before a preview has said which it is", () => {
        const { unit } = rulesWith();

        assert.equal(unit.appliesGameRules(), false);
    });

    it("is decided once, by the first scene, for the preview's life", () => {
        const { unit, applied } = rulesWith("game");

        unit.setRules("file");

        assert.equal(unit.appliesGameRules(), true);
        assert.equal(applied(), 1);
    });

    it("leaves the model file's answer alone in a model preview", () => {
        const { unit } = rulesWith("file");
        unit.setHardpoints(writes({ decals: new Set(["hp_blast"]) }));
        unit.setActiveAbilities(new Set(["STEALTH"]));
        unit.setCloakShadowsOff(true);

        for (const alamoMesh of ["HP_Blast", "stealth", "Hull"]) {
            assert.deepEqual(
                unit.meshState(drawnInFile, { alamoMesh }),
                drawnInFile,
            );
        }
        assert.deepEqual(unit.effectState(drawnInFile), drawnInFile);
        assert.equal(unit.idle(false), undefined);
        assert.equal(unit.shadowsOff(), false);
    });

    it("hides the stealth shell of a unit until it is cloaked", () => {
        const { unit } = rulesWith("game");
        const shell = { alamoMesh: "stealth" };

        assert.deepEqual(unit.meshState(drawnInFile, shell), ruleState(false));

        unit.setActiveAbilities(new Set(["STEALTH"]));

        assert.deepEqual(unit.meshState(drawnInFile, shell), ruleState(true));
    });

    /**
     * Measured: loading the type hides every part whose name CONTAINS `STEALTH`, `buildup` or
     * `charged`, case ignored - meshes and effects alike.
     */
    it("hides every part whose name contains STEALTH, buildup or charged when the type loads", () => {
        const { unit } = rulesWith("game");

        for (const alamoMesh of [
            "Phantom_Stealth_Shell",
            "buildup",
            "Hull_Charged",
        ]) {
            assert.deepEqual(
                unit.meshState(drawnInFile, { alamoMesh }),
                ruleState(false),
                alamoMesh,
            );
        }
        for (const name of [
            "p_weapon_buildup",
            "p_weapon_charged",
            "p_stealth_glow",
        ]) {
            assert.deepEqual(
                unit.effectState(drawnInFile, undefined, name),
                ruleState(false),
                name,
            );
        }
        assert.deepEqual(
            unit.meshState(drawnInFile, { alamoMesh: "Hull" }),
            drawnInFile,
        );
    });

    it("shows a unit's shield mesh only while an ability holds it up", () => {
        const { unit } = rulesWith("game");
        const shield = { alamoMesh: "shield", alamoHidden: true };

        assert.deepEqual(
            unit.meshState(fileState(false), shield),
            ruleState(false),
        );

        unit.setShieldRevealed(true);

        assert.deepEqual(
            unit.meshState(fileState(false), shield),
            ruleState(true),
        );
    });

    it("hides a unit's damage decals until the hardpoint is destroyed", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(writes({ decals: new Set(["hp_blast"]) }));

        assert.deepEqual(
            unit.meshState(drawnInFile, { alamoMesh: "HP_Blast" }),
            ruleState(false),
        );

        unit.setHardpoints(
            writes({
                decals: new Set(["hp_blast"]),
                deaths: [death({ decal: "hp_blast" })],
            }),
        );

        assert.deepEqual(
            unit.meshState(fileState(false), { alamoMesh: "HP_Blast" }),
            ruleState(true),
            "a destroyed hardpoint's decal is not shown over the file",
        );
    });

    it("stands a unit in its idle clip", () => {
        const { unit } = rulesWith("game");

        assert.deepEqual(unit.idle(false), { shown: false, source: "idle" });
        assert.equal(unit.idle(undefined), undefined);
    });

    it("forgets a model's hardpoints and shield", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                decals: new Set(["hp_decal"]),
                damageBones: new Set(["hp_bone"]),
                deaths: [death({ decal: "hp_decal", engineBone: "hp_bone" })],
            }),
        );
        unit.setShieldRevealed(true);

        unit.forgetModel();

        assert.equal(unit.hardpoints.decals.size, 0);
        assert.equal(unit.hardpoints.damageBones.size, 0);
        assert.deepEqual(unit.hardpoints.deaths, []);
        assert.equal(unit.shieldRevealed, false);
    });
});

describe("UnitRules, a hardpoint's Damage_Particles bone", () => {
    const mesh = { alamoMesh: "Plate" };

    it("hides every part on the bone or at any depth below it while the hardpoint lives", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(writes({ damageBones: new Set(["hp_bone"]) }));

        for (const bone of [1, 2, 3]) {
            assert.deepEqual(
                unit.meshState(drawnInFile, mesh, bone),
                ruleState(false),
                `mesh on bone ${bone}`,
            );
            assert.deepEqual(
                unit.effectState(drawnInFile, bone),
                ruleState(false),
                `effect on bone ${bone}`,
            );
        }
    });

    it("leaves every part outside that subtree as it was", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(writes({ damageBones: new Set(["hp_bone"]) }));

        assert.deepEqual(unit.meshState(drawnInFile, mesh, 0), drawnInFile);
        assert.deepEqual(unit.meshState(drawnInFile, mesh, 4), drawnInFile);
        assert.deepEqual(unit.effectState(drawnInFile, 4), drawnInFile);
    });

    it("shows them all once it is destroyed, whatever the file says, and the bone is matched by name", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                damageBones: new Set(["hp_bone"]),
                deaths: [death({ damageBone: "hp_bone" })],
            }),
        );

        assert.deepEqual(
            unit.meshState(fileState(false), mesh, 2),
            ruleState(true),
        );
        assert.deepEqual(
            unit.effectState(fileState(false), 3),
            ruleState(true),
        );
    });

    it("comes after the decal rule, and the cloak still comes after it", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                decals: new Set(["plate"]),
                damageBones: new Set(["hp_bone"]),
                deaths: [death({ damageBone: "hp_bone" })],
            }),
        );

        assert.deepEqual(unit.meshState(drawnInFile, mesh, 2), ruleState(true));

        unit.setActiveAbilities(new Set(["STEALTH"]));

        // A cloak writes BASEMESH and STEALTH parts only: everything else keeps its state.
        assert.deepEqual(unit.meshState(drawnInFile, mesh, 2), ruleState(true));
        assert.deepEqual(unit.effectState(drawnInFile, 2), ruleState(true));
    });

    it("writes nothing in a model preview", () => {
        const { unit } = rulesWith("file");
        unit.setHardpoints(writes({ damageBones: new Set(["hp_bone"]) }));

        assert.deepEqual(unit.meshState(drawnInFile, mesh, 2), drawnInFile);
        assert.deepEqual(unit.effectState(drawnInFile, 3), drawnInFile);
    });
});

describe("UnitRules, a hardpoint's Engine_Particles bone", () => {
    const mesh = { alamoMesh: "Glow" };

    it("writes nothing while the hardpoint lives", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(writes());

        assert.deepEqual(unit.meshState(drawnInFile, mesh, 2), drawnInFile);
        assert.deepEqual(
            unit.effectState(fileState(false), 3),
            fileState(false),
        );
    });

    it("hides every part on the bone or at any depth below it once a hardpoint that hides them dies", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({ deaths: [death({ engineBone: "hp_bone" })] }),
        );

        for (const bone of [1, 2, 3]) {
            assert.deepEqual(
                unit.meshState(drawnInFile, mesh, bone),
                ruleState(false),
                `mesh on bone ${bone}`,
            );
            assert.deepEqual(
                unit.effectState(drawnInFile, bone),
                ruleState(false),
                `effect on bone ${bone}`,
            );
        }
        assert.deepEqual(unit.meshState(drawnInFile, mesh, 4), drawnInFile);
        assert.deepEqual(unit.effectState(drawnInFile, 0), drawnInFile);
    });

    it("hides after the Damage_Particles bone shows within one hardpoint's death", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                damageBones: new Set(["hp_bone"]),
                deaths: [
                    death({ damageBone: "hp_bone", engineBone: "emitdamage" }),
                ],
            }),
        );

        assert.deepEqual(unit.meshState(drawnInFile, mesh, 1), ruleState(true));
        assert.deepEqual(
            unit.meshState(drawnInFile, mesh, 2),
            ruleState(false),
        );
        assert.deepEqual(unit.effectState(drawnInFile, 3), ruleState(false));
    });

    it("writes nothing in a model preview", () => {
        const { unit } = rulesWith("file");
        unit.setHardpoints(
            writes({ deaths: [death({ engineBone: "hp_bone" })] }),
        );

        assert.deepEqual(unit.meshState(drawnInFile, mesh, 2), drawnInFile);
        assert.deepEqual(unit.effectState(drawnInFile, 3), drawnInFile);
    });
});

describe("UnitRules, the order hardpoints die in", () => {
    // An engine hardpoint E whose Engine_Particles bone is EmitDamage, and a hardpoint D whose
    // Damage_Particles bone is HP_Bone, above it: their bones overlap on EmitDamage and below.
    const engine = death({ engineBone: "emitdamage" });
    const damage = death({ damageBone: "hp_bone" });
    const mesh = { alamoMesh: "Plate" };

    it("shows the overlap when the engine hardpoint dies first and the damage hardpoint later", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                damageBones: new Set(["hp_bone"]),
                deaths: [engine, damage],
            }),
        );

        assert.deepEqual(unit.meshState(drawnInFile, mesh, 2), ruleState(true));
        assert.deepEqual(unit.effectState(drawnInFile, 3), ruleState(true));
    });

    it("hides the overlap when the damage hardpoint dies first and the engine hardpoint later", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                damageBones: new Set(["hp_bone"]),
                deaths: [damage, engine],
            }),
        );

        assert.deepEqual(
            unit.meshState(drawnInFile, mesh, 2),
            ruleState(false),
        );
        assert.deepEqual(unit.effectState(drawnInFile, 3), ruleState(false));
        // Above the overlap only the damage hardpoint speaks.
        assert.deepEqual(unit.meshState(drawnInFile, mesh, 1), ruleState(true));
    });

    it("shows a decal a later death names over an earlier engine hide", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                decals: new Set(["plate"]),
                deaths: [engine, death({ decal: "plate" })],
            }),
        );

        assert.deepEqual(unit.meshState(drawnInFile, mesh, 3), ruleState(true));
    });

    it("writes only what creation writes once every death is taken back", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                damageBones: new Set(["hp_bone"]),
                deaths: [engine, damage],
            }),
        );
        unit.setHardpoints(writes({ damageBones: new Set(["hp_bone"]) }));

        assert.deepEqual(unit.hardpoints.deaths, []);
        assert.deepEqual(
            unit.meshState(drawnInFile, mesh, 2),
            ruleState(false),
        );
    });
});

/**
 * Measured in the game: an effect name's start puts it in a group the game switches together, compared
 * over the prefix's length with case ignored - `pe` engines, `pte` turbo engines, `pi` ion stun, `pgw`
 * gravity well, `pas` sprint, `pptw` power to weapons, `pem` invulnerability, `prs` missile shield. A
 * unit is created with `pte`, `pptw` and `prs` hidden, and its abilities and power modes switch their
 * groups.
 */
describe("UnitRules, the effect name groups", () => {
    /** The state a unit's rules give an effect of this name the file draws. */
    function effect(unit: UnitRules, name: string) {
        return unit.effectState(drawnInFile, undefined, name);
    }

    it("hides pte, pptw and prs effects when the unit is created", () => {
        const { unit } = rulesWith("game");

        assert.deepEqual(effect(unit, "PTE_Corvetteengines"), ruleState(false));
        assert.deepEqual(effect(unit, "pptw_2mtank"), ruleState(false));
        assert.deepEqual(effect(unit, "prs_at-aa_fx"), ruleState(false));
        assert.deepEqual(effect(unit, "pe_corvetteengines"), drawnInFile);
        assert.deepEqual(effect(unit, "p_smoke_small"), drawnInFile);
    });

    it("compares a name's start over the prefix's length, case ignored", () => {
        const { unit } = rulesWith("game");

        assert.deepEqual(effect(unit, "PteEngine"), ruleState(false));
        assert.deepEqual(effect(unit, "pt_engine"), drawnInFile);
        assert.deepEqual(effect(unit, "pr"), drawnInFile);
    });

    it("switches pe off and pte on for turbo and for the S-foil lock", () => {
        for (const ability of ["TURBO", "SPOILER_LOCK"]) {
            const { unit } = rulesWith("game");
            unit.setActiveAbilities(new Set([ability]));

            assert.deepEqual(
                effect(unit, "pe_engines"),
                ruleState(false),
                ability,
            );
            assert.deepEqual(
                effect(unit, "pte_engines"),
                ruleState(true),
                ability,
            );
        }
    });

    it("catches a pem effect in the pe rule", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["TURBO"]));

        assert.deepEqual(effect(unit, "pem_invulnerability"), ruleState(false));
    });

    it("shows each ability's own group while it is on", () => {
        const shows: [string, string][] = [
            ["POWER_TO_WEAPONS", "pptw_2mtank"],
            ["LEECH_SHIELDS", "pptw_2mtank"],
            ["MISSILE_SHIELD", "prs_at-aa_fx"],
            ["INVULNERABILITY", "pem_invulnerability"],
            ["SPRINT", "pas_sprint"],
            ["INTERDICT", "pgw_grav_well"],
        ];

        for (const [ability, name] of shows) {
            const { unit } = rulesWith("game");
            unit.setActiveAbilities(new Set([ability]));

            assert.deepEqual(
                unit.effectState(fileState(false), undefined, name),
                ruleState(true),
                ability,
            );
        }
    });

    it("lets the later of two abilities decide an effect both switch", () => {
        const { unit } = rulesWith("game");

        unit.setActiveAbilities(new Set(["INVULNERABILITY"]));
        unit.setActiveAbilities(new Set(["INVULNERABILITY", "TURBO"]));
        assert.deepEqual(effect(unit, "pem_invulnerability"), ruleState(false));

        unit.setActiveAbilities(new Set(["TURBO"]));
        unit.setActiveAbilities(new Set(["TURBO", "INVULNERABILITY"]));
        assert.deepEqual(effect(unit, "pem_invulnerability"), ruleState(true));
    });

    /** The power mode's own write when it ends: plain engines back on, turbo engines off. */
    it("writes pe on and pte off when turbo is switched off", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["TURBO"]));
        unit.setActiveAbilities(new Set());

        assert.deepEqual(effect(unit, "pte_engines"), ruleState(false));
        assert.deepEqual(effect(unit, "pe_engines"), ruleState(true));
    });

    it("hides an ability's group when the ability ends, whatever the file says", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["SPRINT"]));
        unit.setActiveAbilities(new Set());

        assert.deepEqual(effect(unit, "pas_sprint"), ruleState(false));
    });

    it("writes nothing for an ability that was never switched on", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set());

        assert.deepEqual(effect(unit, "pas_sprint"), drawnInFile);
        assert.deepEqual(effect(unit, "pe_engines"), drawnInFile);
    });

    it("leaves turbo's engines to turbo when the unit cloaks", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["TURBO"]));
        unit.setActiveAbilities(new Set(["TURBO", "STEALTH"]));

        assert.deepEqual(effect(unit, "pte_engines"), ruleState(true));
    });

    it("writes nothing in a model preview", () => {
        const { unit } = rulesWith("file");
        unit.setActiveAbilities(new Set(["TURBO"]));

        assert.deepEqual(effect(unit, "pte_engines"), drawnInFile);
        assert.deepEqual(effect(unit, "pe_engines"), drawnInFile);
    });

    /** Measured: a type whose `Passive_Missile_Shield_Radius` is above 0 shows `prs` once created. */
    it("shows prs on creation for a missile-shield object", () => {
        const { unit } = rulesWith("game");
        unit.setMissileShieldObject(true);

        assert.deepEqual(effect(unit, "prs_interdict_fx"), ruleState(true));
    });

    it("keeps prs hidden for a unit that only has the missile shield ability, until it is on", () => {
        const { unit } = rulesWith("game");
        unit.setMissileShieldObject(false);
        assert.deepEqual(effect(unit, "prs_at-aa_fx"), ruleState(false));

        unit.setActiveAbilities(new Set(["MISSILE_SHIELD"]));
        assert.deepEqual(effect(unit, "prs_at-aa_fx"), ruleState(true));
    });

    it("forgets the switches with the model", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["SPRINT"]));
        unit.setActiveAbilities(new Set());
        unit.setMissileShieldObject(true);

        unit.forgetModel();

        assert.deepEqual(effect(unit, "pas_sprint"), drawnInFile);
        assert.deepEqual(effect(unit, "prs_fx"), ruleState(false));
    });
});

/**
 * The ion stun effects.
 *
 * Measured in the game: an object with the ION_STUN_EFFECT behaviour hides its `pi` effects when it
 * is created and shows them only while it is ion stunned. An object without the behaviour takes
 * no such write: its `pi` effects stay as its model file has them. Reported on a mod's star
 * destroyer, which opened with its ion sparks running.
 */
describe("UnitRules, the ion stun effects", () => {
    function effect(unit: UnitRules, name: string) {
        return unit.effectState(drawnInFile, undefined, name);
    }

    it("hides pi effects when a unit with the ion stun behaviour is created", () => {
        const { unit } = rulesWith("game");
        unit.setBehaviors(["SIMPLE_SPACE_LOCOMOTOR", "ion_stun_effect"]);

        assert.deepEqual(effect(unit, "PI_Damage_Elec_SD00"), ruleState(false));
        assert.deepEqual(effect(unit, "pe_engines"), drawnInFile);
        assert.deepEqual(effect(unit, "p_smoke"), drawnInFile);
    });

    it("leaves the pi effects of a unit without the behaviour as its file has them", () => {
        const { unit } = rulesWith("game");
        unit.setBehaviors(["SIMPLE_SPACE_LOCOMOTOR"]);

        assert.deepEqual(effect(unit, "pi_damage_elec"), drawnInFile);
    });

    it("asks for the level pass when the behaviours are set, and forgets them with the model", () => {
        const { unit, applied } = rulesWith("game");
        const before = applied();
        unit.setBehaviors(["ION_STUN_EFFECT"]);
        assert.equal(applied(), before + 1);

        unit.forgetModel();

        assert.deepEqual(effect(unit, "pi_damage_elec"), drawnInFile);
    });

    it("writes nothing in a model preview", () => {
        const { unit } = rulesWith("file");
        unit.setBehaviors(["ION_STUN_EFFECT"]);

        assert.deepEqual(effect(unit, "pi_damage_elec"), drawnInFile);
    });
});

/**
 * The engines going offline.
 *
 * Measured in the game: when an engine hardpoint dies and the unit has no other left undestroyed,
 * its engines go offline, and the space locomotor then keeps the engine effects hidden - `pe`, or
 * `pte` under turbo - whatever else writes them. A hardpoint's `Engine_Particles` plays no part:
 * on the Star Destroyer the glow is not below that bone at all. Reported on a mod's star destroyer
 * with seven engine hardpoints, whose glow stayed on with all of them destroyed.
 */
describe("UnitRules, the engines offline", () => {
    function effect(unit: UnitRules, name: string) {
        return unit.effectState(drawnInFile, undefined, name);
    }

    function shipWith(over: Partial<HardpointWrites>): UnitRules {
        const { unit } = rulesWith("game");
        unit.setBehaviors(["SIMPLE_SPACE_LOCOMOTOR"]);
        unit.setHardpoints(writes(over));
        return unit;
    }

    it("hides the pe and pte effects, and nothing else", () => {
        const unit = shipWith({ enginesOffline: true });

        assert.deepEqual(
            effect(unit, "pe_stardestroyerengines"),
            ruleState(false),
        );
        assert.deepEqual(effect(unit, "PTE_engines"), ruleState(false));
        assert.deepEqual(effect(unit, "p_smoke"), drawnInFile);
        assert.deepEqual(
            effect(unit, "pptw_fx"),
            ruleState(false),
            "as created",
        );
    });

    it("leaves the engine effects on while the engines are online", () => {
        const unit = shipWith({ enginesOffline: false });

        assert.deepEqual(effect(unit, "pe_stardestroyerengines"), drawnInFile);
    });

    // The locomotor writes on every service: turbo switched on afterwards shows nothing.
    it("keeps them hidden whatever an ability writes afterwards", () => {
        const unit = shipWith({ enginesOffline: true });
        unit.setActiveAbilities(new Set(["TURBO"]));

        assert.deepEqual(effect(unit, "pte_engines"), ruleState(false));
        assert.deepEqual(effect(unit, "pe_engines"), ruleState(false));
    });

    it("writes nothing for a unit the space locomotor does not move", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(writes({ enginesOffline: true }));

        assert.deepEqual(effect(unit, "pe_engines"), drawnInFile);
    });

    it("writes nothing in a model preview", () => {
        const { unit } = rulesWith("file");
        unit.setBehaviors(["SIMPLE_SPACE_LOCOMOTOR"]);
        unit.setHardpoints(writes({ enginesOffline: true }));

        assert.deepEqual(effect(unit, "pe_engines"), drawnInFile);
    });
});

/**
 * How bright a ship's engine effects are drawn, and the states the reader puts a unit in.
 *
 * Measured in the game - see `engineGlow.ts` for the brightness, and for the ion stun: a stun shows
 * the `pi` effects of a unit with the ION_STUN_EFFECT behaviour, and its end hides them again.
 */
describe("UnitRules, a ship's speed and the states done to it", () => {
    function ship(): UnitRules {
        const { unit } = rulesWith("game");
        unit.setBehaviors(["SIMPLE_SPACE_LOCOMOTOR", "ION_STUN_EFFECT"]);
        unit.setMaxSpeed(1.5);
        return unit;
    }

    it("draws the engine effects at full brightness at full speed, which is how a unit opens", () => {
        const unit = ship();

        assert.equal(unit.effectBrightness("pe_engines", 0), 1);
        assert.equal(unit.effectBrightness("PTE_engines", 0), 1);
    });

    it("dims the engine effects with the speed, down to a fifth at rest", () => {
        const unit = ship();

        unit.setSpeed(0);
        assert.equal(unit.effectBrightness("pe_engines", 0), 0.2);

        unit.setSpeed(0.5);
        assert.ok(
            Math.abs(unit.effectBrightness("pe_engines", 0) - 0.6) < 1e-9,
        );
    });

    it("leaves every other effect at full brightness", () => {
        const unit = ship();
        unit.setSpeed(0);

        assert.equal(unit.effectBrightness("p_smoke", 0), 1);
        assert.equal(unit.effectBrightness("pi_damage", 0), 1);
        assert.equal(unit.effectBrightness(undefined, 0), 1);
    });

    it("draws the engine effects of a type that cannot move dark", () => {
        const unit = ship();
        unit.setMaxSpeed(0);

        assert.equal(unit.effectBrightness("pe_engines", 0), 0);
    });

    it("leaves the brightness alone for a unit the space locomotor does not move, and in a model preview", () => {
        const { unit: walker } = rulesWith("game");
        walker.setSpeed(0);
        assert.equal(walker.effectBrightness("pe_engines", 0), 1);

        const { unit: file } = rulesWith("file");
        file.setBehaviors(["SIMPLE_SPACE_LOCOMOTOR"]);
        file.setSpeed(0);
        assert.equal(file.effectBrightness("pe_engines", 0), 1);
    });

    it("flickers the engine effects at up to a fifth while the engines are disabled, and keeps them drawn", () => {
        const unit = ship();
        unit.setEnginesDisabled(true);

        const values = Array.from({ length: 30 }, (_, frame) =>
            unit.effectBrightness("pe_engines", frame / 30 + 0.001),
        );
        assert.ok(values.every((value) => value >= 0 && value <= 0.2));
        assert.ok(new Set(values).size > 10);
        assert.deepEqual(
            unit.effectState(drawnInFile, undefined, "pe_engines"),
            drawnInFile,
        );

        unit.setEnginesDisabled(false);
        assert.equal(unit.effectBrightness("pe_engines", 0), 1);
    });

    it("shows the pi effects while the unit is ion stunned, whatever the file says, and hides them after", () => {
        const unit = ship();
        assert.deepEqual(
            unit.effectState(fileState(false), undefined, "pi_damage_elec"),
            ruleState(false),
        );

        unit.setIonStunned(true);
        assert.deepEqual(
            unit.effectState(fileState(false), undefined, "pi_damage_elec"),
            ruleState(true),
        );
        assert.deepEqual(
            unit.effectState(drawnInFile, undefined, "p_smoke"),
            drawnInFile,
        );

        unit.setIonStunned(false);
        assert.deepEqual(
            unit.effectState(drawnInFile, undefined, "pi_damage_elec"),
            ruleState(false),
        );
    });

    it("stuns nothing that has no ion stun behaviour", () => {
        const { unit } = rulesWith("game");
        unit.setBehaviors(["SIMPLE_SPACE_LOCOMOTOR"]);
        unit.setIonStunned(true);

        assert.deepEqual(
            unit.effectState(fileState(false), undefined, "pi_damage_elec"),
            fileState(false),
        );
    });

    it("asks for the level pass when a stun starts or ends, and only then", () => {
        const { unit, applied } = rulesWith("game");
        unit.setBehaviors(["ION_STUN_EFFECT"]);
        const before = applied();

        unit.setIonStunned(true);
        unit.setIonStunned(true);
        unit.setSpeed(0.3);
        unit.setEnginesDisabled(true);

        assert.equal(applied(), before + 1);
    });

    it("forgets all of it with the model", () => {
        const unit = ship();
        unit.setSpeed(0);
        unit.setEnginesDisabled(true);
        unit.setIonStunned(true);

        unit.forgetModel();
        unit.setBehaviors(["SIMPLE_SPACE_LOCOMOTOR", "ION_STUN_EFFECT"]);

        assert.equal(unit.effectBrightness("pe_engines", 0), 1);
        assert.deepEqual(
            unit.effectState(drawnInFile, undefined, "pi_damage_elec"),
            ruleState(false),
        );
    });
});

/**
 * Another object the preview shows - a death clone - is created too, so its effects get what creating
 * it writes, and none of the subject's hardpoint or ability writes.
 */
describe("UnitRules, a created object's effects", () => {
    it("hides its pte, pptw and prs effects and leaves the rest as its file has them", () => {
        const { unit } = rulesWith("game");

        assert.deepEqual(
            unit.createdEffectState(drawnInFile, "pte_x"),
            ruleState(false),
        );
        assert.deepEqual(
            unit.createdEffectState(drawnInFile, "p_explosion_big00"),
            drawnInFile,
        );
    });

    it("takes none of the subject's ability writes", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["TURBO"]));

        assert.deepEqual(
            unit.createdEffectState(drawnInFile, "pe_engines"),
            drawnInFile,
        );
    });

    it("writes nothing in a model preview", () => {
        const { unit } = rulesWith("file");

        assert.deepEqual(
            unit.createdEffectState(drawnInFile, "pte_x"),
            drawnInFile,
        );
    });
});

/**
 * The maintainer's rule: deaths and ability switches are ONE timeline, replayed last write wins after
 * what creation writes - not the abilities after every death.
 */
describe("UnitRules, deaths and abilities in the order they happened", () => {
    /** HP_Bone's Engine_Particles death hides the effect on EmitDamage, below it. */
    const dies = writes({
        deaths: [death({ hardpoint: "HP_E", engineBone: "hp_bone" })],
    });

    it("shows turbo's engines when turbo is switched on after the engine hardpoint died", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(dies);
        unit.setActiveAbilities(new Set(["TURBO"]));

        assert.deepEqual(
            unit.effectState(drawnInFile, 2, "pte_engines"),
            ruleState(true),
        );
    });

    it("hides them when the engine hardpoint dies after turbo is switched on", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["TURBO"]));
        unit.setHardpoints(dies);

        assert.deepEqual(
            unit.effectState(drawnInFile, 2, "pte_engines"),
            ruleState(false),
        );
    });

    it("takes a repaired hardpoint's death out of the timeline", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["TURBO"]));
        unit.setHardpoints(dies);
        unit.setHardpoints(writes());

        assert.deepEqual(
            unit.effectState(drawnInFile, 2, "pte_engines"),
            ruleState(true),
        );
    });
});

/**
 * Measured in the game: stealth hides every part whose name contains BASEMESH and every model a
 * hardpoint attaches, and shows every part whose name contains STEALTH; Force cloak does the same
 * without the attached models. Either ending shows the BASEMESH parts and the attached models again
 * and hides the STEALTH parts. Every other part keeps its state.
 */
describe("UnitRules, the cloaks", () => {
    const base = { alamoMesh: "Tie_Phantom_BaseMesh" };
    const shell = { alamoMesh: "Phantom_Stealth_Shell" };
    const hull = { alamoMesh: "Hull" };

    it("hides the BASEMESH parts and the attached models, and shows the STEALTH parts, for stealth", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["STEALTH"]));

        assert.deepEqual(unit.meshState(drawnInFile, base), ruleState(false));
        assert.deepEqual(
            unit.meshState(fileState(false), shell),
            ruleState(true),
        );
        assert.deepEqual(
            unit.meshState(drawnInFile, hull, undefined, true),
            ruleState(false),
            "the attached model",
        );
        assert.deepEqual(unit.meshState(drawnInFile, hull), drawnInFile);
        assert.deepEqual(
            unit.effectState(drawnInFile, undefined, "p_smoke", true),
            ruleState(false),
            "the attached model's effect",
        );
        assert.deepEqual(
            unit.effectState(drawnInFile, undefined, "pe_engines"),
            drawnInFile,
        );
    });

    it("leaves the attached models alone for Force cloak", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["FORCE_CLOAK"]));

        assert.deepEqual(unit.meshState(drawnInFile, base), ruleState(false));
        assert.deepEqual(
            unit.meshState(fileState(false), shell),
            ruleState(true),
        );
        assert.deepEqual(
            unit.meshState(drawnInFile, hull, undefined, true),
            drawnInFile,
        );
    });

    it("shows the BASEMESH parts and the attached models and hides the STEALTH parts when either ends", () => {
        for (const cloak of ["STEALTH", "FORCE_CLOAK"]) {
            const { unit } = rulesWith("game");
            unit.setActiveAbilities(new Set([cloak]));
            unit.setActiveAbilities(new Set());

            assert.deepEqual(
                unit.meshState(fileState(false), base),
                ruleState(true),
                cloak,
            );
            assert.deepEqual(
                unit.meshState(drawnInFile, shell),
                ruleState(false),
                cloak,
            );
            assert.deepEqual(
                unit.meshState(drawnInFile, hull, undefined, true),
                drawnInFile,
                `${cloak}: the attached model`,
            );
            // The model is shown again; what its own file hides stays hidden.
            assert.deepEqual(
                unit.meshState(fileState(false), hull, undefined, true),
                fileState(false),
                `${cloak}: a part the attached model's file hides`,
            );
        }
    });

    it("puts a cloak on the one timeline with the hardpoint deaths", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["STEALTH"]));
        unit.setHardpoints(
            writes({
                damageBones: new Set(["hp_bone"]),
                deaths: [death({ hardpoint: "HP_A", damageBone: "hp_bone" })],
            }),
        );

        // The death after the cloak shows the part on its bone, BASEMESH or not.
        assert.deepEqual(unit.meshState(drawnInFile, base, 2), ruleState(true));
    });

    it("says when the unit casts no shadow, in a unit preview only", () => {
        const game = rulesWith("game").unit;
        game.setCloakShadowsOff(true);
        assert.equal(game.shadowsOff(), true);

        const file = rulesWith("file").unit;
        file.setCloakShadowsOff(true);
        assert.equal(file.shadowsOff(), false);
    });
});

describe("UnitRules, a hardpoint's Collision_Mesh (#450)", () => {
    const hardpoint: PreviewHardpoint = {
        id: "HP_Weapon",
        partId: "hp:HP_Weapon",
        type: null,
        attachBone: "HP_Bone",
        isDestroyable: true,
        isTargetable: true,
        health: 325,
        damageParticlesBone: null,
        damageDecalBone: null,
        engineParticlesBone: null,
        deathExplosionParticles: null,
        deathBreakoffProp: null,
        engineDeathHidesEngineParticles: false,
        tooltipText: null,
        turret: null,
    };

    // Measured in the game: no XML hides a mesh it names as a Collision_Mesh - the model file's own
    // flag alone decides. So the rule exists nowhere: no state a hardpoint's collision hull writes,
    // and the scene does not even carry the hull's name.
    it("draws a Collision_Mesh the file marks visible", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(hardpointWrites([hardpoint], new Set()));

        assert.deepEqual(
            unit.meshState(drawnInFile, { alamoMesh: "Collision" }),
            drawnInFile,
        );
        assert.equal("setCollisionMeshes" in unit, false);
        assert.equal("collisionMeshes" in unit, false);
        assert.equal("collisionMeshNames" in damage, false);
    });

    it("leaves a Collision_Mesh the file hides hidden", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(hardpointWrites([hardpoint], new Set()));

        assert.deepEqual(
            unit.meshState(fileState(false), { alamoMesh: "Collision" }),
            fileState(false),
        );
    });
});

/**
 * A model a hardpoint attaches, as the unit's rules see it.
 *
 * Measured in the game: the attached model is ONE part of the unit, a model inside the model. A rule
 * that names a part is compared with the unit model's own parts and never looks inside it; a rule
 * that reaches it - a bone it hangs on or below, a cloak - writes the model's one switch; and its
 * own parts keep what its file says, drawn only while the model is.
 *
 * Reported on a mod's star destroyer: its two bow weapons are attached models whose one drawn mesh
 * is named like the hardpoint's `Damage_Decal` on the hull, and the decal rule hid the weapons.
 */
describe("UnitRules, a model a hardpoint attaches", () => {
    const weapon = { alamoMesh: "Weap_Blast" };
    const collision = { alamoMesh: "Collision" };
    /** The hull bones a model can hang on: the hardpoint's own, and its Damage_Particles bone. */
    const HP_BONE = 1;
    const EMIT_DAMAGE = 2;

    it("leaves a part inside it that is named like a decal as its file has it", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(writes({ decals: new Set(["weap_blast"]) }));

        assert.deepEqual(
            unit.meshState(drawnInFile, weapon, HP_BONE, true),
            drawnInFile,
        );
        // The unit's own part of that name is the decal.
        assert.deepEqual(
            unit.meshState(drawnInFile, weapon, HP_BONE),
            ruleState(false),
        );
    });

    it("does not show a part inside it for a death that shows the decal of that name", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                decals: new Set(["weap_blast"]),
                deaths: [death({ decal: "weap_blast" })],
            }),
        );

        assert.deepEqual(
            unit.meshState(fileState(false), weapon, HP_BONE, true),
            fileState(false),
        );
    });

    it("is hidden whole while it hangs on or below a living hardpoint's Damage_Particles bone", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(writes({ damageBones: new Set(["emitdamage"]) }));

        for (const bone of [EMIT_DAMAGE, 3]) {
            assert.deepEqual(
                unit.meshState(drawnInFile, weapon, bone, true),
                ruleState(false),
                `on bone ${bone}`,
            );
        }
        assert.deepEqual(
            unit.meshState(drawnInFile, weapon, HP_BONE, true),
            drawnInFile,
            "above the bone",
        );
    });

    // The death shows the MODEL. What its file hides - a collision hull - stays hidden.
    it("draws its parts as its file has them once that hardpoint is destroyed", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                damageBones: new Set(["emitdamage"]),
                deaths: [death({ damageBone: "emitdamage" })],
            }),
        );

        assert.deepEqual(
            unit.meshState(drawnInFile, weapon, EMIT_DAMAGE, true),
            drawnInFile,
        );
        assert.deepEqual(
            unit.meshState(fileState(false), collision, EMIT_DAMAGE, true),
            fileState(false),
        );
    });

    it("is hidden whole once a hardpoint dies whose Engine_Particles bone it hangs on or below", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({ deaths: [death({ engineBone: "hp_bone" })] }),
        );

        assert.deepEqual(
            unit.meshState(drawnInFile, weapon, EMIT_DAMAGE, true),
            ruleState(false),
        );
    });

    it("is hidden whole for stealth, a part inside it that a cloak would show with it", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["STEALTH"]));

        assert.deepEqual(
            unit.meshState(
                fileState(false),
                { alamoMesh: "Turret_Stealth" },
                HP_BONE,
                true,
            ),
            ruleState(false),
        );
    });

    it("draws its parts as its file has them when the cloak ends", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["STEALTH"]));
        unit.setActiveAbilities(new Set());

        assert.deepEqual(
            unit.meshState(drawnInFile, weapon, HP_BONE, true),
            drawnInFile,
        );
        assert.deepEqual(
            unit.meshState(fileState(false), collision, HP_BONE, true),
            fileState(false),
        );
    });

    it("takes the later of a death and a cloak, as one switch", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(writes({ damageBones: new Set(["emitdamage"]) }));
        unit.setActiveAbilities(new Set(["STEALTH"]));
        unit.setHardpoints(
            writes({
                damageBones: new Set(["emitdamage"]),
                deaths: [death({ damageBone: "emitdamage" })],
            }),
        );

        // Cloaked, then shown by the death: the death is the last write of the model's switch.
        assert.deepEqual(
            unit.meshState(drawnInFile, weapon, EMIT_DAMAGE, true),
            drawnInFile,
        );
    });

    it("writes nothing in a model preview", () => {
        const { unit } = rulesWith("file");
        unit.setHardpoints(writes({ damageBones: new Set(["emitdamage"]) }));

        assert.deepEqual(
            unit.meshState(drawnInFile, weapon, EMIT_DAMAGE, true),
            drawnInFile,
        );
    });
});
