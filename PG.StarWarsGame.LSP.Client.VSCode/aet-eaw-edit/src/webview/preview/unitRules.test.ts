// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { PREVIEW_SCENE_KIND } from "../../protocol/modelPreview";
import { type HardpointDeath, type HardpointWrites } from "./damage";
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
        unit.setCollisionMeshes(new Set(["collision"]));
        unit.setHardpoints(writes({ decals: new Set(["hp_blast"]) }));
        unit.setStealthed(true);

        for (const alamoMesh of ["Collision", "HP_Blast", "stealth", "Hull"]) {
            assert.deepEqual(
                unit.meshState(drawnInFile, { alamoMesh }),
                drawnInFile,
            );
        }
        assert.deepEqual(unit.effectState(drawnInFile), drawnInFile);
        assert.equal(unit.idle(false), undefined);
        assert.equal(unit.isStealthed(), false);
    });

    it("hides the stealth shell of a unit until it is cloaked, and then everything else", () => {
        const { unit } = rulesWith("game");
        const shell = { alamoMesh: "stealth" };
        const hull = { alamoMesh: "Hull" };

        assert.deepEqual(unit.meshState(drawnInFile, shell), ruleState(false));
        assert.deepEqual(unit.meshState(drawnInFile, hull), drawnInFile);

        unit.setStealthed(true);

        assert.equal(unit.isStealthed(), true);
        assert.deepEqual(unit.meshState(drawnInFile, shell), ruleState(true));
        assert.deepEqual(unit.meshState(drawnInFile, hull), ruleState(false));
        assert.deepEqual(unit.effectState(drawnInFile), ruleState(false));
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

    it("hides a unit's collision mesh and its damage decals until the hardpoint is destroyed", () => {
        const { unit } = rulesWith("game");
        unit.setCollisionMeshes(new Set(["collision"]));
        unit.setHardpoints(writes({ decals: new Set(["hp_blast"]) }));

        assert.deepEqual(
            unit.meshState(drawnInFile, { alamoMesh: "Collision" }),
            ruleState(false),
        );
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

    it("forgets a model's hardpoints, collision meshes and shield", () => {
        const { unit } = rulesWith("game");
        unit.setHardpoints(
            writes({
                decals: new Set(["hp_decal"]),
                damageBones: new Set(["hp_bone"]),
                deaths: [death({ decal: "hp_decal", engineBone: "hp_bone" })],
            }),
        );
        unit.setCollisionMeshes(new Set(["collision"]));
        unit.setShieldRevealed(true);

        unit.forgetModel();

        assert.equal(unit.hardpoints.decals.size, 0);
        assert.equal(unit.hardpoints.damageBones.size, 0);
        assert.deepEqual(unit.hardpoints.deaths, []);
        assert.equal(unit.collisionMeshes.size, 0);
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

        unit.setStealthed(true);

        assert.deepEqual(
            unit.meshState(drawnInFile, mesh, 2),
            ruleState(false),
        );
        assert.deepEqual(unit.effectState(drawnInFile, 2), ruleState(false));
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

    it("still lets the cloak hide everything", () => {
        const { unit } = rulesWith("game");
        unit.setActiveAbilities(new Set(["TURBO"]));
        unit.setStealthed(true);

        assert.deepEqual(effect(unit, "pte_engines"), ruleState(false));
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
