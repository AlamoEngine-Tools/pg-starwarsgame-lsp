// SET-15: the commands smoke test.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import {
    command,
    copyBugReport,
    eaw,
    extensionLog,
    recordStatus,
    statusHistory,
    viewItems,
    notification,
    notifications,
    serverReady,
    serverStarts,
} from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

describe("SET-15", () => {
    before(async () => {
        await serverReady();
    });

    it("re-validates the workspace with no error", async () => {
        await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("aet-eaw-edit.lsp.revalidateWorkspace"));
        await browser.pause(3_000);
        expect((await notifications()).filter((n) => n.type === "error")).toEqual([]);
    });

    it("refreshes the story and localisation views silently", async () => {
        await browser.executeWorkbench(async (vscode) => {
            await vscode.commands.executeCommand("aet-eaw-edit.lsp.refreshStoryNavigator");
            await vscode.commands.executeCommand("aet-eaw-edit.lsp.refreshLocalisationNavigator");
        });
        expect((await notifications()).filter((n) => n.type === "error")).toEqual([]);
    });

    it("opens the effective object as a read-only document", async () => {
        const corvettes = path.join(eaw, "Data", "XML", "Spaceunitscorvettes.xml");
        // The command takes the object as its argument, as the editor's context menu gives it.
        await browser.executeWorkbench(async (vscode, file: string) => {
            await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(vscode.Uri.file(file)));
            void vscode.commands.executeCommand("aet-eaw-edit.lsp.showEffectiveObject", "Tartan_Patrol_Cruiser_Easy");
        }, corvettes);
        let generated: string | undefined;
        await browser.waitUntil(
            async () => {
                // Short: a whole XML file per editor per poll closes the bridge's socket.
                const opened: string[] = await browser.executeWorkbench((vscode) =>
                    vscode.window.visibleTextEditors.map(
                        (e: any) => `${e.document.uri.scheme}:${e.document.getText().slice(0, 2000)}`,
                    ),
                );
                generated = opened.find((t) => !t.startsWith("file:"));
                return generated !== undefined;
            },
            { timeout: 60_000, interval: 1000, timeoutMsg: "No generated document opened" },
        );
        expect(generated).toContain("Tartan_Patrol_Cruiser_Easy");
        await closeAll();
    });

    // The panel opens on the Game Data outline, its first list the game objects; the four outlines
    // are the Mod Browser view's.
    it("opens the Mod Browser on the game objects, the view listing its four outlines", async () => {
        await command("aet-eaw-edit.lsp.openModBrowser");
        const text = await inNewestWebview(async () => {
            await browser.waitUntil(
                async () => ((await browser.execute(() => document.body.innerText)) as string).includes("Game Objects"),
                { timeout: 60_000 },
            );
            return browser.execute(() => document.body.innerText);
        });
        expect(text).toContain("Game Objects");
        expect(await viewItems("Mod Browser")).toEqual(expect.arrayContaining(["Game Data", "Assets", "Scripts", "Schema"]));
        await closeAll();
    });

    it("starts the server again on Force LSP Startup", async () => {
        const starts = serverStarts();
        await command("aet-eaw-edit.lsp.debug.forceStartup");
        await browser.waitUntil(async () => serverStarts() > starts, { timeout: 60_000, timeoutMsg: "No restart" });
        await serverReady();
    });

    it("copies the extended bug report", async () => {
        const report = await copyBugReport("Basic + extended stats");
        await notification((m) => m === "EaWEdit: Bug report info copied", 15_000);
        expect(report).toContain("**Extended stats**");
        for (const line of ["- Dependencies:", "- Files:", "- Symbols:"]) expect(report).toContain(line);
    });

    it("re-indexes on Reload Mod Project", async () => {
        await recordStatus();
        await command("aet-eaw-edit.lsp.reloadProject");
        await browser.waitUntil(
            async () => {
                const seen = await statusHistory();
                const indexing = seen.indexOf("EaWEdit LSP: Indexing workspace...");
                return indexing >= 0 && seen.slice(indexing + 1).includes("EaWEdit LSP");
            },
            { timeout: 60_000, timeoutMsg: "The status did not go through Indexing workspace... and back" },
        );
        await serverReady();
    });
});
