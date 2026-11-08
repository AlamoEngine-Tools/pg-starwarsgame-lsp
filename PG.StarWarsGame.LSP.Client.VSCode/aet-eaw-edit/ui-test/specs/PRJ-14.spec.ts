// PRJ-14: adding or removing a folder or a project file starts or stops only the servers concerned.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { logChannels, lspStatus, outputChannels, projectsReady } from "../protocol.js";

const gamma = "C:\\temp\\aet-test-PRJ-multi\\gamma";
const gammaProject = path.join(gamma, "gamma.pgproj");

function linesSince(start: number): string[] {
    return (outputChannels().get("EaWEdit") ?? "").slice(start).split(/\r?\n/);
}

describe("PRJ-14", () => {
    let start = 0;

    before(async () => {
        await projectsReady(2);
        fs.rmSync(gamma, { recursive: true, force: true });
        fs.mkdirSync(path.join(gamma, "Data", "XML"), { recursive: true });
        start = (outputChannels().get("EaWEdit") ?? "").length;
    });

    it("starts nothing for a folder without a project", async () => {
        await browser.executeWorkbench((vscode, folder: string) => {
            vscode.workspace.updateWorkspaceFolders(vscode.workspace.workspaceFolders.length, 0, { uri: vscode.Uri.file(folder) });
        }, gamma);
        await browser.waitUntil(async () => linesSince(start).some((l) => /Projects: Alpha, Beta; folders without a project: .*gamma/i.test(l)), {
            timeout: 30_000,
            timeoutMsg: "No 'folders without a project' line",
        });
        expect(linesSince(start).filter((l) => l.includes("Starting LSP server"))).toEqual([]);
    });

    it("starts Gamma alone once its project file appears", async () => {
        fs.writeFileSync(
            gammaProject,
            JSON.stringify({ _type: "aetswg.ModProject", _typeVersion: "aetswg-1.1.0", game: "foc", name: "Gamma", directories: { xml: ["data/xml"] }, projectReferences: [] }),
        );
        await projectsReady(3);
        const starts = linesSince(start).filter((l) => l.includes("Starting LSP server"));
        expect(starts.filter((l) => l.includes("[Gamma] Starting LSP server:"))).toHaveLength(1);
        expect(starts.filter((l) => l.includes("[Alpha] Starting") || l.includes("[Beta] Starting"))).toEqual([]);
        expect([...logChannels().keys()]).toContain("Alamo Engine Tools - Empire at War Edit (Gamma)");
    });

    it("stops Gamma alone once its project file goes", async () => {
        fs.rmSync(gammaProject);
        await projectsReady(2);
        expect(linesSince(start).filter((l) => l.includes("[Alpha] Starting") || l.includes("[Beta] Starting"))).toEqual([]);
    });

    it("starts and stops nothing when the folder goes", async () => {
        const before = linesSince(start).filter((l) => l.includes("Starting LSP server")).length;
        await browser.executeWorkbench((vscode, folder: string) => {
            const index = vscode.workspace.workspaceFolders.findIndex((f: any) => f.uri.fsPath.toLowerCase() === folder.toLowerCase());
            vscode.workspace.updateWorkspaceFolders(index, 1);
        }, gamma);
        await browser.pause(5_000);
        expect(linesSince(start).filter((l) => l.includes("Starting LSP server")).length).toBe(before);
        expect(await lspStatus()).toBe("$(check) EaWEdit LSP: 2 projects ready");
    });
});
