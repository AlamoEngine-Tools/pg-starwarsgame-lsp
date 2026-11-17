// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The controls a weapon row carries, on a hardpoint card and on a unit weapon card alike: its fire
// arcs, where its turret points, and its fire bones.

import * as l10n from "@vscode/l10n";
import type { RefObject } from "react";

import { IconButton } from "../../shared/Button";
import { Icon } from "../../shared/Icon";
import { ViewRow } from "../../shared/ViewRow";
import { muzzleLabel } from "../hardpointCards";
import {
    aimText,
    TURRET_AT_REST,
    type TurretAim,
    type TurretHandle,
} from "../turretHandles";
import type { PreviewViewport } from "../viewport";
import { fireBoneTitle, type WeaponRow, weaponTitle } from "../weaponRows";

export interface WeaponArcsToggleProps {
    row: WeaponRow;
    hiddenWeapons: ReadonlySet<string>;
    setWeaponArcs: (id: string, on: boolean) => void;
}

/** Shows or hides this weapon's fire cones. */
export function WeaponArcsToggle({
    row,
    hiddenWeapons,
    setWeaponArcs,
}: WeaponArcsToggleProps): React.JSX.Element {
    const drawable = row.arcs.length > 0;

    return (
        <IconButton
            icon="arcs"
            className={
                drawable && !row.destroyed && !hiddenWeapons.has(row.id)
                    ? "active"
                    : undefined
            }
            pressed={drawable && !hiddenWeapons.has(row.id)}
            title={weaponTitle(row)}
            disabled={!drawable || row.destroyed}
            // weaponTitle already phrases the state, including the two that disable this.
            disabledReason={weaponTitle(row)}
            onClick={(event) => {
                event.stopPropagation();
                setWeaponArcs(row.id, hiddenWeapons.has(row.id));
            }}
        />
    );
}

export interface TurretAimRowProps {
    row: WeaponRow;
    draggableTurrets: readonly TurretHandle[];
    turretAims: ReadonlyMap<string, TurretAim>;
    viewportRef: RefObject<PreviewViewport | null>;
}

/**
 * Where this turret is pointed right now, beside the numbers that bound it.
 *
 * Only for a row that HAS handles, because it is a readout of a control: printing "Yaw 0 deg"
 * on a turret nobody can drag says nothing. It names its stops out loud - a handle that will
 * not move is otherwise indistinguishable from a dropped pointer event - and offers the way
 * back to the pose the model authored, which dragging deliberately does not do on release.
 */
export function TurretAimRow({
    row,
    draggableTurrets,
    turretAims,
    viewportRef,
}: TurretAimRowProps): React.JSX.Element | null {
    const handle = draggableTurrets.find((one) => one.id === row.id);

    if (handle === undefined) {
        return null;
    }

    const aim = turretAims.get(row.id) ?? TURRET_AT_REST;
    const moved = aim.yaw !== 0 || aim.pitch !== 0;

    return (
        <ViewRow className="turret-aim">
            <span className="detail">{aimText(handle, aim)}</span>
            <IconButton
                icon="reset"
                title={l10n.t(
                    "Return this turret to the pose the model authored",
                )}
                disabled={!moved}
                disabledReason={l10n.t(
                    "This turret is already at its authored pose",
                )}
                onClick={(event) => {
                    event.stopPropagation();
                    viewportRef.current?.resetTurretAim(row.id);
                }}
            />
        </ViewRow>
    );
}

export interface FireBoneButtonsProps {
    row: WeaponRow;
    boneRows: ReadonlyMap<string, string>;
    selected: ReadonlySet<string>;
    selectBoneRow: (rowId: string | undefined) => void;
}

/**
 * The fire bones, as buttons that point at them in the model.
 *
 * They TOGGLE into the selection rather than replacing it, which is what was wrong before: a
 * weapon with two fire points could never have both boxed at once, and seeing the pair is the
 * one thing looking at two fire points is for.
 */
export function FireBoneButtons({
    row,
    boneRows,
    selected,
    selectBoneRow,
}: FireBoneButtonsProps): React.JSX.Element {
    return (
        <span className="bone-picks">
            {row.fireBones.map((bone, index) => {
                const rowId = boneRows.get(bone.toLowerCase());

                return (
                    <button
                        key={`${bone}#${index}`}
                        className={
                            "muzzle-row" +
                            (rowId !== undefined && selected.has(rowId)
                                ? " selected"
                                : "")
                        }
                        disabled={rowId === undefined}
                        title={fireBoneTitle(bone, rowId !== undefined)}
                        onClick={(event) => {
                            event.stopPropagation();
                            selectBoneRow(rowId);
                        }}
                    >
                        <Icon name="skeleton" size={13} />
                        <span className="muzzle-slot">
                            {muzzleLabel(index)}
                        </span>
                        <span className="muzzle-bone">{bone}</span>
                    </button>
                );
            })}
        </span>
    );
}
