// SET-14: every user-facing text comes from a catalogue - titles, messages, a diagnostic and its fix,
// the story graph's tooltips.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { client, readJson } from "../paths.js";
import { command, eaw, notification, serverReady } from "../protocol.js";
import { closeAll, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const corvettes = path.join(eaw, "Data", "XML", "Spaceunitscorvettes.xml");

describe("SET-14", () => {
    before(async () => {
        await serverReady();
    });

    // The manifest has 37 commands: 32 titled 'EaWEdit: ...', and five entries of the Campaign
    // Editor's nodes, which are named for what they do to the node - the two 'Open File' and the
    // three 'Rename Set' (#181).
    it("titles the 32 commands 'EaWEdit: ...' and leaves no placeholder", () => {
        const manifest = readJson(path.join(client, "package.json"));
        const nls: Record<string, string> = readJson(path.join(client, "package.nls.json"));
        const titles: string[] = manifest.contributes.commands.map((c: any) => nls[c.title.slice(1, -1)] ?? c.title);
        expect(titles.filter((t) => t.includes("%"))).toEqual([]);
        const ofANode = (t: string): boolean => t.startsWith("Open File") || t.startsWith("Rename Set");
        const own = titles.filter((t) => !ofANode(t));
        expect(own).toHaveLength(32);
        expect(own.filter((t) => !t.startsWith("EaWEdit: "))).toEqual([]);
        expect(titles.filter(ofANode).sort()).toEqual([
            "Open File",
            "Open File - no document in the project",
            "Rename Set - no Campaign_Set to rename",
            "Rename Set - no member in the project",
            "Rename Set...",
        ]);
        expect(fs.readFileSync(path.join(client, "package.json"), "utf8")).not.toMatch(/"%[^"%]*[^%"]"/);
    });

    it("answers Select Project with one project open", async () => {
        await command("aet-eaw-edit.lsp.selectProject");
        await notification((m) => m === "EaWEdit: One project open", 15_000);
    });

    it("words the float-for-integer warning and its fix", async () => {
        const above = await browser.executeWorkbench(async (vscode, file: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
            await vscode.window.showTextDocument(document);
            const line = document.lineAt(161);
            const at = line.text.indexOf(">2<") + 1;
            const edit = new vscode.WorkspaceEdit();
            edit.replace(document.uri, new vscode.Range(161, at, 161, at + 1), "2.5");
            await vscode.workspace.applyEdit(edit);
            return line.text;
        }, corvettes);
        expect(above.trim()).toBe("<Population_Value>2</Population_Value>");

        type Seen = { message: string; fixes: string[] };
        let seen: Seen | undefined;
        await browser.waitUntil(
            async () => {
                seen = await browser.executeWorkbench(async (vscode, file: string) => {
                    const uri = vscode.Uri.file(file);
                    const d = vscode.languages
                        .getDiagnostics(uri)
                        .find((d: any) => (typeof d.code === "object" ? d.code.value : d.code) === "aetswg-003-0048");
                    if (!d) return undefined;
                    const actions = await vscode.commands.executeCommand("vscode.executeCodeActionProvider", uri, d.range);
                    return { message: d.message, fixes: (actions ?? []).map((a: any) => a.title) };
                }, corvettes);
                return seen !== undefined;
            },
            { timeout: 60_000, interval: 2000, timeoutMsg: "No aetswg-003-0048 on the edited value" },
        );
        try {
            expect(seen!.message).toBe("'2.5' is a float but <Population_Value> expects an integer. 2 is the nearest valid value.");
            expect(seen!.fixes).toContain("Replace with '2'");
        } finally {
            await browser.executeWorkbench(async (vscode) => {
                await vscode.commands.executeCommand("workbench.action.files.revert");
            });
            await closeAll();
        }
    });

    it("words the story graph's tooltips without a placeholder", async () => {
        await openStoryGraph("Full_Story_Campaign_Empire", "Empire");
        const titles = await inNewestWebview(async () => {
            await (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 });
            return browser.execute(() =>
                Array.from(document.querySelectorAll("[title]")).map((e) => e.getAttribute("title") ?? ""),
            );
        });
        expect(titles.filter((t) => /\{\d\}|%\w/.test(t))).toEqual([]);
        expect(titles).toContain("Validate - check the story for problems (opens the panel below)");
    });
});
