// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as assert from "node:assert/strict";
import * as net from "net";
import * as os from "os";
import * as path from "path";
import { test } from "node:test";

import {
    collectTransfer,
    PreviewTransferError,
    standsUnchanged,
    TRANSFER_GONE,
    TRANSFER_OK,
    TRANSFER_UNKNOWN,
    TransferFrame,
    transferRequest,
} from "./previewTransfer";

const id = "000102030405060708090a0b0c0d0e0f";

test("a reply with the version asked with, no transfer and no error leaves the copy standing", () => {
    assert.equal(standsUnchanged("v1", { version: "v1" }), true);
    assert.equal(
        standsUnchanged("v1", { version: "v1", transfer: null, error: null }),
        true,
    );
});

test("a reply stands for nothing when it is another version, an error, a transfer, or was not asked with a version", () => {
    const transfer = { pipe: "p", id, length: 1, format: "glb" };

    assert.equal(standsUnchanged("v1", { version: "v2" }), false);
    assert.equal(
        standsUnchanged("v1", { version: "v1", error: "gone" }),
        false,
    );
    assert.equal(standsUnchanged("v1", { version: "v1", transfer }), false);
    assert.equal(standsUnchanged(undefined, { version: "v1" }), false);
    assert.equal(standsUnchanged("", { version: "" }), false);
    // A model that did not resolve has no transfer either - and no version.
    assert.equal(standsUnchanged("v1", {}), false);
});

/** A response frame as the server writes it: status, the length little-endian, the payload. */
function response(status: number, payload: number[]): Buffer {
    const frame = Buffer.alloc(9 + payload.length);
    frame[0] = status;
    frame.writeBigUInt64LE(BigInt(payload.length), 1);
    Buffer.from(payload).copy(frame, 9);
    return frame;
}

/** A pipe that answers each request with what `answer` returns, split into chunks of `step` bytes. */
async function serve(
    answer: (request: Buffer, socket: net.Socket) => Buffer | null,
    step = Number.MAX_SAFE_INTEGER,
): Promise<{ pipe: string; close: () => void; requests: Buffer[] }> {
    const name = `aet-transfer-test-${process.pid}-${Math.random().toString(16).slice(2)}`;
    const pipe =
        process.platform === "win32"
            ? `\\\\.\\pipe\\${name}`
            : path.join(os.tmpdir(), name);
    const requests: Buffer[] = [];
    const server = net.createServer((socket) => {
        socket.on("error", () => undefined);
        socket.on("data", (chunk) => {
            const request = Buffer.from(chunk);
            requests.push(request);
            const frame = answer(request, socket);
            if (frame === null) {
                return;
            }
            for (let at = 0; at < frame.length; at += step) {
                socket.write(frame.subarray(at, at + step));
            }
        });
    });
    await new Promise<void>((resolve) => server.listen(pipe, resolve));
    return { pipe, close: () => server.close(), requests };
}

test("the request is the sixteen bytes the id's hex digits spell", () => {
    assert.deepEqual(
        [...transferRequest(id)],
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
    );
    assert.throws(() => transferRequest("not-an-id"));
});

test("a frame is read whole from one chunk", () => {
    const frame = new TransferFrame();

    frame.push(response(TRANSFER_OK, [7, 8, 9]));

    assert.equal(frame.status, TRANSFER_OK);
    assert.equal(frame.length, 3);
    assert.equal(frame.complete, true);
    assert.deepEqual([...frame.payload], [7, 8, 9]);
});

test("a frame is read from chunks that split the header and the payload anywhere", () => {
    const bytes = response(TRANSFER_OK, [1, 2, 3, 4, 5]);

    for (let step = 1; step <= bytes.length; step++) {
        const frame = new TransferFrame();
        for (let at = 0; at < bytes.length; at += step) {
            assert.equal(frame.complete, false);
            frame.push(bytes.subarray(at, at + step));
        }

        assert.equal(frame.complete, true, `step ${step}`);
        assert.deepEqual([...frame.payload], [1, 2, 3, 4, 5], `step ${step}`);
    }
});

test("a frame knows nothing until its header is whole", () => {
    const frame = new TransferFrame();

    frame.push(response(TRANSFER_OK, [1]).subarray(0, 8));

    assert.equal(frame.status, null);
    assert.equal(frame.length, null);
    assert.equal(frame.complete, false);
});

test("a transfer is collected: the id goes out, the bytes come back", async () => {
    const server = await serve(() => response(TRANSFER_OK, [5, 6, 7, 8]), 3);
    try {
        const bytes = await collectTransfer({
            pipe: server.pipe,
            id,
            length: 4,
            format: "glb",
        });

        assert.deepEqual([...bytes], [5, 6, 7, 8]);
        assert.deepEqual(
            [...Buffer.concat(server.requests)],
            [...transferRequest(id)],
        );
    } finally {
        server.close();
    }
});

test("a large transfer arrives whole", async () => {
    const payload = Array.from({ length: 300_000 }, (_, i) => (i * 31) & 0xff);
    const server = await serve(() => response(TRANSFER_OK, payload), 4096);
    try {
        const bytes = await collectTransfer({
            pipe: server.pipe,
            id,
            length: payload.length,
            format: "glb",
        });

        assert.equal(bytes.byteLength, payload.length);
        assert.deepEqual([...bytes.subarray(0, 64)], payload.slice(0, 64));
        assert.deepEqual([...bytes.subarray(-64)], payload.slice(-64));
    } finally {
        server.close();
    }
});

for (const status of [TRANSFER_UNKNOWN, TRANSFER_GONE]) {
    test(`a transfer the server no longer holds (status ${status}) is an error for the reader`, async () => {
        const server = await serve(() => response(status, []));
        try {
            await assert.rejects(
                collectTransfer({
                    pipe: server.pipe,
                    id,
                    length: 4,
                    format: "glb",
                }),
                (error: unknown) =>
                    error instanceof PreviewTransferError &&
                    /no longer holds a model or texture/.test(error.message),
            );
        } finally {
            server.close();
        }
    });
}

test("a pipe that closes before the payload is whole is an error for the reader", async () => {
    const server = await serve((_request, socket) => {
        socket.write(response(TRANSFER_OK, [1, 2, 3, 4]).subarray(0, 11));
        socket.end();
        return null;
    });
    try {
        await assert.rejects(
            collectTransfer({
                pipe: server.pipe,
                id,
                length: 4,
                format: "glb",
            }),
            (error: unknown) =>
                error instanceof PreviewTransferError &&
                /closed before a model or texture had arrived/.test(
                    error.message,
                ),
        );
    } finally {
        server.close();
    }
});

test("a pipe that is not there is an error for the reader", async () => {
    const gone =
        process.platform === "win32"
            ? "\\\\.\\pipe\\aet-transfer-test-nobody-listens"
            : path.join(os.tmpdir(), "aet-transfer-test-nobody-listens");

    await assert.rejects(
        collectTransfer({ pipe: gone, id, length: 4, format: "glb" }),
        (error: unknown) =>
            error instanceof PreviewTransferError &&
            /could not be reached/.test(error.message),
    );
});

test("a payload of another length than the reply announced is an error for the reader", async () => {
    const server = await serve(() => response(TRANSFER_OK, [1, 2]));
    try {
        await assert.rejects(
            collectTransfer({
                pipe: server.pipe,
                id,
                length: 4,
                format: "glb",
            }),
            (error: unknown) =>
                error instanceof PreviewTransferError &&
                /2 bytes where 4 were announced/.test(error.message),
        );
    } finally {
        server.close();
    }
});
