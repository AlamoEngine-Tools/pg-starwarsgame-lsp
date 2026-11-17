// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Scene > Weather: the wind a weather emitter drifts in, and how hard it falls.

import type { Dispatch, SetStateAction } from "react";
import * as l10n from "@vscode/l10n";
import { DockSection } from "../../shared/DockSection";
import { Field } from "../../shared/Field";
import { type Wind } from "../../../shared/viewerSettings";

export interface WeatherSectionProps {
    folded: ReadonlySet<string>;
    hasWeather: boolean;
    setWeatherIntensity: Dispatch<SetStateAction<number>>;
    setWind: Dispatch<SetStateAction<Wind>>;
    toggleSection: (id: string) => void;
    weatherIntensity: number;
    wind: Wind;
}

export function WeatherSection({
    folded,
    hasWeather,
    setWeatherIntensity,
    setWind,
    toggleSection,
    weatherIntensity,
    wind,
}: WeatherSectionProps): React.JSX.Element {
    return (
        <DockSection
            id="scene.weather"
            title={l10n.t("Weather")}
            collapsed={folded.has("scene.weather")}
            onToggle={toggleSection}
        >
            <Field
                label={l10n.t("Wind direction")}
                value={<>{l10n.t("{0} deg", wind.heading)}</>}
                info={<>{l10n.t("Heading the wind blows towards")}</>}
            >
                <input
                    type="range"
                    min={0}
                    max={360}
                    step={1}
                    value={wind.heading}
                    onChange={(e) =>
                        setWind((current) => ({
                            ...current,
                            heading: Number(e.target.value),
                        }))
                    }
                />
            </Field>

            <Field
                label={l10n.t("Wind speed")}
                value={wind.speed.toFixed(1)}
                info={
                    <>
                        {l10n.t(
                            "Bends foliage and carries the emitters and weather declared affected by wind",
                        )}
                    </>
                }
            >
                <input
                    type="range"
                    min={0}
                    max={10}
                    step={0.1}
                    value={wind.speed}
                    onChange={(e) =>
                        setWind((current) => ({
                            ...current,
                            speed: Number(e.target.value),
                        }))
                    }
                />
            </Field>

            <Field
                label={l10n.t("Weather intensity")}
                value={
                    hasWeather
                        ? `${Math.round(weatherIntensity * 100)}%`
                        : "no weather"
                }
                info={
                    hasWeather ? (
                        <>
                            {l10n.t(
                                "Share of a weather field drawn, as a weather phase sets it",
                            )}
                        </>
                    ) : (
                        <>{l10n.t("No weather emitter in this scene")}</>
                    )
                }
            >
                <input
                    type="range"
                    min={0}
                    max={1}
                    step={0.05}
                    disabled={!hasWeather}
                    value={weatherIntensity}
                    onChange={(e) =>
                        setWeatherIntensity(Number(e.target.value))
                    }
                />
            </Field>
        </DockSection>
    );
}
