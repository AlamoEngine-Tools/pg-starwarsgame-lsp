// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model preview's one way to the host, typed by its panel union. A module of its own so every
// panel posts through the same function rather than each taking it as a prop.
//
// It is also where a model request picks up the version already held (#436): every part, wreck and
// death clone asks through here, so none of the callers has to know the store exists. The request
// leaves numbered, and the host echoes the number on the reply: see modelStore.ts. A model that a
// part of the scene has already asked for is not asked for again (#442): that reply answers every
// part waiting on it.

import type { ModelDetail } from "../../protocol/modelPreview";
import type {
    ModelPreviewRequestGlb,
    ModelPreviewToHost,
} from "../../protocol/panels/modelPreview";
import { poster } from "../shared/vscodeApi";
import type { InspectorSubject } from "./inspectorSubject";
import { ModelStore } from "./modelStore";
import { type ParsedModel, readModel } from "./parsedModel";

const send = poster<ModelPreviewToHost<InspectorSubject>>();

/**
 * The models this preview holds. One per webview, which is one preview of one subject for its
 * whole life, so a module-level store is the panel's store. Each is held as the reading of its
 * bytes, so a part asking while the model is still being read waits for the same reading.
 */
export const heldModels = new ModelStore<
    ModelDetail,
    Promise<ParsedModel>,
    ModelPreviewRequestGlb
>(readModel);

export function post(message: ModelPreviewToHost<InspectorSubject>): void {
    if (message.type === "requestGlb") {
        const have = heldModels.asking(message);
        if (heldModels.rides(message.partId)) {
            return;
        }

        send({
            ...message,
            have,
            request: heldModels.requestOf(message.partId),
        });
        return;
    }

    send(message);
}
