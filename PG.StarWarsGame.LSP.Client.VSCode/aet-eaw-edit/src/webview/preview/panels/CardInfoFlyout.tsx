// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a hardpoint or a unit weapon measures, beside its card.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { CombatLens } from "../useCombatLens";
import { Fragment } from "react";
import { DetailsFlyout } from "../../shared/DetailsFlyout";
import type { AnchorPlacement } from "../../shared/flyoutAnchor";
import {
    hardpointFacts,
    unitWeaponFacts,
    weaponFacts,
} from "../hardpointCards";

export interface CardInfoFlyoutProps {
    cardInfoAt: AnchorPlacement | null;
    cardInfoRef: RefObject<HTMLDivElement | null>;
    combat: CombatLens;
    infoCard: string;
    setInfoCard: Dispatch<SetStateAction<string | null>>;
}

export function CardInfoFlyout({
    cardInfoAt,
    cardInfoRef,
    combat,
    infoCard,
    setInfoCard,
}: CardInfoFlyoutProps): React.JSX.Element {
    return (
        <DetailsFlyout
            title={infoCard}
            at={cardInfoAt}
            ref={cardInfoRef}
            onClose={() => setInfoCard(null)}
        >
            {(() => {
                const card = combat.cards.find(
                    (entry) => entry.id === infoCard,
                );

                // A unit weapon's card carries the same button, and the same
                // measurements sit behind it - it simply has no hardpoint half.
                const loose =
                    card === undefined
                        ? combat.looseWeapons.find((row) => row.id === infoCard)
                        : undefined;

                const facts =
                    card !== undefined
                        ? hardpointFacts(card)
                        : loose === undefined
                          ? []
                          : unitWeaponFacts(loose);

                const weapon = card?.weapon ?? loose ?? null;

                if (card === undefined && loose === undefined) {
                    return null;
                }

                return (
                    <dl className="card-info">
                        {facts.map((fact) => (
                            <dd key={fact}>{fact}</dd>
                        ))}
                        {weapon !== null &&
                            weaponFacts(weapon).map(([label, value]) => (
                                <Fragment key={label}>
                                    <dt>{label}</dt>
                                    <dd>{value}</dd>
                                </Fragment>
                            ))}
                    </dl>
                );
            })()}
        </DetailsFlyout>
    );
}
