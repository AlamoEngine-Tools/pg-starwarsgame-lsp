// PRJ-01: a single project behaves as before - one server, no project level, diagnostics name the
// project. The server's file log is written by a Debug server only and the staged server is Release,
// so that check stays a hand check.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { readJson, repo } from "../paths.js";
import { logChannels, lspStatus, notifications, outputChannels, serverReady } from "../protocol.js";

const eaw = path.join(repo, "eaw");
const capital = path.join(eaw, "Data", "Xml", "Spaceunitscapital.xml");

describe("PRJ-01", () => {
    before(async () => {
        await serverReady();
    });

    it("shows one server in the status bar, with no project count", async () => {
        expect(await lspStatus()).toBe("$(check) EaWEdit LSP");
    });

    it("answers Select Project with one project open and no picker", async () => {
        await browser.executeWorkbench((vscode) => {
            void vscode.commands.executeCommand("aet-eaw-edit.lsp.selectProject");
        });
        await browser.waitUntil(
            async () => (await notifications()).some((n) => n.message === "EaWEdit: One project open"),
            { timeout: 15_000, timeoutMsg: "No 'EaWEdit: One project open' message" },
        );
        const picker = await browser.$(".quick-input-widget");
        expect((await picker.isExisting()) && (await picker.isDisplayed())).toBe(false);
    });

    it("names its channels without a project, and has one of each", () => {
        expect([...logChannels().keys()]).toEqual(["Alamo Engine Tools - Empire at War Edit"]);
        const output = outputChannels();
        expect([...output.keys()].sort()).toEqual(["EaWEdit", "EaWEdit LSP Trace"]);
        // The server's capabilities in the initialize result; "supported": false is left out, which
        // the protocol reads as false.
        expect(output.get("EaWEdit LSP Trace")).toMatch(/"workspaceFolders": \{\s*"changeNotifications": false\s*\}/);
        expect(output.get("EaWEdit")!.split(/\r?\n/).filter((l) => /^\[[^\]]+\] Projects:/.test(l))).toEqual([]);
    });

    it("names the diagnostic's project and no layer below it", async () => {
        const above = await browser.executeWorkbench(async (vscode, file: string) => {
            const uri = vscode.Uri.file(file);
            const document = await vscode.workspace.openTextDocument(uri);
            await vscode.window.showTextDocument(document);
            const edit = new vscode.WorkspaceEdit();
            edit.insert(uri, new vscode.Position(392, 0), "<Zzq_Not_A_Tag>1</Zzq_Not_A_Tag>\n");
            await vscode.workspace.applyEdit(edit);
            return document.lineAt(391).text as string;
        }, capital);
        expect(above).toContain('<SpaceUnit Name="Star_Destroyer">');

        type Found = { code: string; message: string; source: string; line: number; severity: number; related: string[] };
        let unknown: Found | undefined;
        await browser.waitUntil(
            async () => {
                const all: Found[] = await browser.executeWorkbench(
                    (vscode, file: string) =>
                        vscode.languages.getDiagnostics(vscode.Uri.file(file)).map((d: any) => ({
                            code: String(typeof d.code === "object" ? d.code.value : d.code),
                            message: d.message,
                            source: d.source,
                            line: d.range.start.line,
                            severity: d.severity,
                            related: (d.relatedInformation ?? []).map((r: any) => r.message),
                        })),
                    capital,
                );
                unknown = all.find((d) => d.line === 392 && d.code === "aetswg-006-0012");
                return unknown !== undefined;
            },
            { timeout: 60_000, timeoutMsg: "No aetswg-006-0012 on the inserted line" },
        );

        try {
            expect(unknown!.severity).toBe(1);
            expect(unknown!.message.startsWith("<Zzq_Not_A_Tag> is not a known SpaceUnit tag.")).toBe(true);
            expect(unknown!.source).toBe("aetswg (Empire at War - Vanilla)");
            expect(unknown!.related.filter((r) => r.startsWith("Layer - "))).toEqual([]);
        } finally {
            await browser.executeWorkbench(async (vscode) => {
                await vscode.commands.executeCommand("workbench.action.files.revert");
                await vscode.commands.executeCommand("workbench.action.closeActiveEditor");
            });
        }
    });

    it("lists the campaigns with no row for the project", async () => {
        const project = fs.readdirSync(eaw).find((f) => f.toLowerCase().endsWith(".pgproj"))!;
        const name: string = readJson(path.join(eaw, project)).name;

        const workbench = await browser.getWorkbench();
        const container = await workbench.getActivityBar().getViewControl("Empire at War Edit");
        expect(container).toBeDefined();
        const view = await container!.openView();
        const section = await view.getContent().getSection("Campaign Editor");
        await section.expand();
        let labels: string[] = [];
        await browser.waitUntil(
            async () => {
                const items = await section.getVisibleItems();
                labels = await Promise.all(items.map((i: any) => i.getLabel()));
                return labels.length > 0;
            },
            { timeout: 60_000, timeoutMsg: "The Campaign Editor lists nothing" },
        );
        expect(labels).not.toContain(name);
    });
});
