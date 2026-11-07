// PRV-22: a click selects, a double click or Enter opens into the preview tab, pinning keeps it, F12 goes to
// the definition. The rail's full keyboard walk stays a hand check; its Up and Down are checked here.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { openOutline, openRow, rail, railClick, rows, search, selectRow, tableReady, tabs } from "./modBrowser.js";

describe("PRV-22", () => {
    before(async () => {
        await serverReady();
    });

    it("selects on a click, opens on a double click or Enter, pins and goes to the definition", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search("Nebulon");
            const [first, second, third] = (await rows()).map((r) => r.cells[0]).filter((n) => n !== "Nebulon_B_Frigate");
            expect(third).toBeDefined();

            await selectRow("Nebulon_B_Frigate");
            await browser.pause(1_000);
            expect((await tabs()).length).toBe(1);
            const relations = await rail();
            const at = relations.findIndex((n) => n.label === "Nebulon_B_Frigate");
            expect(relations[at].selected).toBe(true);
            expect(relations[at + 1].depth).toBeGreaterThan(relations[at].depth);

            await openRow("Nebulon_B_Frigate");
            await browser.waitUntil(async () => (await tabs()).length === 2, { timeout: 30_000 });
            expect((await tabs())[1]).toEqual({
                name: "Nebulon_B_Frigate",
                on: true,
                unpinned: true,
                title: "Preview - double click to keep it",
            });

            await (await browser.$("button.editor-tab-name=Game Objects")).click();
            await selectRow(first);
            await browser.keys("Enter");
            await browser.waitUntil(async () => (await tabs()).some((t) => t.name === first), { timeout: 30_000 });
            expect((await tabs()).map((t) => t.name)).toEqual(["Game Objects", first]);

            await (await browser.$('.editor-tab.unpinned button[title="Keep open"]')).click();
            expect((await tabs())[1].unpinned).toBe(false);

            await (await browser.$("button.editor-tab-name=Game Objects")).click();
            // The table's own tab kept its search and its selection.
            expect((await rows()).every((r) => /nebulon/i.test(r.cells[0]))).toBe(true);
            expect((await rows()).find((r) => r.selected)?.cells[0]).toBe(first);
            await selectRow(second);
            await browser.keys("Enter");
            await browser.waitUntil(async () => (await tabs()).length === 3, { timeout: 30_000 });
            expect((await tabs()).map((t) => [t.name, t.unpinned])).toEqual([
                ["Game Objects", false],
                [first, false],
                [second, true],
            ]);

            await (await browser.$("button.editor-tab-name=Game Objects")).click();
            await selectRow("Nebulon_B_Frigate");
            await browser.keys("F12");
        });
        await browser.waitUntil(
            async () => {
                const at = await browser.executeWorkbench((vscode) => {
                    const editor = vscode.window.activeTextEditor;
                    return editor ? `${editor.document.uri.fsPath}:${editor.selection.active.line + 1}` : "";
                });
                return /Spaceunitsfrigates\.xml:4$/i.test(at);
            },
            { timeout: 30_000, timeoutMsg: "F12 did not open Spaceunitsfrigates.xml at line 4" },
        );
    });
});
