// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { requestDiscard } from "./discardRequest";
import type { Transport } from "./vscodeApi";

/** A transport that records what is posted and lets a test deliver the host's messages. */
function fakeTransport() {
    const posted: { type: string; id: number; count: number }[] = [];
    const handlers = new Set<(message: unknown) => void>();
    const transport: Transport = {
        post: (message) => posted.push(message as (typeof posted)[number]),
        listen(handler) {
            handlers.add(handler);
            return () => handlers.delete(handler);
        },
    };
    return {
        transport,
        posted,
        listening: () => handlers.size,
        deliver: (message: unknown) => {
            for (const handler of [...handlers]) {
                handler(message);
            }
        },
    };
}

describe("requestDiscard", () => {
    it("asks the host, with how many changes are staged", () => {
        const { transport, posted } = fakeTransport();

        void requestDiscard(transport, 3);

        assert.equal(posted.length, 1);
        assert.equal(posted[0].type, "confirmDiscard");
        assert.equal(posted[0].count, 3);
    });

    it("is yes on the host's yes to this question", async () => {
        const { transport, posted, deliver } = fakeTransport();
        const asked = requestDiscard(transport, 1);

        deliver({ type: "discardAnswer", id: posted[0].id, confirmed: true });

        assert.equal(await asked, true);
    });

    it("is no on the host's no", async () => {
        const { transport, posted, deliver } = fakeTransport();
        const asked = requestDiscard(transport, 1);

        deliver({ type: "discardAnswer", id: posted[0].id, confirmed: false });

        assert.equal(await asked, false);
    });

    /** Two pages of one window, or a question asked twice: each gets its own answer. */
    it("takes no answer meant for another question", async () => {
        const { transport, posted, deliver } = fakeTransport();
        const first = requestDiscard(transport, 1);
        const second = requestDiscard(transport, 2);

        deliver({ type: "discardAnswer", id: posted[1].id, confirmed: true });
        deliver({ type: "discardAnswer", id: posted[0].id, confirmed: false });

        assert.equal(await first, false);
        assert.equal(await second, true);
    });

    it("stops listening once it is answered, and ignores everything else until then", async () => {
        const { transport, posted, deliver, listening } = fakeTransport();
        const asked = requestDiscard(transport, 1);

        deliver({ type: "rows" });
        deliver(null);
        assert.equal(listening(), 1);

        deliver({ type: "discardAnswer", id: posted[0].id, confirmed: true });
        await asked;
        assert.equal(listening(), 0);
    });
});
