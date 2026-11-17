// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the model preview does with each message from the host, as named handlers over the panel's
// `ToWebview` union.
//
// It was one function testing `message.type` ten times in a row, 580 lines long. A handler map says
// the same thing with the type doing the narrowing: each handler is handed the one member of the
// union its key names, and a message type the union drops stops compiling at its handler.

import type { ModelPreviewToWebview } from "../../protocol/panels/modelPreview";

type MessageOf<K extends ModelPreviewToWebview["type"]> = Extract<
    ModelPreviewToWebview,
    { type: K }
>;

/** One handler per message type the panel acts on. A type with no handler is ignored. */
export type HostMessageHandlers = {
    [K in ModelPreviewToWebview["type"]]?: (
        message: MessageOf<K>,
    ) => void | Promise<void>;
};

/** Hands `message` to the handler its type names, and waits for it. */
export async function dispatchHostMessage(
    message: ModelPreviewToWebview,
    handlers: HostMessageHandlers,
): Promise<void> {
    const handler = handlers[message.type] as
        ((message: ModelPreviewToWebview) => void | Promise<void>) | undefined;

    await handler?.(message);
}
