// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The dock content: the inventories of the lens in front - the Model tree, the effect groups and
// emitters, the clip library, and the weapons, hardpoints and death clones in Gameplay.

import type { AnimationTransport } from "../useAnimationTransport";
import type { CombatLens } from "../useCombatLens";
import type { Dispatch, SetStateAction, RefObject } from "react";
import type { Inspector } from "../useInspector";
import type { Flyouts } from "../useFlyouts";
import type { ModelTree } from "../useModelTree";
import type { ParticleEffects } from "../useParticleEffects";
import type { Abilities } from "../useAbilities";
import type { HardpointList } from "../useHardpointList";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { DockSection } from "../../shared/DockSection";
import { AnimationsSection } from "./AnimationsSection";
import { ModelTreeSection } from "./ModelTreeSection";
import { DeathCloneSection } from "./DeathCloneSection";
import { HardpointsSection } from "./HardpointsSection";
import { UnitWeaponsSection } from "./UnitWeaponsSection";
import { EffectGroupsSection } from "./EffectGroupsSection";
import { EmittersSection } from "./EmittersSection";
import { type TreeItem } from "../previewTree";
import { type PreviewViewport } from "../viewport";
import { type ParticleGroup } from "../particleScene";
import { type PreviewMode } from "../previewMode";

export interface DockContentProps {
    abilityState: Abilities;
    anchor: string | null;
    boneRows: Map<string, string>;
    clearSelection: () => void;
    combat: CombatLens;
    detailsRow: string | null;
    emitters: string[];
    flyouts: Flyouts;
    groups: ParticleGroup[];
    hardpointList: HardpointList;
    hiddenEmitters: ReadonlySet<number>;
    inspection: Inspector;
    mode: PreviewMode;
    particles: ParticleEffects;
    player: AnimationTransport;
    scene: PreviewScene | null;
    selectBoneRow: (rowId: string | undefined) => void;
    selectHardpoint: (hardpointId: string) => void;
    selected: ReadonlySet<string>;
    setAnchor: Dispatch<SetStateAction<string | null>>;
    setSelected: Dispatch<SetStateAction<ReadonlySet<string>>>;
    setWeaponArcs: (id: string, on: boolean) => void;
    toggleGroup: (group: ParticleGroup, visible: boolean) => void;
    tree: ModelTree;
    treeItems: TreeItem[];
    viewportRef: RefObject<PreviewViewport | null>;
}

export function DockContent({
    abilityState,
    anchor,
    boneRows,
    clearSelection,
    combat,
    detailsRow,
    emitters,
    flyouts,
    groups,
    hardpointList,
    hiddenEmitters,
    inspection,
    mode,
    particles,
    player,
    scene,
    selectBoneRow,
    selectHardpoint,
    selected,
    setAnchor,
    setSelected,
    setWeaponArcs,
    toggleGroup,
    tree,
    treeItems,
    viewportRef,
}: DockContentProps): React.JSX.Element {
    return (
        <>
            {mode === "model" && treeItems.length > 0 && (
                // The count is inside `end` rather than given as `count`, because this
                // one carries a title of its own when rows are filtered out - which a
                // plain count cannot. The order within the group is still fixed, and
                // the group still takes the section's one auto margin.
                <ModelTreeSection
                    anchor={anchor}
                    clearSelection={clearSelection}
                    detailsRow={detailsRow}
                    openInspector={inspection.openInspector}
                    selected={selected}
                    setAnchor={setAnchor}
                    setSearchPressed={flyouts.setSearchPressed}
                    setSelected={setSelected}
                    tree={tree}
                />
            )}

            {/* Bulk switches over the same systems the tree lists one by one - not a
                            second tree. Each row reads its state back OUT of those systems, so a
                            group goes half-lit the moment one of its members is switched off in the
                            tree, and there is only ever one answer to "is this effect playing".

                            One row per MEANING. There used to be two groupers - one over the gates
                            the model assigns, one over whole system names - and they listed the
                            same systems twice: a Nebulon-B showed "Hardpoint damage" and
                            "p_hp_stardestroyer_damage" as separate rows over the same six proxies,
                            so ticking either moved the other and nothing said why. See
                            `FAMILIES` in `particleScene.ts` for how a meaning is recognised. */}
            {mode === "model" && groups.length > 0 && (
                <EffectGroupsSection
                    groups={groups}
                    shownSystems={tree.shownSystems}
                    toggleGroup={toggleGroup}
                />
            )}

            {emitters.length > 0 && (
                <EmittersSection
                    emitters={emitters}
                    hiddenEmitters={hiddenEmitters}
                    scene={scene}
                    toggleEmitter={particles.toggleEmitter}
                />
            )}

            {mode === "animation" && <AnimationsSection player={player} />}

            {/* Nothing to say only when the subject is unarmed, unattached AND unable.
                            A fighter carries its guns on the unit itself and declares no hardpoints
                            at all, and telling it there is nothing here would be wrong twice. */}
            {mode === "gameplay" &&
                (scene?.hardpoints.length ?? 0) === 0 &&
                combat.weapons.length === 0 &&
                abilityState.abilities.length === 0 && (
                    <DockSection title={l10n.t("Gameplay")}>
                        <div className="field-note">
                            {l10n.t(
                                "This unit declares no weapons, no hardpoints and no abilities, so there is nothing to destroy and no damage effects to drive.",
                            )}
                        </div>
                    </DockSection>
                )}

            {/* What is LEFT after the cards: the weapons without
                            hardpoint. A fighter carries its guns on the unit itself and declares no
                            hardpoints at all, so this is not an edge case - it is every fighter. */}
            {mode === "gameplay" && combat.looseWeapons.length > 0 && (
                <UnitWeaponsSection
                    boneRows={boneRows}
                    combat={combat}
                    infoCard={flyouts.infoCard}
                    selectBoneRow={selectBoneRow}
                    selected={selected}
                    setInfoCard={flyouts.setInfoCard}
                    setWeaponArcs={setWeaponArcs}
                    viewportRef={viewportRef}
                />
            )}

            {mode === "gameplay" && combat.cards.length > 0 && (
                <HardpointsSection
                    boneRows={boneRows}
                    combat={combat}
                    foldedGroups={hardpointList.foldedGroups}
                    hardpointFilter={hardpointList.hardpointFilter}
                    hardpointFilterError={hardpointList.hardpointFilterError}
                    hardpointFilterMode={hardpointList.hardpointFilterMode}
                    hardpointGroups={hardpointList.hardpointGroups}
                    hardpointSearchOpen={hardpointList.hardpointSearchOpen}
                    infoCard={flyouts.infoCard}
                    scene={scene}
                    selectBoneRow={selectBoneRow}
                    selectHardpoint={selectHardpoint}
                    selected={selected}
                    setFoldedGroups={hardpointList.setFoldedGroups}
                    setHardpointFilter={hardpointList.setHardpointFilter}
                    setHardpointFilterMode={
                        hardpointList.setHardpointFilterMode
                    }
                    setHardpointSearchPressed={
                        flyouts.setHardpointSearchPressed
                    }
                    setInfoCard={flyouts.setInfoCard}
                    setWeaponArcs={setWeaponArcs}
                    viewportRef={viewportRef}
                />
            )}
            {mode === "gameplay" &&
                ((scene?.deathClones?.length ?? 0) > 0 ||
                    (scene?.spinAway ?? null) !== null) && (
                    <DeathCloneSection combat={combat} scene={scene} />
                )}
        </>
    );
}
