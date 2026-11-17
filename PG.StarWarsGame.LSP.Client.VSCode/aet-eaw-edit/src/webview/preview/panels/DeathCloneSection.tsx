// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the unit leaves behind: the death clone per damage type, or the spin-away.

import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { DockSection } from "../../shared/DockSection";
import { PartCard } from "../PartCard";
import { post } from "../previewPost";
import { Icon } from "../../shared/Icon";
import { deathCloneRows } from "../deathClone";
import { spinAwaySummary } from "../spinAway";

export interface DeathCloneSectionProps {
    combat: CombatLens;
    scene: PreviewScene | null;
}

export function DeathCloneSection({
    combat,
    scene,
}: DeathCloneSectionProps): React.JSX.Element {
    return (
        <DockSection
            title={l10n.t("Death clone")}
            count={
                (scene?.deathClones?.length ?? 0) +
                (scene?.spinAway === null || scene?.spinAway === undefined
                    ? 0
                    : 1)
            }
        >
            {/* The automated one. Its own card, and marked the way a real clone
                                    is when it is what would happen - which is whenever the object
                                    declares no clone at all, since the two never coexist: not one
                                    of the 34 objects declaring `Spin_Away_On_Death` also declares a
                                    `Death_Clone`.

                                    The chance is READ OUT, not rolled - shipped values are 20% and
                                    40%, and a preview that honoured them would look broken four
                                    presses out of five. */}
            {scene?.spinAway !== null && scene?.spinAway !== undefined && (
                <ul className="part-list hardpoint-cards">
                    <PartCard
                        variant="clone-card"
                        selected={(scene?.deathClones?.length ?? 0) === 0}
                        title={l10n.t("Spin_Away_On_Death")}
                        type={
                            scene.spinAway.explosion ??
                            scene.deathExplosions ??
                            l10n.t("no explosion named")
                        }
                        lead={
                            <>
                                {(scene?.deathClones?.length ?? 0) === 0 && (
                                    <Icon name="check" />
                                )}
                            </>
                        }
                        name={l10n.t("Spins away")}
                    >
                        <span className="detail">
                            {spinAwaySummary(scene.spinAway)}
                        </span>
                    </PartCard>
                </ul>
            )}

            {/* One card per mapping: this damage type leaves that object
                                    behind. The same card shape the hardpoints and the abilities
                                    use, because it is the same kind of thing - a named piece of
                                    game data with somewhere to jump to.

                                    Which one you get depends on what KILLED it, which is the weapon
                                    built in the Attacker panel - so the marked card follows the
                                    damage type set there rather than being a static list. The tick
                                    says which, and used to need a sentence under the heading to say
                                    so. */}
            <ul className="part-list hardpoint-cards">
                {deathCloneRows(
                    scene?.deathClones ?? [],
                    combat.attacker.damageType,
                ).map((row) => (
                    <PartCard
                        key={`${row.label}:${row.objectId}`}
                        variant="clone-card"
                        selected={row.selected}
                        title={row.objectId}
                        definition={{
                            title: l10n.t(
                                "Go to where {0} is defined",
                                row.objectId,
                            ),
                            onGo: () =>
                                post({
                                    type: "revealDefinition",
                                    value: row.objectId,
                                    referenceType: "GameObject",
                                }),
                        }}
                        // The object it names, in the monospace a reader would grep their own files in.
                        type={row.objectId}
                        lead={<>{row.selected && <Icon name="check" />}</>}
                        name={row.label}
                    >
                        {/* The one thing worth saying beyond the mapping: there
                                                is no wreck. Either the object is not defined at all
                                                - a typo the game is silent about - or it declares
                                                no tactical model. */}
                        {!row.resolved && (
                            <span className="detail wraps card-unresolved">
                                {l10n.t("Not defined, or declares no model")}
                            </span>
                        )}
                    </PartCard>
                ))}
            </ul>
        </DockSection>
    );
}
