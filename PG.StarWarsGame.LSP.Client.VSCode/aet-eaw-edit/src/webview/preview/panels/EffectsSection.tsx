// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Scene > Effects: how the picture is drawn - heat, bloom and the game's own shaders.

import type { Dispatch, SetStateAction } from "react";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { Button } from "../../shared/Button";
import { DockSection } from "../../shared/DockSection";
import { Field } from "../../shared/Field";
import { ViewRow } from "../../shared/ViewRow";
import { post } from "../previewPost";
import { Icon } from "../../shared/Icon";
import { ModeSelector } from "../../shared/ModeSelector";

export interface EffectsSectionProps {
    anyShaderSource: boolean;
    bloom: boolean;
    folded: ReadonlySet<string>;
    heatDebug: boolean;
    heatOn: boolean;
    scene: PreviewScene | null;
    setBloom: Dispatch<SetStateAction<boolean>>;
    setHeatDebug: Dispatch<SetStateAction<boolean>>;
    setHeatOn: Dispatch<SetStateAction<boolean>>;
    setTranslatedOn: Dispatch<SetStateAction<boolean>>;
    toggleSection: (id: string) => void;
    translatedCount: { translated: number; total: number };
    translatedOn: boolean;
}

export function EffectsSection({
    anyShaderSource,
    bloom,
    folded,
    heatDebug,
    heatOn,
    scene,
    setBloom,
    setHeatDebug,
    setHeatOn,
    setTranslatedOn,
    toggleSection,
    translatedCount,
    translatedOn,
}: EffectsSectionProps): React.JSX.Element {
    return (
        <DockSection
            id="scene.effects"
            title={l10n.t("Effects")}
            collapsed={folded.has("scene.effects")}
            onToggle={toggleSection}
        >
            {/* Which renderer draws the model - one or the other, never both, so a segmented
            control rather than a tick box.

            First in the section because it decides how every SURFACE is drawn, and the two below
            it are passes over the finished frame. All three are Tier 1: they describe the picture,
            not the model, so they follow the reader from one file to the next.

            On a particle file as well: its emitters are drawn through the engine's own Prim effects
            under Game and by the preview's sprite shader under Default, so the switch decides how
            they are drawn too. It was withheld there from before the particle effects existed. */}
            {scene !== null && (
                <Field
                    label={l10n.t("Renderer")}
                    info={
                        <>
                            {l10n.t(
                                "Default reads each sub-mesh's shader NAME and draws the archetype it names - never wrong about geometry, only about how a surface is lit. Game draws the model the way the engine does: Its own effect shaders, translated, and stencil shadow volumes cast by its authored shadow mesh. Only the shaders need the .fx sources; the shadows work without them. An emitter follows the same switch: the engine's effect for its blend mode under Game, the preview's own sprite shader under Default.",
                            )}
                        </>
                    }
                >
                    <ModeSelector
                        label={l10n.t("Which renderer draws the model")}
                        /* What is actually RUNNING, not what could be fully delivered. The stencil
                                                       shadow pass follows this switch alone and needs no .fx sources, so a reader
                                                       with none was shown Default while the engine's own shadows were being cast -
                                                       and told by the problems list, in the same breath, that they were in Game
                                                       mode with nothing to translate. */
                        value={translatedOn ? "game" : "default"}
                        options={[
                            {
                                id: "default",
                                label: l10n.t("Default"),
                                title: l10n.t(
                                    "Every sub-mesh drawn with the archetype reading of its shader name. Never wrong about geometry - only about how a surface is lit.",
                                ),
                            },
                            {
                                id: "game",
                                label: l10n.t("Game"),
                                // Selectable with no sources, because the mode still
                                // DOES something without them: the stencil shadows
                                // follow this switch and need no .fx at all. It was
                                // disabled here, which - once the value above began
                                // reporting the real mode - would have left a reader
                                // who stepped to Default unable to return to the mode
                                // the panel opens in.
                                title: anyShaderSource
                                    ? l10n.t(
                                          "The model's own effect shaders, translated, and the engine's stencil shadows. {0} of {1} sub-meshes and emitters are drawn this way.",
                                          translatedCount.translated,
                                          translatedCount.total,
                                      )
                                    : l10n.t(
                                          "The engine's stencil shadows, which need no sources. For its effect shaders as well, set the .fx sources up.",
                                      ),
                            },
                        ]}
                        onSelect={(id) => setTranslatedOn(id === "game")}
                    />

                    {/* Always here, in one of two states - never absent.

                    The setup flow is a command, and a command nobody can find is not a path out of
                    "nothing translated"; but a button that appears only while something is wrong is
                    a control the reader never sees working. So the row always says where the
                    sources stand, and only the wording changes. */}
                    {anyShaderSource ? (
                        <span className="shader-state">
                            <Icon name="check" />
                            {l10n.t("Shader sources ready")}
                        </span>
                    ) : (
                        <Button
                            title={l10n.t("Fetch the .fx shader sources")}
                            onClick={() =>
                                post({
                                    type: "obtainShaders",
                                })
                            }
                        >
                            <Icon name="download" />
                            {l10n.t("Set up shader sources")}
                        </Button>
                    )}
                </Field>
            )}

            <Field
                label={l10n.t("Heat distortion")}
                info={
                    <>
                        {l10n.t(
                            "Debug draws the heat BUFFER instead of the bent picture - the only way to see which pixels a distortion covers, since its whole effect is a displacement. Red and green carry the direction, blue the strength, and ALL BLACK means nothing on this model is distorting anything. It keeps the buffers alive while it is on.",
                        )}
                    </>
                }
            >
                <ViewRow>
                    <label className="field-label">
                        <input
                            type="checkbox"
                            checked={heatOn}
                            onChange={(e) => setHeatOn(e.target.checked)}
                        />
                        {l10n.t("Bend the frame")}
                    </label>
                    <label className="field-label">
                        <input
                            type="checkbox"
                            checked={heatDebug}
                            disabled={!heatOn}
                            onChange={(e) => setHeatDebug(e.target.checked)}
                        />
                        {l10n.t("Debug")}
                    </label>
                </ViewRow>
            </Field>

            <Field
                // The checkbox and its word are one <label>, so pressing the word toggles it -
                // and the mark stays OUTSIDE that label, or pressing the mark would toggle it too.
                label={
                    <label className="field-label">
                        <input
                            type="checkbox"
                            checked={bloom}
                            onChange={(e) => setBloom(e.target.checked)}
                        />
                        {l10n.t("Bloom")}
                    </label>
                }
                info={
                    <>
                        {l10n.t(
                            "Off by default, as it is in the game. It runs after the heat pass, so the two compose in that order rather than fighting over the frame.",
                        )}
                    </>
                }
            />
        </DockSection>
    );
}
