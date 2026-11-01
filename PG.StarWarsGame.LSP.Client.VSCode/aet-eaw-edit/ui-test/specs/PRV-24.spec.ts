// PRV-24: a planet offers no encyclopedia card - no code lens, a disabled action, no menu item, no tile.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { eaw, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { actions, contextMenu, openOutline, openRow, panels, railClick, search, tableReady } from "./modBrowser.js";

/** The titles of the code lenses on a line (1-based), once the server answers with any for the file. */
async function lensesOn(file: string, line: number): Promise<string[]> {
    let titles: string[] = [];
    await browser.waitUntil(
        async () => {
            const all: { line: number; title: string }[] = await browser.executeWorkbench(async (vscode, target: string) => {
                const uri = vscode.Uri.file(target);
                await vscode.workspace.openTextDocument(uri);
                const lenses: any[] = (await vscode.commands.executeCommand("vscode.executeCodeLensProvider", uri, 200)) ?? [];
                return lenses.map((l) => ({ line: l.range.start.line, title: l.command?.title ?? "" }));
            }, file);
            titles = all.filter((l) => l.line === line - 1).map((l) => l.title);
            return all.length > 0;
        },
        { timeout: 120_000, timeoutMsg: `No code lenses in ${file}` },
    );
    return titles;
}

describe("PRV-24", () => {
    before(async () => {
        await serverReady();
    });

    it("offers the frigate's card and not Coruscant's as a code lens", async () => {
        const xml = path.join(eaw, "Data", "XML");
        expect(await lensesOn(path.join(xml, "Spaceunitsfrigates.xml"), 4)).toContain("preview encyclopedia");
        const coruscant = await lensesOn(path.join(xml, "Planets.xml"), 968);
        expect(coruscant.length).toBeGreaterThan(0);
        expect(coruscant).not.toContain("preview encyclopedia");
    });

    it("disables the card for Coruscant in the Mod Browser", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search("Coruscant");

            expect((await contextMenu("Coruscant")).map((m) => m.label)).not.toContain("Show encyclopedia card");

            await openRow("Coruscant");
            const card = (await actions()).find((a) => a.label === "Show encyclopedia card");
            expect(card).toMatchObject({ disabled: true, title: "Not for a planet" });
            expect(await panels()).not.toContain("Encyclopedia");
        });
    });
});
