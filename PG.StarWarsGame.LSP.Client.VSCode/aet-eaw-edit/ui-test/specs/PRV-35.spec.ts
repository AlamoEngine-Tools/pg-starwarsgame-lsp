// PRV-35: the Overrides relation across layers - highest layer first, the game's last; a loose row opens at its
// line, the game's row is inert.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import {
    openOutline,
    railChildren,
    railClick,
    railDoubleClick,
    railExpand,
    rows,
    search,
    selectRow,
    tableReady,
} from "./modBrowser.js";

/** The active editor's file and one-based line, once it is the one asked for. */
async function editorAt(file: RegExp, line: number): Promise<void> {
    await browser.waitUntil(
        async () => {
            const at: string = await browser.executeWorkbench((vscode) => {
                const editor = vscode.window.activeTextEditor;
                return editor ? `${editor.document.uri.fsPath}:${editor.selection.active.line + 1}` : "";
            });
            return file.test(at.replace(/:\d+$/, "")) && at.endsWith(`:${line}`);
        },
        { timeout: 30_000, timeoutMsg: `No editor at ${file}:${line}` },
    );
}

describe("PRV-35", () => {
    before(async () => {
        await serverReady();
    });

    it("lists the base's and the game's definitions under Overrides", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search("Acclamator");
            const row = (await rows()).find((r) => r.cells[0] === "Acclamator_Assault_Ship")!;
            expect(row.cells[3]).toBe("PRV Mod");
            expect(row.tags).toEqual(["Overrides - PRV Base"]);

            await selectRow("Acclamator_Assault_Ship");
            await railExpand("Acclamator_Assault_Ship", 1);
            expect((await railChildren("Acclamator_Assault_Ship")).find((n) => n.label === "Overrides")?.count).toBe("2");
            await railExpand("Overrides", 2);
            const overrides = await railChildren("Overrides");
            expect(overrides.map((n) => n.label)).toEqual(["PrvBaseUnits.xml:3", "Acclamator_Assault_Ship"]);
            expect(overrides[1]).toMatchObject({ inert: true, title: "Packed - no file to open" });

            await railDoubleClick("PrvBaseUnits.xml:3", 3);
        });
        await editorAt(/aet-prv-ovr\\base\\Data\\XML\\PrvBaseUnits\.xml$/i, 3);
    });

    it("says a definition only the base has is defined once", async () => {
        await openOutline("Game Data");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search("Prv_Base_Only");
            expect((await rows()).find((r) => r.cells[0] === "Prv_Base_Only")?.cells[3]).toBe("PRV Base");
            await selectRow("Prv_Base_Only");
            await railExpand("Prv_Base_Only", 1);
            expect((await railChildren("Prv_Base_Only")).find((n) => n.label === "Overrides")).toMatchObject({
                inert: true,
                title: "Defined once",
            });
        });
    });
});
