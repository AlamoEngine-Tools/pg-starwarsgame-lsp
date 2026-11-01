// PRV-16: the Preview Model picker lists models only - no particle system.
import { browser, expect } from "@wdio/globals";
import { clearNotifications, serverReady } from "../protocol.js";

/** The picker's rows after typing a filter into it, keystroke by keystroke as a reader types. */
async function rowsFor(filter: string): Promise<{ label: string; description: string }[]> {
    await browser.keys(["Control", "a"]);
    await browser.keys("Backspace");
    await browser.keys(filter.split(""));
    await browser.pause(1_000);
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".quick-input-widget .monaco-list-row")).map((r) => ({
            label: (r.querySelector(".label-name")?.textContent ?? "").trim(),
            description: (r.querySelector(".label-description")?.textContent ?? "").trim(),
        })),
    );
}

describe("PRV-16", () => {
    before(async () => {
        await serverReady();
    });

    it("leaves particle systems out and lists the shuttle's models", async () => {
        await clearNotifications();
        await browser.executeWorkbench((vscode) => {
            void vscode.commands.executeCommand("aet-eaw-edit.lsp.previewModel");
        });
        const input = await browser.$(".quick-input-widget .quick-input-box input");
        await input.waitForDisplayed({ timeout: 30_000 });
        await browser.waitUntil(
            async () => (await browser.execute(() => document.querySelectorAll(".quick-input-widget .monaco-list-row").length)) > 0,
            { timeout: 60_000, timeoutMsg: "The picker lists nothing" },
        );

        expect((await rowsFor("P_asteroid_field")).filter((r) => /^P_asteroid_field/i.test(r.label))).toEqual([]);

        const shuttles = await rowsFor("Ev_lambdashuttle");
        // As the files are spelled on disk, not as the game's archives spell them.
        expect(shuttles.map((r) => r.label).sort()).toEqual([
            "Ev_lambdashuttle.alo",
            "Ev_lambdashuttle_space.alo",
            "Ev_lambdashuttle_space_d.alo",
        ]);
        for (const row of shuttles) {
            expect(["Project", "Base game"]).toContain(row.description);
        }
        await browser.keys("Escape");
    });
});
