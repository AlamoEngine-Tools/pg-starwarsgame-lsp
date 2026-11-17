// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The emitters of the particle system on stage.

import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { DockSection } from "../../shared/DockSection";

export interface EmittersSectionProps {
    emitters: string[];
    hiddenEmitters: ReadonlySet<number>;
    scene: PreviewScene | null;
    toggleEmitter: (systemId: string, index: number, visible: boolean) => void;
}

export function EmittersSection({
    emitters,
    hiddenEmitters,
    scene,
    toggleEmitter,
}: EmittersSectionProps): React.JSX.Element {
    return (
        <DockSection title={l10n.t("Emitters")} count={emitters.length}>
            <ul className="part-list">
                {emitters.map((name, index) => (
                    <li key={index}>
                        <label className="field-label">
                            <input
                                type="checkbox"
                                checked={!hiddenEmitters.has(index)}
                                onChange={(e) =>
                                    toggleEmitter(
                                        scene?.subject ?? "",
                                        index,
                                        e.target.checked,
                                    )
                                }
                            />
                            {name}
                        </label>
                    </li>
                ))}
            </ul>
        </DockSection>
    );
}
