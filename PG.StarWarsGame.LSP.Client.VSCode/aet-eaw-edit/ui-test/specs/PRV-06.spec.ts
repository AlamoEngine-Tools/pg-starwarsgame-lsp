// PRV-06: the loading cover on a particle file clears at once. That the blast is seen from its start is a
// picture and stays a hand check.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

describe("PRV-06", () => {
    before(async () => {
        await serverReady();
    });

    it("clears the cover within about two seconds of the stage", async () => {
        await openInPreview(model("P_explosion_big00.alo"));
        await inNewestWebview(async () => {
            // From the first frame the stage exists, which is later than the tab appearing - the
            // switch into the webview's frame is not free - so the measure is on the strict side.
            await (await browser.$(".stage-column")).waitForExist({ timeout: 60_000, interval: 50 });
            const start = Date.now();
            await browser.waitUntil(async () => !(await (await browser.$(".load-cover")).isExisting()), {
                timeout: 15_000,
                interval: 100,
                timeoutMsg: "The loading cover stayed",
            });
            expect(Date.now() - start).toBeLessThan(2_500);
        });
    });
});
