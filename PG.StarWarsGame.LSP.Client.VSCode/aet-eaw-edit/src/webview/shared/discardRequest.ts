// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A page asking to drop its staged changes (#222).
//
// Discard throws work away, so the reader is asked first - by the host, which is the only side that
// can raise a dialog over the whole window and the only one that knows whether the reader has said
// not to be asked again. The page posts the question and acts on the one answer that carries its
// own id; an answer to another question, or to nobody's, changes nothing.

import type {
    ConfirmDiscard,
    DiscardAnswer,
} from "../../protocol/panels/common";
import type { Transport } from "./vscodeApi";

let next = 1;

/**
 * Asks the host whether `count` staged changes may be dropped.
 *
 * Resolves true only on the reader's yes. It never resolves false by itself: a dialog left open is
 * a question still being read, and the page keeps what is staged for as long as it is.
 */
export function requestDiscard(
    transport: Transport,
    count: number,
): Promise<boolean> {
    const id = next++;
    return new Promise((resolve) => {
        const stop = transport.listen((message) => {
            const answer = message as Partial<DiscardAnswer> | null;
            if (answer?.type === "discardAnswer" && answer.id === id) {
                stop();
                resolve(answer.confirmed === true);
            }
        });
        transport.post({
            type: "confirmDiscard",
            id,
            count,
        } satisfies ConfirmDiscard);
    });
}
