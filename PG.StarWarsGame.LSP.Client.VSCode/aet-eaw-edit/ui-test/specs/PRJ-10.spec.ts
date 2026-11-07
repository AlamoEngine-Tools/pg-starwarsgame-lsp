// PRJ-10: a file first opened at a location the server supplied keeps its on-disk spelling and gets findings
// and hovers.
import { browser, expect } from "@wdio/globals";
import { EAW, projectsReady } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";
import { openOutline, railClick, search, selectRow, tableReady } from "./modBrowser.js";

describe("PRJ-10", () => {
    before(async () => {
        await projectsReady(2);
        await closeAll();
    });

    it("opens the definition under its on-disk spelling, with findings and a hover", async () => {
        await openOutline("Game Data", EAW);
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Game Objects");
            await tableReady();
            await search("Star_Destroyer");
            await selectRow("Star_Destroyer");
            await browser.keys("F12");
        });

        let at = { path: "", line: -1 };
        await browser.waitUntil(
            async () => {
                at = await browser.executeWorkbench((vscode) => {
                    const editor = vscode.window.activeTextEditor;
                    return editor
                        ? { path: editor.document.uri.fsPath as string, line: editor.selection.active.line + 1 }
                        : { path: "", line: -1 };
                });
                return /Spaceunitscapital\.xml$/i.test(at.path);
            },
            { timeout: 30_000, timeoutMsg: "F12 opened no Spaceunitscapital.xml" },
        );
        expect(at.line).toBe(392);
        expect(at.path).toMatch(/\\PG\.StarWarsGame\.LSP\\eaw\\Data\\Xml\\Spaceunitscapital\.xml$/);

        // Edited in the very document the editor holds, so the finding is for that document's URI.
        const found: { message: string; source: string }[] = await browser.executeWorkbench(async (vscode) => {
            const editor = vscode.window.activeTextEditor!;
            const line = editor.selection.active.line;
            await editor.edit((b: any) => b.insert(new vscode.Position(line + 1, 0), "<Zzq_Not_A_Tag>1</Zzq_Not_A_Tag>\n"));
            const uri = editor.document.uri;
            for (let i = 0; i < 90; i++) {
                const here = vscode.languages
                    .getDiagnostics(uri)
                    .filter((d: any) => String(typeof d.code === "object" ? d.code.value : d.code) === "aetswg-006-0012" && d.range.start.line === line + 1);
                if (here.length > 0) return here.map((d: any) => ({ message: d.message as string, source: d.source as string }));
                await new Promise((r) => setTimeout(r, 1000));
            }
            return [];
        });
        expect(found.length).toBe(1);
        expect(found[0].message).toContain("<Zzq_Not_A_Tag> is not a known SpaceUnit tag.");
        expect(found[0].source).toBe(`aetswg (${EAW})`);

        const hover: string = await browser.executeWorkbench(async (vscode) => {
            const editor = vscode.window.activeTextEditor!;
            const document = editor.document;
            let line = editor.selection.active.line;
            while (!document.lineAt(line).text.includes("<Tech_Level>")) line++;
            const at = new vscode.Position(line, document.lineAt(line).text.indexOf("Tech_Level") + 2);
            const hovers: any[] = (await vscode.commands.executeCommand("vscode.executeHoverProvider", document.uri, at)) ?? [];
            const text = hovers
                .flatMap((h) => h.contents)
                .map((c: any) => (typeof c === "string" ? c : c.value))
                .join("\n");
            await vscode.commands.executeCommand("workbench.action.files.revert");
            return text;
        });
        expect(hover).toContain("Tech_Level");
    });
});
