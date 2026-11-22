// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How fast a unit is going and what has been done to it, on the stage bottom edge.

import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import {
    PREVIEW_SCENE_KIND,
    type PreviewScene,
} from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { Icon } from "../../shared/Icon";

export interface UnitStateControlsProps {
    combat: Pick<
        CombatLens,
        | "movedByLocomotor"
        | "stunnable"
        | "speed"
        | "setSpeed"
        | "ionStunned"
        | "setIonStunned"
        | "enginesDisabled"
        | "setEnginesDisabled"
    >;
    scene: PreviewScene | null;
}

/**
 * The speed a unit is going at, and the states a hit puts it in, each put on by hand: nothing moves or
 * shoots a unit here.
 *
 * Measured in the game - a ship's engine effects are as bright as it is fast, they flicker while its
 * engines are disabled for a time, and its ion effects show only while it is ion stunned.
 *
 * IN THE END SLOT, with the controls that fire at the unit and repair it: these are things done to the
 * subject, where the start slot holds the levels its model is shown at - and that slot has no room left
 * beside a damage state, a detail level and the turret reset. In every lens, because the effects they
 * drive are drawn in every lens.
 *
 * One control per effect, each DISABLED where the unit's behaviours give it nothing to act on, and
 * saying which behaviour is missing. Left out for a model opened as a file, which has no behaviours for
 * the game to act on.
 */
export function UnitStateControls({
    combat,
    scene,
}: UnitStateControlsProps): React.JSX.Element | null {
    if (scene?.kind !== PREVIEW_SCENE_KIND.object) {
        return null;
    }

    const noLocomotor = l10n.t("No SIMPLE_SPACE_LOCOMOTOR behaviour");
    const percent = Math.round(combat.speed * 100);

    return (
        <div
            className="stage-chrome unit-state"
            role="group"
            aria-label={l10n.t("Unit state")}
        >
            <label
                className="stage-field speed"
                title={
                    combat.movedByLocomotor
                        ? l10n.t("Set speed - the engine effects dim with it")
                        : noLocomotor
                }
            >
                <Icon name="speed" />
                <input
                    type="range"
                    min={0}
                    max={100}
                    step={5}
                    value={percent}
                    disabled={!combat.movedByLocomotor}
                    onChange={(e) =>
                        combat.setSpeed(Number(e.target.value) / 100)
                    }
                />
                <span className="stage-value">{l10n.t("{0} %", percent)}</span>
            </label>
            <IconButton
                icon="ionStun"
                className={combat.ionStunned ? "active" : undefined}
                pressed={combat.ionStunned}
                title={l10n.t("Ion stun")}
                disabled={!combat.stunnable}
                disabledReason={l10n.t("No ION_STUN_EFFECT behaviour")}
                onClick={() => combat.setIonStunned((on) => !on)}
            />
            <IconButton
                icon="enginesDisabled"
                className={combat.enginesDisabled ? "active" : undefined}
                pressed={combat.enginesDisabled}
                title={l10n.t("Engines disabled")}
                disabled={!combat.movedByLocomotor}
                disabledReason={noLocomotor}
                onClick={() => combat.setEnginesDisabled((on) => !on)}
            />
        </div>
    );
}
