// PRV-14: bone names with a dot read as the exporter wrote them - in the tree, on the stage and in the inspector.
import { browser, expect } from "@wdio/globals";
import { clearNotifications, serverReady, tabLabels } from "../protocol.js";
import { model, openInPreview, press } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

const dotted = ["Camera01.Target", "Spot01.Target", "Spot02.Target", "Spot03.Target", "Spot04.Target", "Spot05.Target"];

describe("PRV-14", () => {
    before(async () => {
        await serverReady();
    });

    it("keeps the dot in the tree, the stage labels and the inspector", async () => {
        await clearNotifications();
        await openInPreview(model("Eb_icc.alo"));
        await inNewestWebview(async () => {
            const filter = await browser.$('button[title="Filter the tree by name"]');
            await filter.waitForClickable({ timeout: 120_000 });
            await filter.click();
            await (await browser.$("input.tree-filter")).setValue("Target");

            let names: string[] = [];
            await browser.waitUntil(
                async () => {
                    names = await browser.execute(() =>
                        Array.from(document.querySelectorAll(".bone-tree .tree-name")).map((n) => (n.textContent ?? "").trim()),
                    );
                    return dotted.every((d) => names.includes(d));
                },
                { timeout: 30_000, timeoutMsg: "The tree lacks the dotted bones" },
            );
            expect(names.filter((n) => /^(Camera01|Spot0\d)Target$/.test(n))).toEqual([]);

            // Labels that crowd each other are dropped from the stage; the selected bone's never is.
            await press('button[title="Name every bone"]');
            await press("//li[contains(@class,'bone-row')][.//span[@class='tree-name' and text()='Spot01.Target']]");
            let labels: string[] = [];
            await browser.waitUntil(
                async () => {
                    labels = await browser.execute(() =>
                        Array.from(document.querySelectorAll<HTMLElement>(".bone-labels .bone-label"))
                            .filter((n) => n.style.display !== "none")
                            .map((n) => (n.textContent ?? "").trim()),
                    );
                    return labels.length > 0;
                },
                { timeout: 10_000, timeoutMsg: "No bone labels on the stage" },
            );
            const selected: string = await browser.execute(
                () => (document.querySelector(".bone-labels .bone-label.selected")?.textContent ?? "").trim(),
            );
            expect(selected).toBe("Spot01.Target");
            expect(labels.filter((n) => /^(Camera01|Spot0\d)Target$/.test(n))).toEqual([]);

            await press('button[title="Inspect Spot01.Target"]');
        });
        await browser.waitUntil(async () => (await tabLabels()).includes("Model inspector"), {
            timeout: 30_000,
            timeoutMsg: "No inspector opened",
        });
        // The inspector is the panel that is not the preview: the one naming the bone in its text.
        const workbench = await browser.getWorkbench();
        let named = false;
        await browser.waitUntil(
            async () => {
                for (const view of await workbench.getAllWebviews()) {
                    await view.open();
                    try {
                        const text: string = await browser.execute(() => document.body.innerText);
                        // innerText carries the CSS's capitals, so the heading is matched without case.
                        if (text.includes("Spot01.Target") && !/model tree/i.test(text)) named = true;
                    } finally {
                        await view.close();
                    }
                }
                return named;
            },
            { timeout: 30_000, timeoutMsg: "The inspector does not name Spot01.Target" },
        );
    });
});
