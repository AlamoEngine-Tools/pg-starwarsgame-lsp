// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Scene > Light: the sun, the fill and the ambient, one directional at a time, the shadow tint and
// Game rendering's stand-in shadow.

import type { Dispatch, SetStateAction } from "react";
import * as l10n from "@vscode/l10n";
import { IconButton } from "../../shared/Button";
import { DockSection } from "../../shared/DockSection";
import { Field } from "../../shared/Field";
import { ViewRow } from "../../shared/ViewRow";
import { SHADOW_TINT_TITLE, STAND_IN_BLOCKED } from "../previewLabels";
import { ModeSelector } from "../../shared/ModeSelector";
import {
    colourFromHex,
    type DirectionalName,
    type DirectionalSetting,
    hexFromColour,
    LIGHT_LABELS,
    LIGHT_NAMES,
    type LightRig,
} from "../../../shared/viewerSettings";
import { type ShadowTintReach, type StandInBlock } from "../shadowVolumePass";
import { type LightBearing } from "../lightBearing";

/** Game rendering's stand-in shadow: whether it is asked for, and why it cannot be, if it cannot. */
export interface StandInShadow {
    on: boolean;
    set: Dispatch<SetStateAction<boolean>>;
    blocked: StandInBlock;
}

export interface LightSectionProps {
    bearing: LightBearing;
    editing: DirectionalName;
    folded: ReadonlySet<string>;
    lights: LightRig;
    setEditing: Dispatch<SetStateAction<DirectionalName>>;
    setLight: (
        name: DirectionalName,
        change: Partial<DirectionalSetting>,
    ) => void;
    setLights: Dispatch<SetStateAction<LightRig>>;
    shadowReach: ShadowTintReach;
    standIn: StandInShadow;
    toggleSection: (id: string) => void;
}

