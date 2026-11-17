// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What each shot did, newest first, with the arithmetic under each line.

import type { CombatLens } from "../useCombatLens";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { Button } from "../../shared/Button";
import { StageFlyout } from "../../shared/Stage";
import { ViewRow } from "../../shared/ViewRow";
import { Icon } from "../../shared/Icon";
import { poolSummary } from "../attacker";
import { damageLine } from "../damageLog";

export interface DamageLogFlyoutProps {
    combat: CombatLens;
    scene: PreviewScene | null;
}

export function DamageLogFlyout({
    combat,
    scene,
}: DamageLogFlyoutProps): React.JSX.Element {
    return (
        <StageFlyout
            title={l10n.t("Damage log")}
            at="right"
            fromBottom
            className="sizeable"
            ref={combat.logRef}
            style={{ height: combat.logHeight, width: combat.logWidth }}
            edges={
                <>
                    <div
                        className="resize-handle-n"
                        title={l10n.t("Drag to resize")}
                        {...combat.logResizeN}
                    />
                    <div
                        className="resize-handle-w"
                        title={l10n.t("Drag to resize")}
                        {...combat.logResizeW}
                    />
                </>
            }
            count={combat.damageLog.length}
            onClose={() => combat.setLogOpen(false)}
        >
            {/* Only the numbers that move. What they are and what
                                        governs them is fixed for the subject, so it rides on the
                                        tooltip rather than costing four rows of height. */}
            {poolSummary(
                scene?.defence,
                combat.hull,
                combat.hardpointHealthPool,
            ) && (
                <p
                    className="pool-summary"
                    title={
                        poolSummary(
                            scene?.defence,
                            combat.hull,
                            combat.hardpointHealthPool,
                        )!.title
                    }
                >
                    {
                        poolSummary(
                            scene?.defence,
                            combat.hull,
                            combat.hardpointHealthPool,
                        )!.text
                    }
                </p>
            )}
            <ol className="damage-log">
                {combat.damageLog.map((entry, index) => (
                    <li
                        key={`${index}:${entry.target}`}
                        className={
                            entry.destroyed
                                ? "log-kill"
                                : entry.amount > 0
                                  ? undefined
                                  : "log-nothing"
                        }
                    >
                        {damageLine(entry)}
                        {/* The arithmetic under the sentence: the factor
                                                    that scaled it, how a blast was divided, and
                                                    where each pool stood before and after. */}
                        {(entry.workings ?? []).length > 0 && (
                            <ul className="log-workings">
                                {(entry.workings ?? []).map((line, at) => (
                                    <li key={at}>{line}</li>
                                ))}
                            </ul>
                        )}
                    </li>
                ))}
            </ol>

            <ViewRow>
                <Button
                    compact
                    title={l10n.t("Empty the log")}
                    onClick={() => combat.setDamageLog([])}
                >
                    <Icon name="remove" />
                    {l10n.t("Clear")}
                </Button>
            </ViewRow>
        </StageFlyout>
    );
}
