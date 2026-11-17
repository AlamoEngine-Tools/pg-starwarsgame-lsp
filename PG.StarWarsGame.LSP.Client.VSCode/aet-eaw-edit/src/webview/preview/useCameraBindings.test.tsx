// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import "../shared/vscodeApiStandIn";

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { renderToStaticMarkup } from "react-dom/server";

import { DEFAULT_LIGHTS } from "../../shared/viewerSettings";
import { type CameraBindings, useCameraBindings } from "./useCameraBindings";

/** What the camera holds on the first render, through a component that only calls it. */
function firstRender(): CameraBindings {
    let seen: CameraBindings | undefined;
    const Probe = (): null => {
        seen = useCameraBindings({
            cameraBindings: [],
            cameraPresets: [],
            editing: "sun",
            lights: DEFAULT_LIGHTS,
            scene: null,
            setCameraView: () => undefined,
        });
        return null;
    };
    renderToStaticMarkup(<Probe />);
    assert.ok(seen);
    return seen;
}

describe("useCameraBindings", () => {
    it("offers the model camera disabled, and no binding and nothing to bind, without a scene", () => {
        const camera = firstRender();

        assert.deepEqual(
            camera.authorCameras.map((c) => [c.id, c.disabled]),
            [["camera:none", true]],
        );
        assert.equal(camera.boundRule ?? null, null);
        assert.deepEqual(camera.bindTargets, []);
    });
});
