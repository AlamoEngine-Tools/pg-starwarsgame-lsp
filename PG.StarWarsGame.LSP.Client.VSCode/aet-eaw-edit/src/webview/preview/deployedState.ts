// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The deployed state, and what it does to a unit's arcs (P5).
//
// Measured in the 2018 build: only a unit with `Deploys` and the walk locomotor is ever deployed, and
// `WalkLocomotorBehaviorClass::Is_Deployed` answers yes in `LST_WALK_DEPLOYED` alone - after the deploy
// has played, not while it plays. While it does, the shot, the turret's swing and `Can_Point_At` all read
// `Deployed_Turret_*_Extent_Degrees`, whose defaults are 360 / 180. So the AT-AT's 55 / 60 arc becomes no
// limit at all, and 19 foc and 8 eaw deploying walkers are in that shape.
//
// The preview has no deployed state of its own. It has the clip on the playhead, which is the model
// being shown in that state - so the state is read off the clip, and the weapons are swapped once, here,
// ahead of everything that draws an arc or clamps a handle.

import type { PreviewWeapon } from '../../protocol/modelPreview';
import type { AnimationAction } from './animationNames';

/**
 * Whether the clip on the playhead shows the unit deployed.
 *
 * A `deployed_*` clip is played in the state, wherever its playhead is. The deploy clip itself is the way
 * IN, so it counts only once it is held at its end - mid-clip the engine reports `Is_Deploying`, not
 * deployed. The undeploy clip is the way out and never counts.
 */
export function isDeployedClip(
    action: Pick<AnimationAction, 'action' | 'stance'> | null, atEnd: boolean,
): boolean {
    if (action === null) {
        return false;
    }

    if (action.stance === 'deployed') {
        return true;
    }

    // `(^|_)` so a mission-prefixed deploy counts and `undeploy` never does.
    return /(^|_)deploy$/.test(action.action) && atEnd;
}

/**
 * The weapons as the engine reads them in this state.
 *
 * Returns the given list itself while not deployed, so memoised consumers see no change. Otherwise a copy
 * per weapon that carries deployed values - only unit weapons on a unit that can deploy do - with the arc
 * and the turret limits replaced. Everything downstream keeps reading the ordinary fields.
 */
export function weaponsInState(weapons: readonly PreviewWeapon[], deployed: boolean): readonly PreviewWeapon[] {
    if (!deployed) {
        return weapons;
    }

    return weapons.map(weapon => {
        const turret = weapon.turret;
        const hasArc = present(weapon.deployedConeWidthDegrees) || present(weapon.deployedConeHeightDegrees);
        const hasSwing = turret !== null && turret !== undefined
            && (present(turret.deployedRotateExtentDegrees) || present(turret.deployedElevateExtentDegrees));

        if (!hasArc && !hasSwing) {
            return weapon;
        }

        return {
            ...weapon,
            coneWidthDegrees: weapon.deployedConeWidthDegrees ?? weapon.coneWidthDegrees,
            coneHeightDegrees: weapon.deployedConeHeightDegrees ?? weapon.coneHeightDegrees,
            turret: hasSwing && turret !== null && turret !== undefined
                ? {
                    ...turret,
                    rotateExtentDegrees: turret.deployedRotateExtentDegrees ?? turret.rotateExtentDegrees,
                    elevateExtentDegrees: turret.deployedElevateExtentDegrees ?? turret.elevateExtentDegrees,
                }
                : turret,
        };
    });
}

/** The wire sends an unset value as absent or as null; both mean "no deployed value". */
function present(value: number | null | undefined): value is number {
    return value !== null && value !== undefined;
}
