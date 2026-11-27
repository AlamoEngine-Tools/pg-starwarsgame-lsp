// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What an ability declares, beside its row in the Abilities flyout.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { Abilities } from "../useAbilities";
import { Fragment } from "react";
import { DetailsFlyout } from "../../shared/DetailsFlyout";
import type { AnchorPlacement } from "../../shared/flyoutAnchor";
import { abilityFacts } from "../abilityRows";

export interface AbilityInfoFlyoutProps {
    abilityInfoAt: AnchorPlacement | null;
    abilityInfoRef: RefObject<HTMLDivElement | null>;
    abilityState: Abilities;
    infoAbility: string;
    setInfoAbility: Dispatch<SetStateAction<string | null>>;
}

export function AbilityInfoFlyout({
    abilityInfoAt,
    abilityInfoRef,
    abilityState,
    infoAbility,
    setInfoAbility,
}: AbilityInfoFlyoutProps): React.JSX.Element {
    return (
        <DetailsFlyout
            title={
                abilityState.abilities.find((a) => a.type === infoAbility)
                    ?.label ?? infoAbility
            }
            at={abilityInfoAt}
            ref={abilityInfoRef}
            onClose={() => setInfoAbility(null)}
        >
            <dl className="card-info">
                {abilityFacts(
                    abilityState.abilities.find((a) => a.type === infoAbility)!,
                ).map(([label, value]) => (
                    <Fragment key={label}>
                        <dt>{label}</dt>
                        <dd>{value}</dd>
                    </Fragment>
                ))}
            </dl>
        </DetailsFlyout>
    );
}
