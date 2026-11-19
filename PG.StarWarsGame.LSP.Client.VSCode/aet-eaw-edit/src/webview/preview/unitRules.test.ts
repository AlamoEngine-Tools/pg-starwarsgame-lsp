// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { PREVIEW_SCENE_KIND } from "../../protocol/modelPreview";
import { type RowFacts } from "./visibility";
import { type PreviewRules, UnitRules, rulesFor } from "./unitRules";
import { standInContext } from "./viewportContext.testing";

/** The rules over a stand-in viewport, counting how often the level pass was asked to run. */
function rulesWith(rules?: PreviewRules): {
    unit: UnitRules;
    applied: () => number;
} {
    let applied = 0;
    const unit = new UnitRules(
        standInContext({
            levels: {
                applyLevels: () => {
                    applied++;
                },
            },
        }),
    );

    if (rules !== undefined) {
        unit.setRules(rules);
    }

    return { unit, applied: () => applied };
}

/** A row the file draws, at a level that includes it. */
function drawnInFile(): RowFacts {
    return { inFile: true, gated: false, masters: [] };
}

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
        unit.setDecals(new Set(["hp_blast"]), new Set());
        unit.setStealthed(true);
        const facts = drawnInFile();

        assert.deepEqual(
            unit.meshFacts(facts, { alamoMesh: "Collision" }, false),
            facts,
        );
        assert.deepEqual(
            unit.meshFacts(facts, { alamoMesh: "HP_Blast" }, false),
            facts,
        );
        assert.deepEqual(
            unit.meshFacts(facts, { alamoMesh: "stealth" }, false),
            facts,
        );
        assert.deepEqual(unit.effectFacts(facts), facts);
        assert.deepEqual(unit.idleFacts(facts, false), facts);
        assert.equal(unit.isStealthed(), false);
    });

    it("hides the stealth shell of a unit until it is cloaked, and then everything else", () => {
        const { unit } = rulesWith("game");
        const shell = { alamoMesh: "stealth" };
        const hull = { alamoMesh: "Hull" };

        assert.equal(unit.meshFacts(drawnInFile(), shell, false).inFile, false);
        assert.equal(unit.meshFacts(drawnInFile(), hull, false).gated, false);

        unit.setStealthed(true);

        assert.equal(unit.isStealthed(), true);
        assert.equal(unit.meshFacts(drawnInFile(), shell, false).inFile, true);
        assert.equal(unit.meshFacts(drawnInFile(), hull, false).gated, true);
        assert.equal(unit.effectFacts(drawnInFile()).gated, true);
    });

    it("shows a unit's shield mesh only while an ability holds it up", () => {
        const { unit } = rulesWith("game");
        const shield = { alamoMesh: "shield", alamoHidden: true };
        const inFile: RowFacts = { inFile: false, gated: true, masters: [] };

        assert.equal(unit.meshFacts(inFile, shield, false).inFile, false);

        unit.setShieldRevealed(true);
        const revealed = unit.meshFacts(inFile, shield, false);

        assert.equal(revealed.inFile, true);
        assert.equal(revealed.gated, false);
    });

    it("hides a unit's collision mesh and its damage decals until the hardpoint is destroyed", () => {
        const { unit } = rulesWith("game");
        unit.setCollisionMeshes(new Set(["collision"]));
        unit.setDecals(new Set(["hp_blast"]), new Set());

        assert.equal(
            unit.meshFacts(drawnInFile(), { alamoMesh: "Collision" }, false)
                .gated,
            true,
        );
        assert.equal(
            unit.meshFacts(drawnInFile(), { alamoMesh: "HP_Blast" }, false)
                .inFile,
            false,
        );

        unit.setDecals(new Set(["hp_blast"]), new Set(["hp_blast"]));

        assert.equal(
            unit.meshFacts(drawnInFile(), { alamoMesh: "HP_Blast" }, false)
                .inFile,
            true,
        );
    });

    it("stands a unit in its idle clip", () => {
        const { unit } = rulesWith("game");

        assert.equal(unit.idleFacts(drawnInFile(), false).resting, false);
        assert.equal(
            unit.idleFacts(drawnInFile(), undefined).resting,
            undefined,
        );
    });

    it("forgets a model's decals, collision meshes and shield", () => {
        const { unit } = rulesWith("game");
        unit.setDecals(new Set(["hp_decal"]), new Set(["hp_decal"]));
        unit.setCollisionMeshes(new Set(["collision"]));
        unit.setShieldRevealed(true);

        unit.forgetModel();

        assert.equal(unit.knownDecals.size, 0);
        assert.equal(unit.shownDecals.size, 0);
        assert.equal(unit.collisionMeshes.size, 0);
        assert.equal(unit.shieldRevealed, false);
    });
});
