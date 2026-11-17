// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Camera flyout: the presets and their bindings, the draw distance and the capture.

import type { Dispatch, SetStateAction, RefObject } from "react";
import type { Flyouts } from "../useFlyouts";
import type { CameraBindings } from "../useCameraBindings";
import type { RoomSettings } from "../useRoomSettings";
import * as l10n from "@vscode/l10n";
import { type PreviewScene } from "../../../protocol/modelPreview";
import { StageFlyout } from "../../shared/Stage";
import { CameraPresetsSection } from "./CameraPresetsSection";
import { CaptureSection } from "./CaptureSection";
import { ViewSection } from "./ViewSection";
import { type PreviewViewport } from "../viewport";

export interface CameraFlyoutProps {
    cameraState: CameraBindings;
    captureDims: { width: number; height: number } | null;
    captureScenery: boolean;
    captureSizeId: string;
    captureTransparent: boolean;
    customCapture: { width: string; height: string };
    flyouts: Flyouts;
    renamingPreset: string | null;
    room: RoomSettings;
    scene: PreviewScene | null;
    sceneRef: RefObject<PreviewScene | null>;
    setCaptureScenery: Dispatch<SetStateAction<boolean>>;
    setCaptureSizeId: Dispatch<SetStateAction<string>>;
    setCaptureTransparent: Dispatch<SetStateAction<boolean>>;
    setCustomCapture: Dispatch<
        SetStateAction<{ width: string; height: string }>
    >;
    setRenamingPreset: Dispatch<SetStateAction<string | null>>;
    toggleSection: (id: string) => void;
    viewportRef: RefObject<PreviewViewport | null>;
}

export function CameraFlyout({
    cameraState,
    captureDims,
    captureScenery,
    captureSizeId,
    captureTransparent,
    customCapture,
    flyouts,
    renamingPreset,
    room,
    scene,
    sceneRef,
    setCaptureScenery,
    setCaptureSizeId,
    setCaptureTransparent,
    setCustomCapture,
    setRenamingPreset,
    toggleSection,
    viewportRef,
}: CameraFlyoutProps): React.JSX.Element {
    return (
        <StageFlyout
            title={l10n.t("Camera")}
            at="right"
            ref={flyouts.cameraRef}
            onClose={() => flyouts.setCameraOpen(false)}
        >
            <CameraPresetsSection
                bindTargets={cameraState.bindTargets}
                boundRule={cameraState.boundRule}
                cameraPresets={room.cameraPresets}
                folded={room.folded}
                renamingPreset={renamingPreset}
                sceneRef={sceneRef}
                setCameraBindings={room.setCameraBindings}
                setCameraPresets={room.setCameraPresets}
                setCameraView={room.setCameraView}
                setRenamingPreset={setRenamingPreset}
                toggleSection={toggleSection}
                viewportRef={viewportRef}
            />

            <ViewSection
                drawDistance={room.drawDistance}
                folded={room.folded}
                setDrawDistance={room.setDrawDistance}
                toggleSection={toggleSection}
            />

            <CaptureSection
                captureDims={captureDims}
                captureScenery={captureScenery}
                captureSizeId={captureSizeId}
                captureTransparent={captureTransparent}
                customCapture={customCapture}
                floor={room.floor}
                folded={room.folded}
                grid={room.grid}
                particlesOn={room.particlesOn}
                scene={scene}
                setCaptureScenery={setCaptureScenery}
                setCaptureSizeId={setCaptureSizeId}
                setCaptureTransparent={setCaptureTransparent}
                setCustomCapture={setCustomCapture}
                toggleSection={toggleSection}
                viewportRef={viewportRef}
            />
        </StageFlyout>
    );
}
