// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The capture settings: the size written, the custom sides as typed, and whether the background
// and the scenery go with it.

import { useState } from "react";
import { CAPTURE_SIZES, DEFAULT_CAPTURE_SIZE, MAX_SIDE } from "./capture";
import { parseInteger } from "../shared/integerValue";

export type CaptureSettings = ReturnType<typeof useCaptureSettings>;

export function useCaptureSettings() {
    /**
     * How big a capture is written - one of the game's icon sizes, or a custom one - and whether it
     * carries the room with it. The custom sides are the fields' text, so a half-typed number
     * survives until it is finished.
     */
    const [captureSizeId, setCaptureSizeId] = useState(DEFAULT_CAPTURE_SIZE.id);
    const [customCapture, setCustomCapture] = useState({
        width: "256",
        height: "256",
    });
    // The capture's pixel size, or null while a custom side is not a whole number from 1 to 4096.
    const captureDims = ((): { width: number; height: number } | null => {
        if (captureSizeId !== "custom") {
            const preset =
                CAPTURE_SIZES.find((s) => s.id === captureSizeId) ??
                DEFAULT_CAPTURE_SIZE;
            return { width: preset.width, height: preset.height };
        }
        const width = parseInteger(customCapture.width);
        const height = parseInteger(customCapture.height);
        return width !== null &&
            height !== null &&
            width >= 1 &&
            height >= 1 &&
            width <= MAX_SIDE &&
            height <= MAX_SIDE
            ? { width, height }
            : null;
    })();
    const [captureTransparent, setCaptureTransparent] = useState(true);
    const [captureScenery, setCaptureScenery] = useState(false);

    return {
        captureSizeId,
        setCaptureSizeId,
        customCapture,
        setCustomCapture,
        captureDims,
        captureTransparent,
        setCaptureTransparent,
        captureScenery,
        setCaptureScenery,
    };
}
