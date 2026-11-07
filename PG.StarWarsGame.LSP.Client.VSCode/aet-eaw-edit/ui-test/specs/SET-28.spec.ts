// SET-28: on a graph whose threads all belong to a referenced project, Edit is disabled and says why. The
// dimming and the cursor are a picture and stay a hand check.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const REASON =
    "Every thread here belongs to 'Empire at War - Vanilla', which this workspace only references. Open the graph in that project to edit it.";

describe("SET-28", () => {
    before(async () => {
        await serverReady();
    });

    it("disables Edit with the reason, and the graph stays in View", async () => {
        await openStoryGraph("Full_Story_Campaign_Empire", "Empire");
        await inNewestWebview(async () => {
            await (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 });
            await graphLoaded();
            const positions: { title: string; disabled: boolean; active: boolean }[] = await browser.execute(() =>
                Array.from(document.querySelectorAll<HTMLButtonElement>("button.rotary-pos")).map((b) => ({
                    title: b.getAttribute("title") ?? "",
                    disabled: b.disabled || b.getAttribute("aria-disabled") === "true",
                    active: b.classList.contains("active") || b.getAttribute("aria-checked") === "true",
                })),
            );
            expect(positions.map((p) => p.title)).toEqual(["View", REASON]);
            expect(positions[1].disabled).toBe(true);

            await browser.execute(() => document.querySelectorAll<HTMLButtonElement>("button.rotary-pos")[1].click());
            await browser.pause(1_000);
            const active: string = await browser.execute(
                () =>
                    Array.from(document.querySelectorAll("button.rotary-pos"))
                        .find((b) => b.classList.contains("active") || b.getAttribute("aria-checked") === "true")
                        ?.getAttribute("title") ?? "",
            );
            expect(active).toBe("View");
        });
    });
});
