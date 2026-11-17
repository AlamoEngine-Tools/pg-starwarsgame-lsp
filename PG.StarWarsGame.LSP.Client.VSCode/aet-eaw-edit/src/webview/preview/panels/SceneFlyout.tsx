// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Scene flyout: the room, the lights, the weather and the effects the picture is drawn with.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { Flyouts } from "../useFlyouts";
import type { RoomSettings } from "../useRoomSettings";
import type { CameraBindings } from "../useCameraBindings";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { IconButton } from "../../shared/Button";
import { StageFlyout } from "../../shared/Stage";
import { EffectsSection } from "./EffectsSection";
import { LightSection } from "./LightSection";
import { RoomSection } from "./RoomSection";
import { WeatherSection } from "./WeatherSection";
import {
    type DirectionalName,
    type DirectionalSetting,
} from "../../../shared/viewerSettings";
import { type ShadowTintReach } from "../shadowVolumePass";

export interface SceneFlyoutProps {
    anyShaderSource: boolean;
    cameraState: CameraBindings;
    editing: DirectionalName;
    flyouts: Flyouts;
    ground: { limit: number; step: number };
    groundDragging: RefObject<boolean>;
    hasWeather: boolean;
    room: RoomSettings;
    scene: PreviewScene | null;
    setEditing: Dispatch<SetStateAction<DirectionalName>>;
    setLight: (
        name: DirectionalName,
        change: Partial<DirectionalSetting>,
    ) => void;
    shadowReach: ShadowTintReach;
    toggleSection: (id: string) => void;
    translatedCount: { translated: number; total: number };
}

export function SceneFlyout({
    anyShaderSource,
    cameraState,
    editing,
    flyouts,
    ground,
    groundDragging,
    hasWeather,
    room,
    scene,
    setEditing,
    setLight,
    shadowReach,
    toggleSection,
    translatedCount,
}: SceneFlyoutProps): React.JSX.Element {
    return (
        <StageFlyout
            title={l10n.t("Scene")}
            at="left"
            ref={flyouts.worldRef}
            tools={
                <IconButton
                    icon="reset"
                    title={l10n.t(
                        "Put the backdrop, lights, wind and draw distance back to their defaults. The model's own state is left as it is.",
                    )}
                    onClick={room.resetRoom}
                />
            }
            onClose={() => flyouts.setWorldOpen(false)}
        >
            <RoomSection
                background={room.background}
                floor={room.floor}
                floorLevel={room.floorLevel}
                folded={room.folded}
                ground={ground}
                groundDragging={groundDragging}
                setBackground={room.setBackground}
                setFloorLevel={room.setFloorLevel}
                toggleSection={toggleSection}
            />

            <LightSection
                bearing={cameraState.bearing}
                editing={editing}
                folded={room.folded}
                lights={room.lights}
                setEditing={setEditing}
                setLight={setLight}
                setLights={room.setLights}
                shadowReach={shadowReach}
                toggleSection={toggleSection}
            />

            <WeatherSection
                folded={room.folded}
                hasWeather={hasWeather}
                setWeatherIntensity={room.setWeatherIntensity}
                setWind={room.setWind}
                toggleSection={toggleSection}
                weatherIntensity={room.weatherIntensity}
                wind={room.wind}
            />

            <EffectsSection
                anyShaderSource={anyShaderSource}
                bloom={room.bloom}
                folded={room.folded}
                heatDebug={room.heatDebug}
                heatOn={room.heatOn}
                scene={scene}
                setBloom={room.setBloom}
                setHeatDebug={room.setHeatDebug}
                setHeatOn={room.setHeatOn}
                setTranslatedOn={room.setTranslatedOn}
                toggleSection={toggleSection}
                translatedCount={translatedCount}
                translatedOn={room.translatedOn}
            />
        </StageFlyout>
    );
}
