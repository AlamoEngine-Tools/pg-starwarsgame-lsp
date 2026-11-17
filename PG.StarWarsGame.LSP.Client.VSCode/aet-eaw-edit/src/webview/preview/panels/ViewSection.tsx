// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Camera > View: how far the camera draws.

import type { Dispatch, SetStateAction } from "react";
import * as l10n from "@vscode/l10n";
import { DockSection } from "../../shared/DockSection";
import { Field } from "../../shared/Field";
import { ModeSelector } from "../../shared/ModeSelector";

export interface ViewSectionProps {
    drawDistance: number;
    folded: ReadonlySet<string>;
    setDrawDistance: Dispatch<SetStateAction<number>>;
    toggleSection: (id: string) => void;
}

export function ViewSection({
    drawDistance,
    folded,
    setDrawDistance,
    toggleSection,
}: ViewSectionProps): React.JSX.Element {
    return (
        <DockSection
            id="camera.view"
            title={l10n.t("View")}
            collapsed={folded.has("camera.view")}
            onToggle={toggleSection}
        >
            <Field
                label={l10n.t("Draw distance")}
                info={
                    <>
                        {l10n.t(
                            "The fit already reaches the end of the effects, not just the hull. A long trail needs more reach; a huge model may z-fight then.",
                        )}
                    </>
                }
            >
                <ModeSelector
                    label={l10n.t("Draw distance")}
                    value={String(drawDistance)}
                    options={[
                        {
                            id: "1",
                            label: l10n.t("Fit"),
                            title: l10n.t("Fit the model and its effects"),
                        },
                        {
                            id: "2",
                            label: l10n.t("2x"),
                            title: l10n.t("Twice the fitted distance"),
                        },
                        {
                            id: "4",
                            label: l10n.t("4x"),
                            title: l10n.t("Four times"),
                        },
                        {
                            id: "8",
                            label: l10n.t("8x"),
                            title: l10n.t("Eight times"),
                        },
                    ]}
                    onSelect={(value) => setDrawDistance(Number(value))}
                />
            </Field>
        </DockSection>
    );
}
