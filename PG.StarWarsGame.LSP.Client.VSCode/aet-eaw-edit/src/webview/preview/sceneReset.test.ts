// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { resetScene, type SceneResetSteps } from "./sceneReset";

/** Steps that only record that they ran, in the order they ran. */
function recorder(): { ran: string[]; steps: SceneResetSteps } {
    const ran: string[] = [];
    const step = (name: keyof SceneResetSteps) => (): void => {
        ran.push(name);
    };
    return {
        ran,
        steps: {
            showRestPose: step("showRestPose"),
            resetModel: step("resetModel"),
            forgetDetailPick: step("forgetDetailPick"),
            resetTurrets: step("resetTurrets"),
            repair: step("repair"),
            resetUnitState: step("resetUnitState"),
            clearAbilities: step("clearAbilities"),
            clearSelection: step("clearSelection"),
            restartParticles: step("restartParticles"),
            clearDamageLog: step("clearDamageLog"),
        },
    };
}

describe("resetScene", () => {
    it("puts back every part of the subject, each once", () => {
        const { ran, steps } = recorder();

        resetScene(steps);

        assert.deepEqual([...ran].sort(), Object.keys(steps).sort());
    });

    /** Stopping a clip puts back the parts the clip moved, before anything else is put back. */
    it("shows the rest pose first", () => {
        const { ran, steps } = recorder();

        resetScene(steps);

        assert.equal(ran[0], "showRestPose");
    });

    /**
     * Repairing writes a line into the damage log. A reset that left that line would be Repair all
     * under another name; the log is cleared after everything that writes to it.
     */
    it("clears the damage log last, after the repair that writes to it", () => {
        const { ran, steps } = recorder();

        resetScene(steps);

        assert.equal(ran.at(-1), "clearDamageLog");
        assert.ok(ran.indexOf("repair") < ran.indexOf("clearDamageLog"));
    });
});
