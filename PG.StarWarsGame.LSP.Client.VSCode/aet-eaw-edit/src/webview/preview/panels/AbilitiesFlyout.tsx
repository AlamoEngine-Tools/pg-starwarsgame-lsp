// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One row per ability: the art, the words the game shows, what it drives on this model, and the
// jump to where it is defined.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { Abilities } from "../useAbilities";
import * as l10n from "@vscode/l10n";
import { IconButton } from "../../shared/Button";
import { safeImageSource } from "../../shared/safeImage";
import { StageFlyout } from "../../shared/Stage";
import { PartCard } from "../PartCard";
import { post } from "../previewPost";
import { abilityFacts, gotoDefinitionTitle } from "../abilityRows";

export interface AbilitiesFlyoutProps {
    /** The pill on the command bar that opens this, which is where it opens. */
    abilitiesPillRef: RefObject<HTMLButtonElement | null>;
    abilitiesRef: RefObject<HTMLDivElement | null>;
    abilityState: Abilities;
    infoAbility: string | null;
    setAbilitiesOpen: Dispatch<SetStateAction<boolean>>;
    setInfoAbility: Dispatch<SetStateAction<string | null>>;
}

export function AbilitiesFlyout({
    abilitiesPillRef,
    abilitiesRef,
    abilityState,
    infoAbility,
    setAbilitiesOpen,
    setInfoAbility,
}: AbilitiesFlyoutProps): React.JSX.Element {
    return (
        <StageFlyout
            title={l10n.t("Abilities")}
            anchor={abilitiesPillRef}
            ref={abilitiesRef}
            count={abilityState.abilities.length}
            onClose={() => setAbilitiesOpen(false)}
        >
            <ul className="part-list ability-list">
                {abilityState.abilities.map((ability) => (
                    <PartCard
                        key={ability.type}
                        data={{ ability: ability.type }}
                        definition={{
                            title: gotoDefinitionTitle(ability),
                            disabled: ability.definition === null,
                            onGo: () =>
                                post({
                                    type: "revealDefinition",
                                    value: ability.definition ?? "",
                                    referenceType: "SpecialAbility",
                                }),
                        }}
                        // The tag, in the card's monospace: it is what binds a proxy and what a reader greps for.
                        type={ability.type}
                        lead={
                            <>
                                {ability.iconDataUri !== null && (
                                    <img
                                        className="ability-icon"
                                        src={safeImageSource(
                                            ability.iconDataUri,
                                        )}
                                        alt=""
                                    />
                                )}
                            </>
                        }
                        name={ability.label}
                        actions={
                            <>
                                <IconButton
                                    icon="details"
                                    className={
                                        "card-info-btn" +
                                        (infoAbility === ability.type
                                            ? " active"
                                            : "")
                                    }
                                    expanded={infoAbility === ability.type}
                                    title={l10n.t("Info")}
                                    disabled={
                                        abilityFacts(ability).length === 0
                                    }
                                    disabledReason={l10n.t(
                                        "This ability declares nothing else",
                                    )}
                                    onClick={() =>
                                        setInfoAbility((open) =>
                                            open === ability.type
                                                ? null
                                                : ability.type,
                                        )
                                    }
                                />
                            </>
                        }
                    >
                        {/* What the game tells the PLAYER it does. The one
                                                    line here not written for a modder, and now the
                                                    only one on the card's face. */}
                        {ability.description !== null && (
                            <span className="detail wraps ability-description">
                                {ability.description}
                            </span>
                        )}
                    </PartCard>
                ))}
            </ul>
        </StageFlyout>
    );
}
