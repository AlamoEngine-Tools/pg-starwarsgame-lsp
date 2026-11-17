// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import type { ModelPreviewToWebview } from "../../protocol/panels/modelPreview";
import { dispatchHostMessage } from "./hostMessages";

const inspectorClosed = { type: "inspectorClosed" } as ModelPreviewToWebview;
const features = {
    type: "previewFeatures",
    features: { energyPool: true },
} as ModelPreviewToWebview;

describe("dispatchHostMessage", () => {
    it("hands a message to the handler named by its type, and to no other", async () => {
        const called: string[] = [];
        await dispatchHostMessage(features, {
            inspectorClosed: () => {
                called.push("inspectorClosed");
            },
            previewFeatures: (message) => {
                called.push(
                    `previewFeatures:${JSON.stringify(message.features)}`,
                );
            },
        });

        assert.deepEqual(called, ['previewFeatures:{"energyPool":true}']);
    });

    it("ignores a message no handler is named for", async () => {
        await assert.doesNotReject(dispatchHostMessage(inspectorClosed, {}));
    });

    /** The geometry handlers await the viewport; the caller has to be able to wait for them too. */
    it("resolves only once an async handler has finished", async () => {
        let finished = false;
        await dispatchHostMessage(inspectorClosed, {
            inspectorClosed: async () => {
                await new Promise((resolve) => setTimeout(resolve, 5));
                finished = true;
            },
        });

        assert.equal(finished, true);
    });
});
