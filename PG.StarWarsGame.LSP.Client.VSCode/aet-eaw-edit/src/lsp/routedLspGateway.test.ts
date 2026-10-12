// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { LspGateway, SERVER_NOT_RUNNING } from "./lspGateway";
import { RoutedLspGateway } from "./routedLspGateway";

/** A gateway over a fake server that answers every request with its own name. */
function serverNamed(name: string): LspGateway {
    return new LspGateway(() => ({
        sendRequest: <T>(method: string) =>
            Promise.resolve(`${name}:${method}` as T),
    }));
}

/**
 * The gateway the panels and navigators hold, over one server per project (#229).
 *
 * They were handed one gateway at construction and keep it for the session, while servers now come
 * and go per project. The routed gateway is that one object: each call goes to whichever project's
 * server the route names at that moment.
 */
describe("RoutedLspGateway", () => {
    it("sends each request to the server the route names at that moment", async () => {
        let current = serverNamed("rev");
        const routed = new RoutedLspGateway(() => current);

        const first = await routed.request<string>("aet/getStoryPlots");
        current = serverNamed("fotr");
        const second = await routed.request<string>("aet/getStoryPlots");

        assert.deepEqual(first, { ok: true, value: "rev:aet/getStoryPlots" });
        assert.deepEqual(second, { ok: true, value: "fotr:aet/getStoryPlots" });
    });

    it("is offline with no server to route to", async () => {
        const routed = new RoutedLspGateway(() => undefined);

        assert.equal(routed.isRunning, false);
        assert.deepEqual(await routed.request("aet/listModels"), {
            ok: false,
            reason: "offline",
            message: SERVER_NOT_RUNNING,
        });
    });

    it("routes the helpers too, since they all go through request", async () => {
        const routed = new RoutedLspGateway(() => serverNamed("core"));

        assert.equal(
            await routed.requestOr("aet/x", {}, "fallback"),
            "core:aet/x",
        );
        assert.equal(await routed.executeCommand("aet-eaw-edit.reload"), true);
    });

    it("waits on the routed server's readiness", async () => {
        const server = serverNamed("rev");
        const routed = new RoutedLspGateway(() => server);

        const waiting = routed.whenReady(1000);
        server.markReady();

        assert.equal(await waiting, true);
    });

    it("waits for a server to appear when there is none yet, then for it to be ready", async () => {
        let current: LspGateway | undefined;
        const routed = new RoutedLspGateway(() => current);

        const waiting = routed.whenReady(1000);
        current = serverNamed("rev");
        routed.routesChanged();
        current.markReady();

        assert.equal(await waiting, true);
    });

    it("gives up when no server appears in time", async () => {
        const routed = new RoutedLspGateway(() => undefined);

        assert.equal(await routed.whenReady(10), false);
    });
});
