// SET-33: the unlisted lsp.debug.fileLogLevel setting has a release server write its log file, into the
// workspace's folder in the user's own log store, and neither a channel nor the file names the home
// directory: they say ~ (#461).
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { logChannels, outputChannels, serverReady } from "../protocol.js";

const SERVER = "Alamo Engine Tools - Empire at War Edit";

/**
 * The lines of a log file written since this window's server was started: the day's file also holds what
 * earlier runs of the day wrote. A line without a time of its own belongs to the one above it.
 */
function writtenSince(text: string, since: number): string {
    const kept: string[] = [];
    let keep = false;
    for (const line of text.split(/\r?\n/)) {
        const stamp = /^(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2}\.\d{3}) ([+-]\d{2}:\d{2}) /.exec(line);
        if (stamp) {
            keep = new Date(`${stamp[1]}T${stamp[2]}${stamp[3]}`).getTime() >= since;
        }
        if (keep) {
            kept.push(line);
        }
    }
    return kept.join("\n");
}

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

    it("names the home directory in no line of a channel, and says ~ where the server names its log", async () => {
        let server = "";
        await browser.waitUntil(
            () => {
                server = logChannels().get(SERVER) ?? "";
                return /\[LSP\] PID \d+ args=/.test(server) && server.includes("[LSP] Logging to ");
            },
            { timeout: 30_000, timeoutMsg: "The server's start lines did not reach its channel" },
        );
        const home = os.homedir().toLowerCase();
        expect((outputChannels().get("EaWEdit") ?? "").toLowerCase()).not.toContain(home);
        for (const text of logChannels().values()) {
            expect(text.toLowerCase()).not.toContain(home);
        }
        expect(server).toContain(`[LSP] Logging to ~${path.sep}.aetswg${path.sep}logs${path.sep}`);
    });

    it("writes the day's file for the project, Debug lines included and the home directory as ~", async () => {
        const folder = start.split("--log-dir=")[1].split(" --log-stem=")[0].replace(/^~/, os.homedir());
        const today = () => (fs.existsSync(folder) ? fs.readdirSync(folder) : []).filter((f) => /^aetswg-.+-\d{8}(_\d+)?\.log$/.test(f));
        await browser.waitUntil(() => today().length > 0, { timeout: 30_000, timeoutMsg: `No log file in ${folder}` });
        // Serilog holds the file open for writing and shares it for reading. The channel stamps its lines in
        // UTC; two seconds of slack cover the server's clock reading before the line was written.
        const startedAt = new Date(`${start.slice(1, 24).replace(" ", "T")}Z`).getTime() - 2_000;
        const text = writtenSince(today().map((f) => fs.readFileSync(path.join(folder, f), "utf8")).join("\n"), startedAt);
        expect(text).toContain("[DBG]");
        expect(text.toLowerCase()).not.toContain(os.homedir().toLowerCase().replace(/\\/g, "/"));
        expect(text.toLowerCase()).not.toContain(os.homedir().toLowerCase());
    });
});
