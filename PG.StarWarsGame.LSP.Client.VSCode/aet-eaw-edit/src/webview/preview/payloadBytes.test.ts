// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { bufferOf } from "./payloadBytes";

describe("bufferOf", () => {
    it("hands a loader a message's bytes without copying them", () => {
        const bytes = new Uint8Array([103, 108, 84, 70, 2, 0, 0, 0]);

        assert.equal(bufferOf(bytes), bytes.buffer);
    });

    it("hands a loader just the view's bytes when the view is part of a larger buffer", () => {
        const whole = new Uint8Array([9, 9, 103, 108, 84, 70, 9]);
        const view = whole.subarray(2, 6);

        const buffer = bufferOf(view);

        assert.notEqual(buffer, whole.buffer);
        assert.deepEqual([...new Uint8Array(buffer)], [103, 108, 84, 70]);
    });
});