export function LightSection({
    bearing,
    editing,
    folded,
    lights,
    setEditing,
    setLight,
    setLights,
    shadowReach,
    standIn,
    toggleSection,
}: LightSectionProps): React.JSX.Element {
    return (
        <DockSection
            id="scene.light"
            title={l10n.t("Light")}
            collapsed={folded.has("scene.light")}
            onToggle={toggleSection}
        >
            <Field
                label={l10n.t("Light")}
                info={
                    <>
                        {l10n.t(
                            "AloViewer's default rig: A sun that casts the shadow and two fills that do not. The translated shaders take the sun's direction and highlight, and all three as diffuse light.",
                        )}
                    </>
                }
            >
                <ModeSelector
                    label={l10n.t("Light being edited")}
                    value={editing}
                    options={LIGHT_NAMES.map((name) => ({
                        id: name,
                        label: LIGHT_LABELS[name],
                        title: l10n.t(
                            "Edit the {0}",
                            LIGHT_LABELS[name].toLowerCase(),
                        ),
                    }))}
                    onSelect={setEditing}
                />
            </Field>

            <Field
                label={l10n.t("Light around")}
                value={
                    <>
                        {l10n.t(
                            "{0}{1}deg, {2}",
                            lights[editing].azimuth,
                            " ",
                            bearing.from,
                        )}
                    </>
                }
                info={
                    <>
                        {l10n.t(
                            "Turns round the model the way the stage does, from the same zero: At 0 the light stands at the Front view's camera, and it moves clockwise seen from above.",
                        )}
                    </>
                }
            >
                <input
                    type="range"
                    min={0}
                    max={360}
                    step={5}
                    value={lights[editing].azimuth}
                    onChange={(e) =>
                        setLight(editing, {
                            azimuth: Number(e.target.value),
                        })
                    }
                />
            </Field>

            <Field
                label={l10n.t("Light height")}
                infoSeverity={bearing.belowGround ? "warning" : "info"}
                info={
                    <>
                        {bearing.belowGround
                            ? editing === "sun"
                                ? l10n.t(
                                      "Under the ground plane. The floor is between the sun and the model, so it lights the hull from beneath and casts nothing onto the ground.",
                                  )
                                : l10n.t(
                                      "Under the ground plane, which is where the engine puts both of its own fills. They cast no shadow, so nothing is hidden by it.",
                                  )
                            : l10n.t(
                                  "The sun drives the shadow. Straight overhead flattens the hull and hides the shadow under it; raking is what makes panel lines read.",
                              )}
                    </>
                }
                value={
                    <>
                        {l10n.t(
                            "{0}{1}deg, {2}",
                            lights[editing].elevation,
                            " ",
                            bearing.height,
                        )}
                    </>
                }
            >
                <input
                    type="range"
                    min={-90}
                    max={90}
                    step={5}
                    value={lights[editing].elevation}
                    onChange={(e) =>
                        setLight(editing, {
                            elevation: Number(e.target.value),
                        })
                    }
                />
            </Field>

            <Field
                label={l10n.t("Light colour")}
                value={
                    <>{l10n.t("{0}x", lights[editing].intensity.toFixed(2))}</>
                }
            >
                <ViewRow>
                    <input
                        type="color"
                        value={hexFromColour(lights[editing].colour)}
                        onChange={(e) =>
                            setLight(editing, {
                                colour: colourFromHex(e.target.value),
                            })
                        }
                    />
                    <input
                        type="range"
                        min={0}
                        max={4}
                        step={0.05}
                        value={lights[editing].intensity}
                        onChange={(e) =>
                            setLight(editing, {
                                intensity: Number(e.target.value),
                            })
                        }
                    />
                </ViewRow>
            </Field>

            <Field
                label={l10n.t("Ambient")}
                value={
                    <>{l10n.t("{0}x", lights.ambient.intensity.toFixed(2))}</>
                }
                info={
                    <>
                        {l10n.t(
                            "What reaches the parts no light does. Too much and the model reads flat; none at all and its shadowed side is black.",
                        )}
                    </>
                }
            >
                <ViewRow>
                    <input
                        type="color"
                        value={hexFromColour(lights.ambient.colour)}
                        onChange={(e) =>
                            setLights((current) => ({
                                ...current,
                                ambient: {
                                    ...current.ambient,
                                    colour: colourFromHex(e.target.value),
                                },
                            }))
                        }
                    />
                    <input
                        type="range"
                        min={0}
                        max={2}
                        step={0.05}
                        value={lights.ambient.intensity}
                        onChange={(e) =>
                            setLights((current) => ({
                                ...current,
                                ambient: {
                                    ...current.ambient,
                                    intensity: Number(e.target.value),
                                },
                            }))
                        }
                    />
                </ViewRow>
            </Field>

            {/* The swatch rides IN the label, not under it.

            A colour input carries `margin-left: auto` so it sits at the far edge of its row - which
            is what the swatch beside a slider wants. Under a `.field`, though, the row is a COLUMN,
            and an auto margin there aligns on the cross axis instead: the swatch landed on a line of
            its own, hard right, with a gap above it and nothing beside it. The two fields with no
            slider were the only ones shaped like that.

            Beside the label in a row, NOT inside it: the label carries opacity 0.7, and opacity
            composites its whole subtree as a group, so a swatch in there renders a colour that is
            not the one stored - measured, a stored #ffffff drew as #bdbdbd. A sibling keeps its
            own colour and still reaches the far edge, because the well carries the auto margin. */}
            <Field
                label={l10n.t("Shadow colour")}
                inline
                info={
                    <>
                        {l10n.t(
                            "Alamo's one shadow colour, and it MULTIPLIES: 0.5 grey means half as bright. In Game mode it tints the stencil volume, so it reaches the hull's own self-shadowing as well as the ground. In Default mode only the ground can catch it.",
                        )}
                    </>
                }
            >
                <input
                    type="color"
                    value={hexFromColour(lights.shadow)}
                    /* Dead ONLY when nothing can show it. It was gated on the floor alone, which
                                                   disabled a working control: in Game mode the stencil darken tints the hull's
                                                   own self-shadowing with no ground under it at all. */
                    disabled={
                        shadowReach === "none" ||
                        shadowReach === "uncast" ||
                        shadowReach === "off"
                    }
                    title={SHADOW_TINT_TITLE[shadowReach]}
                    onChange={(e) =>
                        setLights((current) => ({
                            ...current,
                            shadow: colourFromHex(e.target.value),
                        }))
                    }
                />
            </Field>

            {/* Beside the shadow colour, because it decides whether there is a shadow to tint. A
            toggle, not a mode: it puts back a shadow the game does not draw, so it stays off until
            asked for, and the header marks it while it is drawn. Disabled with the reason wherever
            there is nothing for it to stand in for. */}
            <Field
                label={l10n.t("Stand-in shadow")}
                inline
                info={
                    <>
                        {l10n.t(
                            "The game draws no shadow for a model without a shadow mesh, and neither does the Game renderer. The stand-in casts the Default renderer's shadow-map shadow from the render geometry, for an unfinished model. Off in every new preview.",
                        )}
                    </>
                }
            >
                <IconButton
                    icon="standInShadow"
                    className={standIn.on ? "active" : undefined}
                    pressed={standIn.on}
                    title={
                        standIn.on
                            ? l10n.t("Stop casting the stand-in shadow")
                            : l10n.t(
                                  "Cast a stand-in shadow from the render geometry",
                              )
                    }
                    disabled={standIn.blocked !== null}
                    disabledReason={
                        standIn.blocked === null
                            ? ""
                            : STAND_IN_BLOCKED[standIn.blocked]
                    }
                    onClick={() => standIn.set((on) => !on)}
                />
            </Field>

            <Field
                label={l10n.t("Highlight colour")}
                inline
                info={
                    <>
                        {l10n.t(
                            "One global specular for all three lights, as the engine keeps it. Only the translated effect shaders read it.",
                        )}
                    </>
                }
            >
                <input
                    type="color"
                    value={hexFromColour(lights.specular)}
                    onChange={(e) =>
                        setLights((current) => ({
                            ...current,
                            specular: colourFromHex(e.target.value),
                        }))
                    }
                />
            </Field>
        </DockSection>
    );
}
