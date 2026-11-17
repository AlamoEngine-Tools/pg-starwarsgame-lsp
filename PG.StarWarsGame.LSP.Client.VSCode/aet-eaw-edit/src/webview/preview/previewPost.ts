// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The model preview's one way to the host, typed by its panel union. A module of its own so every
// panel posts through the same function rather than each taking it as a prop.

import type { ModelPreviewToHost } from "../../protocol/panels/modelPreview";
import { poster } from "../shared/vscodeApi";
import type { InspectorSubject } from "./inspectorSubject";

export const post = poster<ModelPreviewToHost<InspectorSubject>>();
