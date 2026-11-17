// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The weapon built to shoot the subject with. INVERTED against the rest of the lens: everything else
// describes the subject, this describes the attacker.

import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import {
    type PreviewScene,
    type PreviewTargetDefence,
} from "../../../protocol/modelPreview";
import { Button } from "../../shared/Button";
import { DockSection } from "../../shared/DockSection";
import { Field } from "../../shared/Field";
import { ViewRow } from "../../shared/ViewRow";
import { post } from "../previewPost";
import { Icon } from "../../shared/Icon";
import {
    armorFactor,
    attackerFromProjectile,
    damageSwitches,
    projectileChoices,
} from "../attacker";

export interface AttackerSectionProps {
    combat: CombatLens;
    defence: PreviewTargetDefence;
    scene: PreviewScene;
}

export function AttackerSection({
    combat,
    defence,
    scene,
}: AttackerSectionProps): React.JSX.Element {
    return (
        <DockSection
            title={l10n.t("Attacker")}
            count={defence.isShielded ? "shielded" : "unshielded"}
        >
            <div className="field-note">
                {l10n.t(
                    "The model on stage is the TARGET. Build a weapon here and fire it at the unit or at one hardpoint.",
                )}
            </div>

            <Field label={l10n.t("Damage")}>
                <input
                    type="number"
                    min={0}
                    value={combat.attacker.damage}
                    onChange={(e) =>
                        combat.setAttacker((current) => ({
                            ...current,
                            damage: Number(e.target.value),
                        }))
                    }
                />
            </Field>

            <Field
                label={l10n.t("Damage type")}
                valueTitle={l10n.t(
                    "What the damage table says against this target's armor. A pair the table does not name is 1.0, which is over half of them.",
                )}
                value={
                    <>
                        {/* Off the LIVE defence: a ship whose generators are
                                            gone is unshielded, so the factor a shot would
                                            actually take is the hull's. Reading the scene's
                                            own copy showed the shield column beside a bar
                                            that had just gone flat. */}
                        {l10n.t(
                            "x{0}",
                            armorFactor(
                                combat.attacker.shield &&
                                    combat.liveDefence?.isShielded === true
                                    ? defence.shieldFactors
                                    : defence.hullFactors,
                                combat.attacker.damageType,
                            ),
                        )}
                    </>
                }
            >
                <select
                    value={combat.attacker.damageType}
                    onChange={(e) =>
                        combat.setAttacker((current) => ({
                            ...current,
                            damageType: e.target.value,
                        }))
                    }
                    title={l10n.t("Which damage type the shot does")}
                >
                    {/* The reader's own value first when the tree does not
                                            declare it - a preset saved against another mod must not
                                            silently become whatever happens to sort first. */}
                    {!defence.damageTypes.includes(
                        combat.attacker.damageType,
                    ) && (
                        <option value={combat.attacker.damageType}>
                            {l10n.t(
                                "{0}{1}(not in this tree)",
                                combat.attacker.damageType,
                                " ",
                            )}
                        </option>
                    )}
                    {defence.damageTypes.map((type) => (
                        <option key={type} value={type}>
                            {type}
                        </option>
                    ))}
                </select>
            </Field>

            {/* The three switches a projectile carries. They decide entirely
                                    what a hit touches - see `attacker.ts` for the four rules. */}
            <ViewRow>
                {damageSwitches(combat.poolOptions).map(
                    ({ id, label, title }) => (
                        <label key={id} className="field-label" title={title}>
                            <input
                                type="checkbox"
                                checked={combat.attacker[id]}
                                onChange={(e) =>
                                    combat.setAttacker((current) => ({
                                        ...current,
                                        [id]: e.target.checked,
                                    }))
                                }
                            />
                            {label}
                        </label>
                    ),
                )}
            </ViewRow>

            {/* AREA DAMAGE. A blast reaches everything within its range as
                                    well as the thing it hit, and the two numbers are independent -
                                    `Proj_Veers_AT_AT_Max_Power_Laser_Red` declares a direct damage
                                    of 0.0 alongside a blast of 40, so a weapon can do all of its
                                    work here and none above.

                                    Range is what decides who ELSE is caught, so a blast with no
                                    range reaches nobody and the fields below it are moot - they
                                    disable rather than vanish, since a reader who has typed a
                                    damage and seen nothing happen needs to see the reason. */}
            <Field label={l10n.t("Blast damage")}>
                <input
                    type="number"
                    min={0}
                    value={combat.attacker.blastDamage}
                    title={l10n.t(
                        "Projectile_Blast_Area_Damage - applied to the target AND to everything inside the range below",
                    )}
                    onChange={(e) =>
                        combat.setAttacker((current) => ({
                            ...current,
                            blastDamage: Number(e.target.value),
                        }))
                    }
                />
            </Field>

            <Field label={l10n.t("Blast range")}>
                <input
                    type="number"
                    min={0}
                    value={combat.attacker.blastRange}
                    title={l10n.t(
                        "Projectile_Blast_Area_Range, in engine units. The target itself is always hit.",
                    )}
                    onChange={(e) =>
                        combat.setAttacker((current) => ({
                            ...current,
                            blastRange: Number(e.target.value),
                        }))
                    }
                />
            </Field>

            <ViewRow>
                <label
                    className="field-label"
                    title={l10n.t(
                        "Projectile_Blast_Area_Dropoff - the blast weakens with distance instead of being flat",
                    )}
                >
                    <input
                        type="checkbox"
                        checked={combat.attacker.blastDropoff}
                        disabled={combat.attacker.blastRange <= 0}
                        onChange={(e) =>
                            combat.setAttacker((current) => ({
                                ...current,
                                blastDropoff: e.target.checked,
                            }))
                        }
                    />
                    {l10n.t("Falls off")}
                </label>

                <label
                    className="field-label"
                    title={l10n.t(
                        "Projectile_Blast_Area_Dropoff_Tiers - how many bands it falls off in. Shipped values are 3, 4 or 5.",
                    )}
                >
                    {l10n.t("Tiers")}
                    <input
                        type="number"
                        min={0}
                        max={16}
                        className="tier-count"
                        value={combat.attacker.blastDropoffTiers}
                        disabled={
                            !combat.attacker.blastDropoff ||
                            combat.attacker.blastRange <= 0
                        }
                        onChange={(e) =>
                            combat.setAttacker((current) => ({
                                ...current,
                                blastDropoffTiers: Number(e.target.value),
                            }))
                        }
                    />
                </label>
            </ViewRow>

            {/* Disabled rather than absent when the subject fires nothing: a
                                    control that comes and goes reads as a feature that does. */}
            {/* EVERY projectile in the tree, not the handful this subject
                                    fires - you are building a weapon to shoot AT it, so its own
                                    armament is the wrong list. 105 of them, so the search is the
                                    way through rather than an optional extra. */}
            <Field
                label={l10n.t("Fill from a projectile")}
                value={<>{scene.projectileCatalog?.length ?? 0}</>}
            >
                <input
                    type="text"
                    placeholder={l10n.t("Search projectiles")}
                    value={combat.projectileSearch}
                    onChange={(e) => combat.setProjectileSearch(e.target.value)}
                />
                <select
                    value=""
                    size={6}
                    disabled={(scene.projectileCatalog?.length ?? 0) === 0}
                    onChange={(e) => {
                        const picked = e.target.value;
                        const bolt = scene.projectiles?.find(
                            (p) => p.id === picked,
                        );

                        if (bolt !== undefined) {
                            // A COPY. Edits afterwards stick.
                            combat.setAttacker(
                                attackerFromProjectile(bolt, combat.attacker),
                            );
                            // Except the blast area, which has no field to edit
                            // and is read off the bolt when the shot resolves.
                            combat.setAttackerProjectile(bolt.id);
                            combat.setProjectileNote(null);
                        } else if (picked !== "") {
                            // Everything the subject does not fire is a NAME
                            // until it is fetched. It used to stop here and say
                            // so, which made the catalogue read as broken - and
                            // as incomplete, since 104 of its 105 entries do
                            // nothing on any given subject. Now it asks.
                            combat.setProjectileNote(`Loading ${picked}...`);
                            post({
                                type: "requestProjectile",
                                name: picked,
                            });
                        }
                    }}
                >
                    {projectileChoices(
                        scene.projectileCatalog ?? [],
                        combat.projectileSearch,
                    ).map((name) => (
                        <option key={name} value={name}>
                            {name}
                        </option>
                    ))}
                </select>
                {combat.projectileNote !== null && (
                    <span className="field-note">{combat.projectileNote}</span>
                )}
            </Field>

            {/* Presets are TIER 1: they describe the reader's testing habits,
                                    not this model, so they live in globalState beside the camera
                                    presets and survive opening a different ship. */}
            <Field
                label={l10n.t("Saved weapons")}
                value={<>{combat.attackerPresets.length}</>}
            >
                <select
                    value=""
                    disabled={combat.attackerPresets.length === 0}
                    onChange={(e) => {
                        const saved = combat.attackerPresets.find(
                            (p) => p.id === e.target.value,
                        );

                        if (saved !== undefined) {
                            const { id, name, ...weapon } = saved;
                            combat.setAttacker(weapon);
                        }
                    }}
                    title={
                        combat.attackerPresets.length === 0
                            ? l10n.t(
                                  "Name a configuration below to save it here",
                              )
                            : l10n.t(
                                  "Loads a saved weapon into the fields above",
                              )
                    }
                >
                    <option value="">{l10n.t("Recall a weapon...")}</option>
                    {combat.attackerPresets.map((saved) => (
                        <option key={saved.id} value={saved.id}>
                            {saved.name}
                        </option>
                    ))}
                </select>
            </Field>

            <ViewRow>
                <input
                    type="text"
                    placeholder={l10n.t("Name this weapon")}
                    value={combat.presetName}
                    onChange={(e) => combat.setPresetName(e.target.value)}
                />
                <Button
                    compact
                    title={l10n.t(
                        "Saves these values under that name, for any model",
                    )}
                    disabled={combat.presetName.trim() === ""}
                    disabledReason={l10n.t("Give the weapon a name to save it")}
                    onClick={() => {
                        const name = combat.presetName.trim();

                        combat.setAttackerPresets((current) => [
                            // Saving over a name REPLACES it. Two rows reading
                            // the same thing is worse than losing the old one,
                            // which is what the reader just asked for anyway.
                            ...current.filter((p) => p.name !== name),
                            {
                                ...combat.attacker,
                                id: `atk-${Date.now()}`,
                                name,
                            },
                        ]);
                        combat.setPresetName("");
                    }}
                >
                    <Icon name="save" />
                    {l10n.t("Save")}
                </Button>
                <Button
                    compact
                    title={l10n.t("Remove every saved weapon")}
                    disabled={combat.attackerPresets.length === 0}
                    disabledReason={l10n.t("No saved weapons to remove")}
                    onClick={() => combat.setAttackerPresets([])}
                >
                    <Icon name="remove" />
                    {l10n.t("Clear")}
                </Button>
            </ViewRow>
        </DockSection>
    );
}
