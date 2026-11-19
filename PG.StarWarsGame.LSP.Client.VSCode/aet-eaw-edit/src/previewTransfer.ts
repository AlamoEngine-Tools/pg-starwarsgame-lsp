// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The client's half of the preview's transfer pipe (#436).
//
// A model's bytes do not ride in the JSON-RPC reply: the reply names a transfer, and the bytes are
// collected here from a pipe the server opened. This side decides nothing - which file, which cache
// entry, which clips were all settled by the request - it asks for an id and takes what comes.
//
// The frames, pinned on the server side by PreviewTransferPipeTest. All integers little-endian.
//
//   client -> server   16 bytes   the transfer id: the bytes its 32 hex digits spell, in order
//   server -> client    1 byte    status
//                       8 bytes   payload length, zero unless the status is ok
//                       n bytes   the payload
//
// Pure - no `vscode` import - so the framing is unit-tested against a socket.

import * as l10n from "@vscode/l10n";
import * as net from "net";

import type { PreviewTransfer } from "./protocol/modelPreview";

/** The payload follows. */
export const TRANSFER_OK = 0;
/** No transfer of that id is waiting: never offered, already collected, or expired. */
export const TRANSFER_UNKNOWN = 1;
/** The transfer was waiting and its payload could not be opened any more. */
export const TRANSFER_GONE = 2;

const REQUEST_LENGTH = 16;
const HEADER_LENGTH = 9;

/**
 * How long a transfer may go without a byte arriving before it is given up. A wait for the pipe to
 * say something, not for the whole payload: a large model that keeps coming is never cut short.
 */
export const TRANSFER_IDLE_MS = 30_000;

/** The request frame for a transfer id. */
export function transferRequest(id: string): Uint8Array {
    if (!/^[0-9a-fA-F]{32}$/.test(id)) {
        throw new Error(`Not a transfer id: '${id}'`);
    }

    const request = Buffer.from(id, "hex");
    return new Uint8Array(request.buffer, request.byteOffset, REQUEST_LENGTH);
}

/**
 * One response frame, put together from the chunks a socket delivers.
 *
 * The payload buffer is allocated once, when the header has said how long it is, and each chunk is
 * copied straight into it: an 80 MB model is held once, not as a list of chunks and their join.
 */
export class TransferFrame {
    private readonly header = new Uint8Array(HEADER_LENGTH);
    private headerFilled = 0;
    private payloadFilled = 0;
    private bytes: Uint8Array | null = null;

    /** The status byte, or null until the header has arrived. */
    get status(): number | null {
        return this.headerFilled === HEADER_LENGTH ? this.header[0] : null;
    }

    /** The payload length the header states, or null until it has arrived. */
    get length(): number | null {
        return this.bytes === null ? null : this.bytes.byteLength;
    }

    /** Whether the header and the whole payload are here. */
    get complete(): boolean {
        return (
            this.bytes !== null && this.payloadFilled === this.bytes.byteLength
        );
    }

    /** The payload. Only meaningful once `complete`. */
    get payload(): Uint8Array {
        return this.bytes ?? new Uint8Array(0);
    }

    /** Takes the next chunk off the socket. Bytes past the end of the frame are not this frame's. */
    push(chunk: Uint8Array): void {
        let at = 0;

        if (this.headerFilled < HEADER_LENGTH) {
            const take = Math.min(
                HEADER_LENGTH - this.headerFilled,
                chunk.byteLength,
            );
            this.header.set(chunk.subarray(0, take), this.headerFilled);
            this.headerFilled += take;
            at = take;

            if (this.headerFilled < HEADER_LENGTH) {
                return;
            }

            const length = new DataView(
                this.header.buffer,
                this.header.byteOffset,
                HEADER_LENGTH,
            ).getBigUint64(1, true);
            this.bytes = new Uint8Array(Number(length));
        }

        if (this.bytes === null) {
            return;
        }

        const take = Math.min(
            this.bytes.byteLength - this.payloadFilled,
            chunk.byteLength - at,
        );
        this.bytes.set(chunk.subarray(at, at + take), this.payloadFilled);
        this.payloadFilled += take;
    }
}

/** Why a transfer did not arrive, in words the preview shows. */
export class PreviewTransferError extends Error {}

/**
 * Whether a reply says the copy the webview asked with still stands: it names the same version,
 * offers no transfer and reports no error. Only then is nothing sent on - a reply that merely lacks
 * a transfer is a model or texture that did not load.
 */
export function standsUnchanged(
    have: string | undefined,
    reply: {
        transfer?: PreviewTransfer | null;
        version?: string | null;
        error?: string | null;
    },
): boolean {
    return (
        have !== undefined &&
        have !== "" &&
        (reply.transfer ?? null) === null &&
        (reply.error ?? null) === null &&
        reply.version === have
    );
}

/** What `collectTransfer` connects with; a test hands in its own. */
export type TransferConnect = (pipe: string) => net.Socket;

/**
 * Collects one transfer's bytes from the server's pipe.
 *
 * Rejects with a `PreviewTransferError` whose message is for the reader when the pipe cannot be
 * reached, closes before the payload is whole, sends nothing for `idleMs`, or the server no longer
 * has the transfer. A pipe that took the connection and never answered used to leave the part
 * loading for as long as the panel stayed open.
 */
export function collectTransfer(
    transfer: PreviewTransfer,
    connect: TransferConnect = (pipe) => net.connect(pipe),
    idleMs = TRANSFER_IDLE_MS,
): Promise<Uint8Array> {
    return new Promise((resolve, reject) => {
        const frame = new TransferFrame();
        let settled = false;
        let socket: net.Socket;

        const fail = (message: string): void => {
            if (settled) {
                return;
            }
            settled = true;
            socket?.destroy();
            reject(new PreviewTransferError(message));
        };

        try {
            socket = connect(transfer.pipe);
        } catch {
            fail(
                l10n.t(
                    "The preview's data channel could not be reached, so a model or texture was not loaded. Reopen the preview.",
                ),
            );
            return;
        }

        // Counted from now and again from every byte that arrives, the connection included.
        socket.setTimeout(idleMs, () => {
            fail(
                l10n.t(
                    "The preview's data channel did not answer in time, so a model or texture was not loaded. Reopen the preview.",
                ),
            );
        });

        socket.on("connect", () => {
            socket.write(transferRequest(transfer.id));
        });

        socket.on("data", (chunk: Buffer) => {
            frame.push(chunk);

            if (frame.status !== null && frame.status !== TRANSFER_OK) {
                fail(
                    l10n.t(
                        "The server no longer holds a model or texture it offered. Reopen the preview.",
                    ),
                );
                return;
            }

            if (frame.length !== null && frame.length !== transfer.length) {
                fail(
                    l10n.t(
                        "The preview's data channel sent {0} bytes where {1} were announced. Reopen the preview.",
                        frame.length,
                        transfer.length,
                    ),
                );
                return;
            }

            if (frame.complete && !settled) {
                settled = true;
                socket.destroy();
                resolve(frame.payload);
            }
        });

        socket.on("error", () => {
            fail(
                l10n.t(
                    "The preview's data channel could not be reached, so a model or texture was not loaded. Reopen the preview.",
                ),
            );
        });

        socket.on("close", () => {
            fail(
                l10n.t(
                    "The preview's data channel closed before a model or texture had arrived. Reopen the preview.",
                ),
            );
        });
    });
}
