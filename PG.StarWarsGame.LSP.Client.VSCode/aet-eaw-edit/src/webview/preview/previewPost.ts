// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model preview's one way to the host, typed by its panel union. A module of its own so every
// panel posts through the same function rather than each taking it as a prop.
//
// It is also where a model request picks up the version already held (#436): every part, wreck and
// death clone asks through here, so none of the callers has to know the store exists.

import type { ModelDetail } from "../../protocol/modelPreview";
import type { ModelPreviewToHost } from "../../protocol/panels/modelPreview";
import { poster } from "../shared/vscodeApi";
import type { InspectorSubject } from "./inspectorSubject";
import { ModelStore } from "./modelStore";

const send = poster<ModelPreviewToHost<InspectorSubject>>();

/**
 * The models this preview holds. One per webview, which is one preview of one subject for its
 * whole life, so a module-level store is the panel's store.
 */
export const heldModels = new ModelStore<ModelDetail>();

export function post(message: ModelPreviewToHost<InspectorSubject>): void {
    if (message.type === "requestGlb") {
        send({ ...message, have: heldModels.asking(message) });
        return;
    }

    send(message);
}
