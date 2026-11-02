// PRV-28: every dynamic enum value carries a references lens, and pressing it opens the references peek on
// the XML files whose tags hold the value. The lens counts are also
// EawPreviewProtocolTest.PRV_28_EachDynamicEnumValue_CarriesAReferencesLens.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { eaw, serverReady } from "../protocol.js";
import { codeLenses } from "./xmlEditor.js";

const file = path.join(eaw, "Data", "XML", "Enum", "Movementclasstype.xml");
const COUNT = /^(1 reference|\d+ references)$/;

describe("PRV-28", () => {
    before(async () => {
        await serverReady();
    });

    it("counts the references of each value and opens them in the peek", async () => {
        let lenses = await codeLenses(file);
        await browser.waitUntil(async () => (lenses = await codeLenses(file)).some((l) => l.line === 4 && COUNT.test(l.title)), {
            timeout: 120_000,
            timeoutMsg: "No references lens above Infantry",
        });
        const infantry = lenses.find((l) => l.line === 4)!;
        expect(Number.parseInt(infantry.title, 10)).toBeGreaterThan(0);
        expect(lenses.find((l) => l.line === 17)?.title).toMatch(COUNT);

        // Pressing the lens runs its command with its arguments, in the editor that shows the file.
        await browser.executeWorkbench(async (vscode, target: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
            await vscode.window.showTextDocument(document, { preview: false });
            const all: any[] = (await vscode.commands.executeCommand("vscode.executeCodeLensProvider", document.uri, 1000)) ?? [];
            const lens = all.find((l) => l.range.start.line === 3 && l.command);
            await vscode.commands.executeCommand(lens.command.command, ...(lens.command.arguments ?? []));
        }, file);

        let rows: string[] = [];
        await browser.waitUntil(
            async () =>
                (rows = await browser.execute(() =>
                    Array.from(document.querySelectorAll(".peekview-widget .monaco-list-row")).map((r) =>
                        ((r as HTMLElement).innerText ?? "").replace(/\s+/g, " ").trim(),
                    ),
                )).length > 0,
            { timeout: 30_000, timeoutMsg: "The references peek never opened" },
        );
        expect(rows.join(" | ")).toMatch(/\.xml/i);
        expect(rows.join(" | ")).not.toMatch(/Movementclasstype\.xml/i);
    });
});
