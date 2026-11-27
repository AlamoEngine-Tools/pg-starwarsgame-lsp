// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Putting the subject back the way it loaded (#222).
//
// The preview had a reset per corner: the tree's own for hidden parts, the stage's for turrets, the
// attacker's for a repaired target, the player's for the rest pose. Getting back to "as it opened"
// took all four and knowing where each was. This is the one step that does them together, without
// loading anything again - a large mod's unit takes long enough to load once.
//
// What it puts back is the SUBJECT. What the reader set up to look at it - the camera, the
// overlays, the team colour, the lens, the attacker, the tree's filter and its folded rows - is
// theirs and stays.
//
// Free of React and the viewport, so the order is testable: the caller hands in what each step
// does.

/** The steps of a scene reset, each put back by the part of the preview that owns it. */
export interface SceneResetSteps {
    /** The clip stopped, the bones at rest. */
    showRestPose(): void;
    /** Every part and effect shown or hidden by hand, the damage state and the detail level. */
    resetModel(): void;
    /** The detail level follows the opening rule again, as if none had been picked. */
    forgetDetailPick(): void;
    resetTurrets(): void;
    /** Hull and shield pools full, every hardpoint back, the breakoffs gone. */
    repair(): void;
    /** Full speed, no ion stun, engines on. */
    resetUnitState(): void;
    clearAbilities(): void;
    clearSelection(): void;
    restartParticles(): void;
    /** Last, so it also takes whatever the steps before it wrote into the log. */
    clearDamageLog(): void;
}

/**
 * Runs every step, in an order that matters twice: the rest pose comes first, because stopping a
 * clip puts the parts it moved back before anything else is judged against them; and the damage
 * log goes last, because repairing writes to it.
 */
export function resetScene(steps: SceneResetSteps): void {
    steps.showRestPose();
    steps.resetModel();
    steps.forgetDetailPick();
    steps.resetTurrets();
    steps.repair();
    steps.resetUnitState();
    steps.clearAbilities();
    steps.clearSelection();
    steps.restartParticles();
    steps.clearDamageLog();
}
