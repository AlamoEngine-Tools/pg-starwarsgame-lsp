// PRJ-03: panels refresh once when the startup scan completes, not after every scan stage.
import { browser, expect } from "@wdio/globals";
import { outputChannels, restartServer, serverReady } from "../protocol.js";
import { inNewestWebview, openStoryGraph } from "./storyGraph.js";

describe("PRJ-03", () => {
    it("refreshes the localisation and story views at most once after a restart", async () => {
        await serverReady();
        await openStoryGraph("Sandbox_Alderaans_Demise_Rebel", "Rebel");
        await inNewestWebview(async () => (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 }));
        const before = (outputChannels().get("EaWEdit") ?? "").length;

        await restartServer();
        await browser.pause(10_000);
        const after = (outputChannels().get("EaWEdit") ?? "").slice(before).split(/\r?\n/);
        expect(after.filter((l) => l.includes("Localisation index updated - refreshing localisation views.")).length).toBeLessThanOrEqual(1);
        expect(after.filter((l) => /\] Story graph changed:/.test(l)).length).toBeLessThanOrEqual(1);

        const text = await inNewestWebview(async () => browser.execute(() => document.body.innerText));
        expect(text).not.toContain("LSP server is not running.");
    });
});
