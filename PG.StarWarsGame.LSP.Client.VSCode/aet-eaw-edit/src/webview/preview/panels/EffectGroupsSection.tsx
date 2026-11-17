// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model's particle systems by the group that switches them.

import * as l10n from "@vscode/l10n";
import { DockSection } from "../../shared/DockSection";
import { GROUP_STATE_TITLES } from "../previewLabels";
import { groupState, type ParticleGroup } from "../particleScene";

export interface EffectGroupsSectionProps {
    groups: ParticleGroup[];
    shownSystems: Set<string>;
    toggleGroup: (group: ParticleGroup, visible: boolean) => void;
}

export function EffectGroupsSection({
    groups,
    shownSystems,
    toggleGroup,
}: EffectGroupsSectionProps): React.JSX.Element {
    return (
        <DockSection title={l10n.t("Effect groups")} count={groups.length}>
            <ul className="part-list">
                {groups.map((group) => {
                    const state = groupState(group, shownSystems);

                    return (
                        <li key={group.id}>
                            <label className="field-label">
                                <input
                                    type="checkbox"
                                    checked={state === "all"}
                                    ref={(box) => {
                                        // A split group is neither on nor off,
                                        // and only the DOM property can say so.
                                        if (box !== null) {
                                            box.indeterminate =
                                                state === "some";
                                        }
                                    }}
                                    title={GROUP_STATE_TITLES[state]}
                                    onChange={(e) =>
                                        toggleGroup(group, e.target.checked)
                                    }
                                />
                                {group.label}
                                <span className="section-count">
                                    {l10n.t("x{0}", group.ids.length)}
                                </span>
                            </label>
                        </li>
                    );
                })}
            </ul>
        </DockSection>
    );
}
