// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Weapon flyout: the attacker the reader builds to shoot the subject with.

import type { CombatLens } from "../useCombatLens";
import type { Flyouts } from "../useFlyouts";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { StageFlyout } from "../../shared/Stage";
import { AttackerSection } from "./AttackerSection";
import { type PreviewMode } from "../previewMode";

export interface WeaponFlyoutProps {
    combat: CombatLens;
    flyouts: Flyouts;
    mode: PreviewMode;
    scene: PreviewScene | null;
}

export function WeaponFlyout({
    combat,
    flyouts,
    mode,
    scene,
}: WeaponFlyoutProps): React.JSX.Element {
    return (
        <StageFlyout
            title={l10n.t("Weapon")}
            at="right"
            fromBottom
            ref={flyouts.attackRef}
            onClose={() => flyouts.setAttackOpen(false)}
        >
            {mode === "gameplay" &&
                scene?.defence !== null &&
                scene?.defence !== undefined && (
                    <AttackerSection
                        combat={combat}
                        defence={scene.defence}
                        scene={scene}
                    />
                )}
        </StageFlyout>
    );
}
