// SET-01: on a clean profile the bundled server starts on a .pgproj workspace, with no file opened.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { client } from "../paths.js";
import { notifications, outputChannels, serverReady } from "../protocol.js";

describe("SET-01", () => {
    it("runs on VS Code 1.107 or later", async () => {
        const version: string = await browser.executeWorkbench((vscode) => vscode.version);
        const [major, minor] = version.split(".").map(Number);
        expect(major > 1 || minor >= 107).toBe(true);
    });

    it("starts the bundled server with no file opened", async () => {
        await serverReady();
        const log = outputChannels().get("EaWEdit")!;
        expect(log).toContain("Extension activated.");
        expect(log).toContain("  lsp.executable   = (bundled)");
        const start = log.split(/\r?\n/).find((l) => l.includes("Starting LSP server: Bundled: "))!;
        expect(start.toLowerCase()).toContain(
            `bundled: ${path.join(client, "server", "PG.StarWarsGame.LSP.Server.exe")} --log-dir=`.toLowerCase(),
        );
        expect(start.toLowerCase()).toContain("pg.starwarsgame.lsp\\eaw");
        expect(log).toContain("Workspace scan complete.");
    });

    it("shows no error notification", async () => {
        expect((await notifications()).filter((n) => n.type === "error")).toEqual([]);
    });
});
