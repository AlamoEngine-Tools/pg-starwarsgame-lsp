// SET-34: the descriptions of the two debug settings say where the lines land - each names its output
// channel by the title the window gives it and the one per project, and the log level's names the log
// file's setting and folder (#462).
import path from "node:path";
import { expect } from "@wdio/globals";
import { client, readJson } from "../paths.js";
import { logChannels, outputChannels, serverReady } from "../protocol.js";

describe("SET-34", () => {
    const nls: Record<string, string> = readJson(path.join(client, "package.nls.json"));
    const logLevel = nls["config.lsp.debug.logLevel.markdownDescription"];
    const trace = nls["config.lsp.debug.trace.server.markdownDescription"];

    before(async () => {
        await serverReady();
    });

    it("names the server's channel as the window titles it, and the one per project", () => {
        const title = [...logChannels().keys()].find((name) => name.startsWith("Alamo Engine Tools"))!;
        expect(logLevel).toContain(`*${title}*`);
        expect(logLevel).toContain("one channel per project, with the project's name in brackets");
    });

    it("names the trace channel as the window titles it, and the one per project", () => {
        const title = [...outputChannels().keys()].find((name) => name.startsWith("EaWEdit LSP Trace"))!;
        expect(trace).toContain(`*${title}*`);
        expect(trace).toContain("one channel per project, with the project's name in brackets");
        expect(trace).not.toContain("output panel");
    });

    it("names the log file's setting and its folder, the same for one project and for several", () => {
        expect(logLevel).toContain("`#aet-eaw-edit.lsp.debug.fileLogLevel#`");
        expect(logLevel).toContain("`~/.aetswg/logs/`");
        expect(logLevel).toContain("with one project open or several");
        expect(logLevel).not.toContain("first workspace folder");
    });
});
