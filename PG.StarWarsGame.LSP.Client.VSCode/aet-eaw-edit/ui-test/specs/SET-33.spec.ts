// SET-33: the unlisted lsp.debug.fileLogLevel setting has a release server write its log file, into the
// workspace's folder in the user's own log store, and the lines that name that folder say ~ for the home
// directory (#461).
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { logChannels, outputChannels, serverReady } from "../protocol.js";

const SERVER = "Alamo Engine Tools - Empire at War Edit";

describe("SET-33", () => {
    let start = "";

    before(async () => {
        await serverReady();
        start = (outputChannels().get("EaWEdit") ?? "").split(/\r?\n/).find((l) => l.includes("Starting LSP server:"))!;
    });

    it("starts the server on the workspace's log folder, at the level the setting names", () => {
        expect(start).toContain(`--log-dir=~${path.sep}.aetswg${path.sep}logs${path.sep}`);
        expect(start).toContain("--log-file-level=Debug");
    });

    it("names the home directory in no line of the extension's channel", () => {
        expect((outputChannels().get("EaWEdit") ?? "").toLowerCase()).not.toContain(os.homedir().toLowerCase());
    });

    it("says ~ in the server's own start lines", async () => {
        let lines: string[] = [];
        await browser.waitUntil(
            () => {
                lines = (logChannels().get(SERVER) ?? "").split(/\r?\n/).filter((l) => /\[LSP\] (PID \d+ args=|Logging to )/.test(l));
                return lines.length >= 2;
            },
            { timeout: 30_000, timeoutMsg: "The server's start lines did not reach its channel" },
        );
        for (const line of lines) {
            expect(line.toLowerCase()).not.toContain(os.homedir().toLowerCase());
        }
        expect(lines.find((l) => l.includes("Logging to "))).toContain(`~${path.sep}.aetswg${path.sep}logs${path.sep}`);
    });

    it("writes the day's file for the project, Debug lines included", async () => {
        const folder = start.split("--log-dir=")[1].split(" --log-stem=")[0].replace(/^~/, os.homedir());
        const today = () => (fs.existsSync(folder) ? fs.readdirSync(folder) : []).filter((f) => /^aetswg-.+-\d{8}(_\d+)?\.log$/.test(f));
        await browser.waitUntil(() => today().length > 0, { timeout: 30_000, timeoutMsg: `No log file in ${folder}` });
        // Serilog holds the file open for writing and shares it for reading.
        const text = today()
            .map((f) => fs.readFileSync(path.join(folder, f), "utf8"))
            .join("");
        expect(text).toContain("[DBG]");
    });
});
