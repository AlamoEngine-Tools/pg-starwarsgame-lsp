// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What an ability declares, beside its row in the Abilities flyout.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { Abilities } from "../useAbilities";
import * as l10n from "@vscode/l10n";
import { Fragment } from "react";
import { IconButton } from "../../shared/Button";
import { abilityFacts } from "../abilityRows";

export interface AbilityInfoFlyoutProps {
    abilityInfoAt: { left: number; top: number } | null;
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
        <div
            className="details-flyout"
            role="dialog"
            ref={abilityInfoRef}
            style={
                abilityInfoAt === null
                    ? { visibility: "hidden" }
                    : {
                          left: abilityInfoAt.left,
                          top: abilityInfoAt.top,
                      }
            }
        >
            <div className="details-head">
                <span className="details-title">
                    {abilityState.abilities.find((a) => a.type === infoAbility)
                        ?.label ?? infoAbility}
                </span>
                <IconButton
                    icon="close"
                    title={l10n.t("Close")}
                    onClick={() => setInfoAbility(null)}
                />
            </div>
            <div className="details-body">
                <dl className="card-info">
                    {abilityFacts(
                        abilityState.abilities.find(
                            (a) => a.type === infoAbility,
                        )!,
                    ).map(([label, value]) => (
                        <Fragment key={label}>
                            <dt>{label}</dt>
                            <dd>{value}</dd>
                        </Fragment>
                    ))}
                </dl>
            </div>
        </div>
    );
}
