// PRJ-18: the model preview reads the archives the catalog reports - a packed model with its packed texture,
// and no texture problem. How the hull looks stays a hand check.
import { browser, expect } from "@wdio/globals";
import { logLine, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";

/** The server's own channel. */
const SERVER = "Alamo Engine Tools - Empire at War Edit";

describe("PRJ-18", () => {
    before(async () => {
        await serverReady();
    });

    it("draws the packed Tartan cruiser with no texture problem", async () => {
        await browser.executeWorkbench((vscode) => {
            void vscode.commands.executeCommand("aet-eaw-edit.lsp.previewModel", "EV_TartanCruiser.alo");
        });
        await inNewestWebview(async () => {
            await (await browser.$(".stage-column")).waitForExist({ timeout: 120_000 });
            await browser.waitUntil(async () => !(await (await browser.$(".load-cover")).isExisting()), {
                timeout: 120_000,
                timeoutMsg: "The cruiser never finished loading",
            });
            const tag = await browser.$("button.validate-btn");
            let notes: string[] = [];
            if (await tag.isEnabled()) {
                await browser.execute(() => document.querySelector<HTMLButtonElement>("button.validate-btn")?.click());
                await (await browser.$(".problem-list")).waitForExist({ timeout: 5_000 });
                notes = await browser.execute(() =>
                    Array.from(document.querySelectorAll(".problem-list .problem-msg")).map((m) => (m.textContent ?? "").trim()),
                );
            }
            expect(notes.filter((n) => /Texture .*(was not found|could not be read)/.test(n))).toEqual([]);
        });
    });

    // After the preview: the archives are read on the first asset a reader asks for, not at startup.
    it("logs the archives and the game path it reads", async () => {
        expect(await logLine(/Indexed \d+ entries from \d+ MEG archive/, SERVER)).toContain(
            "Indexed 4490 entries from 2 MEG archive(s)",
        );
        expect(await logLine(/LSP configuration loaded/, SERVER)).toMatch(/gamePath=C:\\temp\\aet-test-PRJ-preview\\nogame\)/i);
    });
});
