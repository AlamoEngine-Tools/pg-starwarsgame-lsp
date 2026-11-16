// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A webview's request to its panel's project's server, answered through the panel (#208).
//
// Two pages had built this by hand: the Mod Browser's waited for ever on a reply that never came, and
// the story graph's guarded its one method with a timeout of its own. One requester now does it for
// every page: an id per request, the response matched on it, and null when the host answers nothing
// or does not answer in time - so a dropped reply is an empty answer, never a page that hangs.

import type { HostRequest, HostResponse } from "../../protocol/panels/common";
import type { Transport } from "./vscodeApi";

/** Long enough for a large page of a list; a reply that takes longer is treated as lost. */
export const REQUEST_TIMEOUT_MS = 30_000;

/** How the requester waits. The timers are injectable so a test can fire them by hand. */
export interface RequesterOptions {
    timeoutMs?: number;
    setTimer?(fn: () => void, ms: number): unknown;
    clearTimer?(handle: unknown): void;
}

/** One request; null when the server answered nothing, failed, or did not answer in time. */
export type Requester = <T>(
    method: string,
    params?: unknown,
    timeoutMs?: number,
) => Promise<T | null>;

function isResponse(message: unknown): message is HostResponse {
    return (
        typeof message === "object" &&
        message !== null &&
        (message as { type?: unknown }).type === "response" &&
        typeof (message as { id?: unknown }).id === "number"
    );
}

/** A requester over one transport, with its own id sequence. */
export function createRequester(
    transport: Transport,
    options: RequesterOptions = {},
): Requester {
    const setTimer =
        options.setTimer ??
        ((fn: () => void, ms: number): unknown => setTimeout(fn, ms));
    const clearTimer =
        options.clearTimer ??
        ((handle: unknown): void =>
            clearTimeout(handle as ReturnType<typeof setTimeout>));
    const waiting = new Map<number, (result: unknown) => void>();
    let next = 1;

    transport.listen((message) => {
        if (!isResponse(message)) {
            return;
        }
        // Only an answer to a request this requester made has someone waiting for it.
        const answer = waiting.get(message.id);
        if (answer !== undefined) {
            waiting.delete(message.id);
            answer(message.result ?? null);
        }
    });

    return <T>(
        method: string,
        params?: unknown,
        timeoutMs = options.timeoutMs ?? REQUEST_TIMEOUT_MS,
    ): Promise<T | null> =>
        new Promise((resolve) => {
            const id = next++;
            const timer = setTimer(() => {
                if (waiting.delete(id)) {
                    resolve(null);
                }
            }, timeoutMs);
            waiting.set(id, (result) => {
                clearTimer(timer);
                resolve(result as T | null);
            });
            const request: HostRequest = {
                type: "request",
                id,
                method,
                params,
            };
            transport.post(request);
        });
}
