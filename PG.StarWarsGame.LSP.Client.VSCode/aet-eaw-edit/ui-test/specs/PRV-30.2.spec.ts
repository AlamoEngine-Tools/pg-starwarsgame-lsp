// PRV-30, second part: for comparison, the model preview's mode switch, whose positions are never disabled.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

describe("PRV-30", () => {
    before(async () => {
        await serverReady();
    });

    it("never disables a model preview mode", async () => {
        await openInPreview(model("Ev_lambdashuttle.alo"));
        await inNewestWebview(async () => {
            await (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 });
            const found: { title: string; disabled: boolean }[] = await browser.execute(() =>
                Array.from(document.querySelectorAll<HTMLButtonElement>("button.rotary-pos")).map((b) => ({
                    title: b.getAttribute("title") ?? "",
                    disabled: b.disabled,
                })),
            );
            expect(found.map((p) => p.title.split(" (")[0]).sort()).toEqual(["Animation", "Gameplay", "Model"]);
            expect(found.filter((p) => p.disabled)).toEqual([]);
        });
    });
});
