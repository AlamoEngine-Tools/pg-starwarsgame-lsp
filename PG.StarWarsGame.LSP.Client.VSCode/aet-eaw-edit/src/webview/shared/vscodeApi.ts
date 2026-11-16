// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The one place a webview reaches VS Code (#208).
//
// `acquireVsCodeApi` may be called once per page, and every bundle used to call it itself, eight times
// over, each with its own untyped postMessage and its own `event.data` cast. Here it is called once,
// when the bundle first imports this module, and the rest of the page talks through the transport:
// typed by the panel's protocol on the way out, and on the way in through `useHostMessages`.
//
// A rig or a test that runs a bundle outside VS Code stands in for the API by defining
// `window.acquireVsCodeApi` before the bundle loads - which is the fake transport the Mod Browser rig
// has always used.

import { useEffect, useRef } from "react";

/** How a page talks to its host: what it posts, and what it hears back. */
export interface Transport {
    post(message: unknown): void;
    /** Subscribes to the host's messages; returns the unsubscribe. */
    listen(handler: (message: unknown) => void): () => void;
}

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

/** The API itself, for the few seams that take it whole (the panel layout bridge). */
export const vscodeApi = acquireVsCodeApi();

/** This page's transport. */
export const transport: Transport = {
    post: (message) => vscodeApi.postMessage(message),
    listen(handler) {
        const listener = (event: MessageEvent): void => handler(event.data);
        window.addEventListener("message", listener);
        return () => window.removeEventListener("message", listener);
    },
};

/** A post typed by what this panel's host accepts. */
export function poster<T extends { type: string }>(): (message: T) => void {
    return (message) => transport.post(message);
}

/**
 * Hands the host's messages to `handler`, typed by what this panel's host sends.
 *
 * The handler may change from render to render; the subscription does not, so a message is never
 * dropped between an unsubscribe and the next subscribe.
 */
export function useHostMessages<T extends { type: string }>(
    handler: (message: T) => void,
): void {
    const current = useRef(handler);
    current.current = handler;
    useEffect(
        () =>
            transport.listen((message) => {
                if (
                    typeof message === "object" &&
                    message !== null &&
                    typeof (message as { type?: unknown }).type === "string"
                ) {
                    current.current(message as T);
                }
            }),
        [],
    );
}
