// PRJ-06: references go to the owning project's server in a two-project window.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { eaw, foc, hoverAt, projectsReady } from "../protocol.js";

async function referencesOf(file: string, line: number): Promise<string[]> {
    let found: string[] = [];
    await browser.waitUntil(
        async () => {
            found = await browser.executeWorkbench(
                async (vscode, file: string, line: number) => {
                    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
                    await vscode.window.showTextDocument(document);
                    const at = document.lineAt(line).text.indexOf("Star_Destroyer") + 3;
                    const locations = await vscode.commands.executeCommand(
                        "vscode.executeReferenceProvider",
                        document.uri,
                        new vscode.Position(line, at),
                    );
                    return (locations ?? []).map((l: any) => l.uri.fsPath.toLowerCase());
                },
                file,
                line,
            );
            return found.length > 0;
        },
        { timeout: 60_000, interval: 2000, timeoutMsg: `No references from ${file}` },
    );
    return found;
}

describe("PRJ-06", () => {
    before(async () => {
        await projectsReady(2);
    });

    it("finds references from the EaW file in EaW only", async () => {
        const found = await referencesOf(path.join(eaw, "Data", "Xml", "Spaceunitscapital.xml"), 391);
        expect(found.filter((f) => !f.startsWith(eaw.toLowerCase() + "\\"))).toEqual([]);
    });

    it("finds references from the FoC file in FoC only", async () => {
        const found = await referencesOf(path.join(foc, "Data", "Xml", "Spaceunitscapital.xml"), 406);
        expect(found.filter((f) => !f.startsWith(foc.toLowerCase() + "\\"))).toEqual([]);
    });

    it("hovers Variant_Of_Existing_Type in both files", async () => {
        expect(await hoverAt(path.join(eaw, "Data", "Xml", "Spaceunitscapital.xml"), 392, 4)).not.toBe("");
        expect(await hoverAt(path.join(foc, "Data", "Xml", "Spaceunitscapital.xml"), 407, 4)).not.toBe("");
    });
});
