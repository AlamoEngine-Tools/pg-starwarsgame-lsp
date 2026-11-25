// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The hardpoints, grouped by type, each a card that aims, destroys, sweeps and jumps.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import { Fragment } from "react";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { DockSection } from "../../shared/DockSection";
import { safeImageSource } from "../../shared/safeImage";
import { PartCard } from "../PartCard";
import { post } from "../previewPost";
import {
    FireBoneButtons,
    TurretAimRow,
    WeaponArcsToggle,
} from "./WeaponRowControls";
import { Icon } from "../../shared/Icon";
import { toggleSelected } from "../previewTree";
import { type PreviewViewport } from "../viewport";
import { healthBar } from "../hardpointCards";
import { reticleFor } from "../reticles";
import { type HardpointGroup } from "../hardpointGroups";

export interface HardpointsSectionProps {
    boneRows: Map<string, string>;
    combat: CombatLens;
    foldedGroups: ReadonlySet<string>;
    hardpointFilter: string;
    hardpointGroups: HardpointGroup[];
    hardpointSearchOpen: boolean;
    hardpointSearchRef: RefObject<HTMLDivElement | null>;
    infoCard: string | null;
    scene: PreviewScene | null;
    selectBoneRow: (rowId: string | undefined) => void;
    selectHardpoint: (hardpointId: string) => void;
    selected: ReadonlySet<string>;
    setFoldedGroups: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setHardpointFilter: Dispatch<SetStateAction<string>>;
    setHardpointSearchPressed: Dispatch<SetStateAction<boolean>>;
    setInfoCard: Dispatch<SetStateAction<string | null>>;
    setWeaponArcs: (id: string, on: boolean) => void;
    viewportRef: RefObject<PreviewViewport | null>;
}

