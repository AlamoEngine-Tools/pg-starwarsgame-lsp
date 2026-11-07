// SET-20: troubleshooting - the trace without a restart, Debug log lines, the executable and its
// version check.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { repo } from "../paths.js";
import {
    eaw,
    extensionLog,
    logLine,
    notifications,
    openFile,
    outputChannels,
    restartServer,
    serverReady,
    serverStarts,
    setSettings,
} from "../protocol.js";

const debugServer = path.join(repo, "PG.StarWarsGame.LSP.Server", "bin", "Debug", "net10.0", "PG.StarWarsGame.LSP.Server.exe");

describe("SET-20", () => {
    before(async () => {
        await serverReady();
    });

    it("traces messages from the moment the setting changes", async () => {
        await setSettings({ "aet-eaw-edit.lsp.debug.trace.server": "messages" });
        const before = (outputChannels().get("EaWEdit LSP Trace") ?? "").length;
        await openFile(path.join(eaw, "Data", "XML", "Spaceunitscorvettes.xml"));
        await browser.waitUntil(
            async () => (outputChannels().get("EaWEdit LSP Trace") ?? "").slice(before).includes("textDocument/didOpen"),
            { timeout: 30_000, timeoutMsg: "No didOpen in the trace" },
        );
    });

    // A Debug line is the server's Log message, which the channel writes as [info]; the protocol
    // names the one that says a document reached the sync handler.
    it("logs Debug lines at log level Debug", async () => {
        await setSettings({ "aet-eaw-edit.lsp.debug.logLevel": "Debug" });
        await restartServer();
        await openFile(path.join(eaw, "Data", "XML", "Spaceunitsfighters.xml"));
        await browser.waitUntil(async () => /XmlTextDocumentSyncHandler: didOpen /.test(extensionLog()), {
            timeout: 30_000,
            timeoutMsg: "No Debug line from the sync handler",
        });
    });

    it("restarts on another executable and checks its version", async () => {
        const starts = serverStarts();
        await setSettings({ "aet-eaw-edit.lsp.executable": debugServer });
        await logLine(/Experimental features or executables changed - restarting LSP server\./);
        await browser.waitUntil(async () => serverStarts() > starts, { timeout: 60_000, timeoutMsg: "No restart" });
        await serverReady();
        const reported = (await logLine(/Server version reported: /)).split("Server version reported: ").at(-1)!.trim();
        await browser.pause(3_000);
        const warned = (await notifications()).some((n) => n.message.startsWith(`EaWEdit: Server version ${reported} does not match`));
        expect(warned).toBe(reported !== "0.4.1");
    });

    after(async () => {
        await setSettings({
            "aet-eaw-edit.lsp.debug.trace.server": undefined,
            "aet-eaw-edit.lsp.debug.logLevel": undefined,
            "aet-eaw-edit.lsp.executable": undefined,
        });
    });
});
