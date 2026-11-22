// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import type {
    PreviewHardpoint,
    PreviewParticle,
} from "../../protocol/modelPreview";
import {
    type Destroyed,
    destroyable,
    effectPlaysNow,
    hardpointWrites,
    partHidden,
    withDestroyed,
} from "./damage";

function hardpoint(over: Partial<PreviewHardpoint> = {}): PreviewHardpoint {
    return {
        id: "HP_Weapon",
        partId: "hp:HP_Weapon",
        type: null,
        attachBone: "HP_F-L_Bone",
        isDestroyable: true,
        isTargetable: true,
        health: 325,
        damageParticlesBone: "HP_F-L_EmitDamage",
        damageDecalBone: "HP_F-L_Blast",
        engineParticlesBone: null,
        deathExplosionParticles: "Large_Explosion_Space_Empire",
        deathBreakoffProp: null,
        engineDeathHidesEngineParticles: false,
        tooltipText: null,
        turret: null,
        ...over,
    };
}

function particle(over: Partial<PreviewParticle> = {}): PreviewParticle {
    return {
        id: "hull#0",
        systemRef: "p_hp_imperial_damage",
        partId: "hull",
        bone: "p_hp_imperial_damage",
        boneIndex: 2,
        gate: "Always",
        hardpointId: null,
        startsVisible: true,
        ...over,
    };
}

describe("partHidden", () => {
    it("breaks the turret off when its hardpoint is destroyed", () => {
        assert.equal(
            partHidden("hp:HP_Weapon", [hardpoint()], new Set(["HP_Weapon"])),
            true,
        );
    });

    it("leaves an intact hardpoint attached", () => {
        assert.equal(
            partHidden("hp:HP_Weapon", [hardpoint()], new Set()),
            false,
        );
    });

    it("never hides the hull, whatever is destroyed", () => {
        assert.equal(
            partHidden("hull", [hardpoint()], new Set(["HP_Weapon"])),
            false,
        );
    });
});

describe("hardpointWrites", () => {
    const engine = hardpoint({
        id: "HP_Engine",
        type: "HARD_POINT_ENGINE",
        damageDecalBone: null,
        damageParticlesBone: null,
        engineParticlesBone: "HP_E_Bone",
        engineDeathHidesEngineParticles: true,
    });

    // Measured in the game: the engines go offline when an engine hardpoint dies and none is left
    // undestroyed - with one left, nothing happens.
    describe("the engines", () => {
        const second = hardpoint({
            id: "HP_Engine_2",
            type: " hard_point_engine ",
        });

        it("are online while any engine hardpoint is left", () => {
            const some = hardpointWrites(
                [hardpoint(), engine, second],
                new Set(["HP_Engine", "HP_Weapon"]),
            );

            assert.equal(some.enginesOffline, false);
            assert.equal(
                hardpointWrites([engine], new Set()).enginesOffline,
                false,
            );
        });

        it("are offline once every engine hardpoint is destroyed", () => {
            const all = hardpointWrites(
                [hardpoint(), engine, second],
                new Set(["HP_Engine_2", "HP_Engine"]),
            );

            assert.equal(all.enginesOffline, true);
        });

        it("are never offline on a unit with no engine hardpoint", () => {
            const none = hardpointWrites([hardpoint()], new Set(["HP_Weapon"]));

            assert.equal(none.enginesOffline, false);
        });
    });

    it("names what every hardpoint writes on creation, lowercased, whatever is destroyed", () => {
        const writes = hardpointWrites([hardpoint(), engine], new Set());

        // Damage_Decal names a mesh that shares its bone's name, and the file ships it VISIBLE - the
        // game hides it until the hardpoint is blown off, so the preview has to do the same.
        assert.deepEqual([...writes.decals], ["hp_f-l_blast"]);
        assert.deepEqual([...writes.damageBones], ["hp_f-l_emitdamage"]);
        assert.deepEqual(writes.deaths, []);
    });

    it("lists the deaths in the order the hardpoints died, each with what its death writes", () => {
        const writes = hardpointWrites(
            [hardpoint(), engine],
            new Set(["HP_Engine", "HP_Weapon"]),
        );

        assert.deepEqual(writes.deaths, [
            {
                hardpoint: "HP_Engine",
                decal: null,
                damageBone: null,
                engineBone: "hp_e_bone",
            },
            {
                hardpoint: "HP_Weapon",
                decal: "hp_f-l_blast",
                damageBone: "hp_f-l_emitdamage",
                engineBone: null,
            },
        ]);
    });

    it("writes no engine hide for a hardpoint whose death leaves the engines alone", () => {
        const keeps = hardpoint({
            id: "HP_Keeps",
            engineParticlesBone: "HP_E_Bone",
            engineDeathHidesEngineParticles: false,
        });

        assert.equal(
            hardpointWrites([keeps], new Set(["HP_Keeps"])).deaths[0]
                .engineBone,
            null,
        );
    });
});