export function HardpointsSection({
    boneRows,
    combat,
    foldedGroups,
    hardpointFilter,
    hardpointGroups,
    hardpointSearchOpen,
    hardpointSearchRef,
    infoCard,
    scene,
    selectBoneRow,
    selectHardpoint,
    selected,
    setFoldedGroups,
    setHardpointFilter,
    setHardpointSearchPressed,
    setInfoCard,
    setWeaponArcs,
    viewportRef,
}: HardpointsSectionProps): React.JSX.Element {
    return (
        <DockSection title={l10n.t("Hardpoints")} count={combat.cards.length}>
            <div className="field-note">
                {l10n.t(
                    "Hover a targeting mark to see its tracked art; click one to aim the attacker at that hardpoint.",
                )}
            </div>

            {/* The tree's own search element, in the same shape: a magnifier
                                    that opens leftward into a field, with the icons anchored to
                                    the right so nothing moves when it does. Filters on the TYPE,
                                    which is what the groups are, and on the name, which is what a
                                    reader looking for one hardpoint knows. */}
            <div
                className={
                    "tree-search hardpoint-search" +
                    (hardpointSearchOpen ? " open" : "")
                }
                ref={hardpointSearchRef}
            >
                {hardpointSearchOpen && (
                    <input
                        className="tree-filter"
                        type="text"
                        autoFocus
                        value={hardpointFilter}
                        placeholder={l10n.t("Filter by type or name")}
                        onChange={(e) => setHardpointFilter(e.target.value)}
                        onKeyDown={(e) => {
                            if (e.key === "Escape") {
                                e.stopPropagation();
                                setHardpointFilter("");
                                setHardpointSearchPressed(false);
                            }
                        }}
                    />
                )}
                <div className="tree-search-icons">
                    <IconButton
                        icon="search"
                        className={
                            "tree-search-toggle" +
                            (hardpointSearchOpen ? " active" : "")
                        }
                        expanded={hardpointSearchOpen}
                        title={
                            hardpointSearchOpen
                                ? l10n.t("Close the filter")
                                : l10n.t("Filter by type or name")
                        }
                        onClick={() => {
                            if (hardpointSearchOpen) {
                                setHardpointFilter("");
                            }
                            setHardpointSearchPressed((open) => !open);
                        }}
                    />
                </div>
            </div>

            {hardpointGroups.length === 0 && (
                <div className="field-note">
                    {l10n.t("Nothing matches that filter.")}
                </div>
            )}

            {hardpointGroups.map((group) => (
                <Fragment key={group.type}>
                    <button
                        type="button"
                        className="card-group-title"
                        aria-expanded={!foldedGroups.has(group.type)}
                        title={
                            foldedGroups.has(group.type)
                                ? l10n.t(
                                      "Show the {0} hardpoints",
                                      group.label.toLowerCase(),
                                  )
                                : l10n.t(
                                      "Hide the {0} hardpoints",
                                      group.label.toLowerCase(),
                                  )
                        }
                        onClick={() =>
                            setFoldedGroups((current) =>
                                toggleSelected(current, group.type),
                            )
                        }
                    >
                        <Icon
                            name={
                                foldedGroups.has(group.type)
                                    ? "collapsed"
                                    : "expanded"
                            }
                            size={13}
                        />
                        {group.label}
                        <span className="section-count">
                            {group.cards.length}
                        </span>
                    </button>
                    {!foldedGroups.has(group.type) && (
                        <ul className="part-list hardpoint-cards">
                            {group.cards.map((card) => (
                                <PartCard
                                    key={card.id}
                                    data={{
                                        card: card.id,
                                    }}
                                    selected={combat.fireTarget === card.id}
                                    gone={card.destroyed}
                                    // Picking the card aims at it, which is the same act as picking it in the attacker's Fire at
                                    // list and the same act as clicking its targeting mark. One meaning, three ways in.
                                    onClick={() => selectHardpoint(card.id)}
                                    definition={{
                                        title: l10n.t(
                                            "Go to where {0} is defined",
                                            card.id,
                                        ),
                                        onGo: () =>
                                            post({
                                                type: "revealDefinition",
                                                value: card.id,
                                                referenceType: "HardPoint",
                                            }),
                                    }}
                                    // The game's own words for it. The Type is what picks its reticle; the tooltip is what a
                                    // player reads.
                                    type={card.type}
                                    lead={
                                        <>
                                            {/* The art the GAME draws over this hardpoint,
                                                    the same image the marks on the model use. It
                                                    is picked by Type, so seeing it here is how a
                                                    reader checks that the Type they wrote resolves
                                                    to the mark they expected. */}
                                            {reticleFor(
                                                scene?.reticles,
                                                card.type,
                                            ) !== null && (
                                                <img
                                                    className="card-reticle"
                                                    src={safeImageSource(
                                                        reticleFor(
                                                            scene?.reticles,
                                                            card.type,
                                                        ),
                                                    )}
                                                    alt=""
                                                    title={
                                                        card.isTargetable
                                                            ? l10n.t(
                                                                  "The mark the game draws over this",
                                                              )
                                                            : l10n.t(
                                                                  "Is_Targetable is off, so the game draws no mark over this",
                                                              )
                                                    }
                                                />
                                            )}
                                        </>
                                    }
                                    name={card.id}
                                    actions={
                                        <>
                                            {/* The glyph follows the tooltip: a wrench once it
                                                    is destroyed, because that is what pressing it
                                                    does now. Alive, it is the same reticle the
                                                    stage's Fire wears - shooting a hardpoint off
                                                    and pressing this are one outcome, and two
                                                    glyphs for it said they were different things. */}
                                            <IconButton
                                                icon={
                                                    card.destroyed
                                                        ? "repair"
                                                        : "fire"
                                                }
                                                className={
                                                    card.destroyed
                                                        ? "active"
                                                        : undefined
                                                }
                                                pressed={card.destroyed}
                                                title={
                                                    card.destroyed
                                                        ? l10n.t(
                                                              "Repair hardpoint",
                                                          )
                                                        : l10n.t(
                                                              "Destroy hardpoint",
                                                          )
                                                }
                                                disabled={!card.isDestroyable}
                                                disabledReason={l10n.t(
                                                    "Is_Destroyable is off",
                                                )}
                                                onClick={(event) => {
                                                    event.stopPropagation();
                                                    combat.setHardpointDestroyed(
                                                        card.id,
                                                        !card.destroyed,
                                                    );
                                                }}
                                            />

                                            {card.weapon !== null && (
                                                <WeaponArcsToggle
                                                    row={card.weapon}
                                                    hiddenWeapons={
                                                        combat.hiddenWeapons
                                                    }
                                                    setWeaponArcs={
                                                        setWeaponArcs
                                                    }
                                                />
                                            )}

                                            {/* The sweep belongs to the hardpoint that
                                                    declares the traverse. One button swinging
                                                    everything at once could not say what it would
                                                    do on a hull whose hardpoints differ. */}
                                            <IconButton
                                                icon="loop"
                                                className={
                                                    combat.sweeping.has(card.id)
                                                        ? "active"
                                                        : undefined
                                                }
                                                pressed={combat.sweeping.has(
                                                    card.id,
                                                )}
                                                title={l10n.t("Move turret")}
                                                disabled={!card.sweepable}
                                                disabledReason={l10n.t(
                                                    "No traverse declared",
                                                )}
                                                onClick={(event) => {
                                                    event.stopPropagation();
                                                    combat.setSweeping(
                                                        (current) =>
                                                            toggleSelected(
                                                                current,
                                                                card.id,
                                                            ),
                                                    );
                                                }}
                                            />

                                            {/* Pushed to the far end: the three on the
                                                        left DO something to the model, and this
                                                        one only says what the file holds. */}
                                            <IconButton
                                                icon="details"
                                                className={
                                                    "card-info-btn" +
                                                    (infoCard === card.id
                                                        ? " active"
                                                        : "")
                                                }
                                                expanded={infoCard === card.id}
                                                title={l10n.t("Info")}
                                                onClick={(event) => {
                                                    event.stopPropagation();
                                                    setInfoCard((open) =>
                                                        open === card.id
                                                            ? null
                                                            : card.id,
                                                    );
                                                }}
                                            />
                                        </>
                                    }
                                >
                                    {/* What a PLAYER reads, with the key the author wrote
                                                on the hover. The card showed the key itself before
                                                the server resolved it, which is the one line on a
                                                card that nobody outside the files ever sees. */}
                                    {card.tooltip !== null && (
                                        <span
                                            className="detail wraps"
                                            title={card.tooltipKey ?? undefined}
                                        >
                                            {card.tooltip}
                                        </span>
                                    )}

                                    {/* The key alone, when it resolved to nothing. Said as
                                                a key rather than dressed up as text: a row with no
                                                translation is the localisation editor's to fix, and
                                                showing it as prose would hide that. */}
                                    {card.tooltip === null &&
                                        card.tooltipKey !== null && (
                                            <span
                                                className="detail card-type"
                                                title={l10n.t(
                                                    "This key resolves to no text",
                                                )}
                                            >
                                                {card.tooltipKey}
                                            </span>
                                        )}

                                    {/* The pool, as a bar. The colour is the ramp the
                                                targeting marks on the model use, so a mark gone
                                                orange out there and a bar gone orange in here are
                                                saying the same thing. */}
                                    {healthBar(
                                        card,
                                        combat.hardpointHealth[card.id] ?? null,
                                    ) !== null && (
                                        <span className="card-health">
                                            <span
                                                className={
                                                    "card-health-fill" +
                                                    (card.destroyed
                                                        ? " gone"
                                                        : "")
                                                }
                                                style={{
                                                    width: `${Math.round(
                                                        healthBar(
                                                            card,
                                                            combat
                                                                .hardpointHealth[
                                                                card.id
                                                            ] ?? null,
                                                        )!.fraction * 100,
                                                    )}%`,
                                                    background: healthBar(
                                                        card,
                                                        combat.hardpointHealth[
                                                            card.id
                                                        ] ?? null,
                                                    )!.colour,
                                                }}
                                            />
                                            <span className="card-health-label">
                                                {
                                                    healthBar(
                                                        card,
                                                        combat.hardpointHealth[
                                                            card.id
                                                        ] ?? null,
                                                    )!.label
                                                }
                                            </span>
                                        </span>
                                    )}

                                    {card.weapon !== null && (
                                        <TurretAimRow
                                            row={card.weapon}
                                            draggableTurrets={
                                                combat.draggableTurrets
                                            }
                                            turretAims={combat.turretAims}
                                            viewportRef={viewportRef}
                                        />
                                    )}

                                    {card.weapon !== null && (
                                        <FireBoneButtons
                                            row={card.weapon}
                                            boneRows={boneRows}
                                            selected={selected}
                                            selectBoneRow={selectBoneRow}
                                        />
                                    )}
                                </PartCard>
                            ))}
                        </ul>
                    )}
                </Fragment>
            ))}
        </DockSection>
    );
}
