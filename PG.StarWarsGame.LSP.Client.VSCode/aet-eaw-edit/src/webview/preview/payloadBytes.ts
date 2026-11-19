// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A model's or a texture's bytes, as they arrive in a message from the host (#436).

/**
 * A payload's bytes as the buffer a loader parses.
 *
 * The view's own buffer when the view is all of it - the usual case for a message's typed array -
 * so an 80 MB model is not copied to be read; a copy of just its bytes otherwise, because the
 * three.js loaders read from the start of whatever buffer they are handed.
 */
export function bufferOf(bytes: Uint8Array): ArrayBuffer {
    if (
        bytes.buffer instanceof ArrayBuffer &&
        bytes.byteOffset === 0 &&
        bytes.byteLength === bytes.buffer.byteLength
    ) {
        return bytes.buffer;
    }

    return bytes.slice().buffer;
}