describe("withDestroyed", () => {
    it("records deaths in the order they happen, and a repair takes one out", () => {
        let destroyed: Destroyed = new Set();
        destroyed = withDestroyed(destroyed, "HP_B", true);
        destroyed = withDestroyed(destroyed, "HP_A", true);
        assert.deepEqual([...destroyed], ["HP_B", "HP_A"]);

        destroyed = withDestroyed(destroyed, "HP_B", false);
        assert.deepEqual([...destroyed], ["HP_A"]);

        // Destroyed again, it died last.
        destroyed = withDestroyed(destroyed, "HP_B", true);
        assert.deepEqual([...destroyed], ["HP_A", "HP_B"]);
    });

    it("leaves the order empty once every hardpoint is repaired", () => {
        let destroyed: Destroyed = new Set(["HP_A", "HP_B"]);
        for (const id of ["HP_A", "HP_B"]) {
            destroyed = withDestroyed(destroyed, id, false);
        }

        assert.deepEqual([...destroyed], []);
    });

    it("changes nothing for a hardpoint already in that state", () => {
        const destroyed: Destroyed = new Set(["HP_A"]);

        assert.equal(withDestroyed(destroyed, "HP_A", true), destroyed);
        assert.equal(withDestroyed(destroyed, "HP_B", false), destroyed);
    });
});

describe("destroyable", () => {
    it("lists only the hardpoints that can actually be destroyed", () => {
        const shield = hardpoint({ id: "HP_Shield", isDestroyable: false });

        assert.deepEqual(
            destroyable([hardpoint(), shield]).map((h) => h.id),
            ["HP_Weapon"],
        );
    });

    it("keeps the order the hardpoints are declared in, which is the order Destroy all kills them", () => {
        assert.deepEqual(
            destroyable([
                hardpoint({ id: "HP_C" }),
                hardpoint({ id: "HP_A" }),
                hardpoint({ id: "HP_B" }),
            ]).map((h) => h.id),
            ["HP_C", "HP_A", "HP_B"],
        );
    });
});

/**
 * Measured in the game: a unit is created with every attached effect running that no switch hides.
 * Which effects a switch hides on creation - by name group, by hardpoint bone - is the unit's rules'
 * to write; this is the file's answer they write over.
 */
describe("effectPlaysNow", () => {
    it("runs an ordinary effect the file draws, however it is named", () => {
        assert.equal(
            effectPlaysNow(particle({ systemRef: "p_smoke_small" })),
            true,
        );
        assert.equal(
            effectPlaysNow(particle({ systemRef: "p_dust_plume" })),
            true,
        );
    });

    it("runs an engine the file draws", () => {
        assert.equal(
            effectPlaysNow(particle({ systemRef: "pe_engines" })),
            true,
        );
    });

    /**
     * The Damage_Particles and Engine_Particles rules reach every part below a bone, meshes too, so
     * they live with the unit's other state writers. The gate only names the owner here.
     */
    it("leaves an effect a hardpoint owns to the hardpoint's bone rules", () => {
        const smoke = particle({
            gate: "HardpointDestroyed",
            hardpointId: "HP_Weapon",
        });
        const glow = particle({
            gate: "HardpointAlive",
            hardpointId: "HP_Engine",
        });

        assert.equal(effectPlaysNow(smoke), true);
        assert.equal(effectPlaysNow(glow), true);
    });

    /**
     * `pi_damage_elec_SD00` is present but invisible in the file. Only a Damage_Particles bone above
     * it shows it on destruction, and that is the unit's rule, not this one.
     */
    it("keeps an effect the model ships switched off switched off", () => {
        assert.equal(effectPlaysNow(particle({ startsVisible: false })), false);
    });
});
