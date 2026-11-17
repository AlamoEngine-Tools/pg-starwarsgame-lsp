// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The weapons the unit carries itself rather than on a hardpoint - same card, no pool.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import { IconButton } from "../../shared/Button";
import { DockSection } from "../../shared/DockSection";
import { PartCard } from "../PartCard";
import {
    FireBoneButtons,
    TurretAimRow,
    WeaponArcsToggle,
} from "./WeaponRowControls";
import { toggleSelected } from "../previewTree";
import { type PreviewViewport } from "../viewport";
import { unitWeaponFacts } from "../hardpointCards";

export interface UnitWeaponsSectionProps {
    boneRows: Map<string, string>;
    combat: CombatLens;
    infoCard: string | null;
    selectBoneRow: (rowId: string | undefined) => void;
    selected: ReadonlySet<string>;
    setInfoCard: Dispatch<SetStateAction<string | null>>;
    setWeaponArcs: (id: string, on: boolean) => void;
    viewportRef: RefObject<PreviewViewport | null>;
}

export function UnitWeaponsSection({
    boneRows,
    combat,
    infoCard,
    selectBoneRow,
    selected,
    setInfoCard,
    setWeaponArcs,
    viewportRef,
}: UnitWeaponsSectionProps): React.JSX.Element {
    return (
        <DockSection
            title={l10n.t("Unit weapons")}
            count={combat.looseWeapons.length}
        >
            {/* The hardpoints' own card, which is what the user asked for -
                                    same shape, same list, same info button. What a unit weapon has
                                    not got is a POOL: it is not a target, so there is no health bar
                                    and no destroy switch. What it does have is its muzzles, and
                                    often more of them than a hardpoint carries. */}
            <ul className="part-list hardpoint-cards">
                {combat.looseWeapons.map((row) => (
                    <PartCard
                        key={row.id}
                        variant="weapon-card"
                        data={{ card: row.id }}
                        title={row.id}
                        // What the file calls it. The hardpoint card puts its Type here for the same reason: it is
                        // game data a reader greps for, not prose.
                        type={
                            row.label !== "" && row.label !== row.name
                                ? row.label
                                : null
                        }
                        name={row.name}
                        actions={
                            <>
                                {
                                    <WeaponArcsToggle
                                        row={row}
                                        hiddenWeapons={combat.hiddenWeapons}
                                        setWeaponArcs={setWeaponArcs}
                                    />
                                }

                                {/* The sweep, which until now lived ONLY on a
                                                    hardpoint card - so the AT-AA, whose turret is
                                                    on its weapon and which has no hardpoints at
                                                    all, had nothing to press. */}
                                <IconButton
                                    icon="loop"
                                    className={
                                        combat.sweeping.has(row.id)
                                            ? "active"
                                            : undefined
                                    }
                                    pressed={combat.sweeping.has(row.id)}
                                    title={l10n.t("Move turret")}
                                    disabled={
                                        !combat.sweepable.some(
                                            (s) => s.id === row.id,
                                        )
                                    }
                                    disabledReason={l10n.t(
                                        "No traverse declared",
                                    )}
                                    onClick={(event) => {
                                        event.stopPropagation();
                                        combat.setSweeping((current) =>
                                            toggleSelected(current, row.id),
                                        );
                                    }}
                                />

                                <IconButton
                                    icon="details"
                                    className={
                                        "card-info-btn" +
                                        (infoCard === row.id ? " active" : "")
                                    }
                                    expanded={infoCard === row.id}
                                    title={l10n.t("Info")}
                                    onClick={(event) => {
                                        event.stopPropagation();
                                        setInfoCard((open) =>
                                            open === row.id ? null : row.id,
                                        );
                                    }}
                                />
                            </>
                        }
                    >
                        {/* The turret, where it declares one. No bone line: a
                                                unit weapon hangs off the hull rather than an
                                                attachment bone of its own. */}
                        {unitWeaponFacts(row).map((fact) => (
                            <span className="detail" key={fact}>
                                {fact}
                            </span>
                        ))}

                        {
                            <TurretAimRow
                                row={row}
                                draggableTurrets={combat.draggableTurrets}
                                turretAims={combat.turretAims}
                                viewportRef={viewportRef}
                            />
                        }

                        {
                            <FireBoneButtons
                                row={row}
                                boneRows={boneRows}
                                selected={selected}
                                selectBoneRow={selectBoneRow}
                            />
                        }
                    </PartCard>
                ))}
            </ul>
        </DockSection>
    );
}
