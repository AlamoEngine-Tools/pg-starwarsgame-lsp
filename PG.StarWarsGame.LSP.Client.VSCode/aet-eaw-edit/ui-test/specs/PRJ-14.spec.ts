// PRJ-14: adding or removing a folder or a project file starts or stops only the servers concerned. A
// project file appearing in the same moment as a restart setting changes runs in one queue with the restart
// (#395): every project ends with one server, started once, and none is left stopped.
import fs from "node:fs";
import { execFileSync } from "node:child_process";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { logChannels, lspStatus, outputChannels, projectsReady, setSettings } from "../protocol.js";

const gamma = "C:\\temp\\aet-test-PRJ-multi\\gamma";
const gammaProject = path.join(gamma, "gamma.pgproj");

function linesSince(start: number): string[] {
    return (outputChannels().get("EaWEdit") ?? "").slice(start).split(/\r?\n/);
}

/** The servers running for this test's projects: their command lines name its folder. */
function serversRunning(): number {
    const out = execFileSync(
        "pwsh",
        [
            "-NoProfile",
            "-Command",
            "@(Get-CimInstance Win32_Process -Filter \"Name = 'PG.StarWarsGame.LSP.Server.exe'\" | Where-Object { $_.CommandLine -like '*aet-test-PRJ-multi*' }).Count",
        ],
        { encoding: "utf8" },
    );
    return Number.parseInt(out.trim(), 10);
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

    it("takes a project file and a restart setting arriving together one after the other", async () => {
        await browser.executeWorkbench((vscode, folder: string) => {
            vscode.workspace.updateWorkspaceFolders(vscode.workspace.workspaceFolders.length, 0, { uri: vscode.Uri.file(folder) });
        }, gamma);
        const seen = linesSince(start).filter((l) => l.includes("folders without a project")).length;
        await browser.waitUntil(
            async () => linesSince(start).filter((l) => /Projects: Alpha, Beta; folders without a project: .*gamma/i.test(l)).length > seen,
            { timeout: 30_000, timeoutMsg: "The folder was not discovered again" },
        );
        const from = (outputChannels().get("EaWEdit") ?? "").length;

        // Not awaited apart: the project file and the setting land in the same moment.
        fs.writeFileSync(
            gammaProject,
            JSON.stringify({ _type: "aetswg.ModProject", _typeVersion: "aetswg-1.1.0", game: "foc", name: "Gamma", directories: { xml: ["data/xml"] }, projectReferences: [] }),
        );
        await setSettings({ "aet-eaw-edit.lsp.localisation.language": "GERMAN" });

        await browser.waitUntil(
            async () => linesSince(from).some((l) => l.includes("Server startup settings changed - restarting LSP server.")),
            { timeout: 30_000, timeoutMsg: "The setting restarted nothing" },
        );
        await projectsReady(3);
        await browser.pause(10_000);
        expect(await lspStatus()).toBe("$(check) EaWEdit LSP: 3 projects ready");

        // No server started while the restart was still stopping the old ones: every start after
        // the restart line comes after the line that says the stop is over.
        const lines = linesSince(from);
        const restartAt = lines.findIndex((l) => l.includes("Server startup settings changed - restarting LSP server."));
        const stoppedAt = lines.findIndex((l, i) => i > restartAt && l.includes("All LSP servers stopped."));
        expect(stoppedAt).toBeGreaterThan(restartAt);
        const early = lines.filter((l, i) => i > restartAt && i < stoppedAt && l.includes("Starting LSP server"));
        expect(early).toEqual([]);

        // One server per project, and nothing left over from a stop that overlapped a start.
        let servers = 0;
        await browser
            .waitUntil(async () => (servers = serversRunning()) === 3, { timeout: 30_000, interval: 2_000 })
            .catch(() => undefined);
        expect(servers).toBe(3);
    });
});
