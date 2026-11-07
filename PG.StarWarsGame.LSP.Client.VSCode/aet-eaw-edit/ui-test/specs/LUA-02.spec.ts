// LUA-02: aet-eaw-edit.lua.analyzerPath runs another analyzer and restarts the server; a missing one is an error
// and starts no server; clearing it starts the bundled one again.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { client } from "../paths.js";
import { logLine, notification, serverReady, serverStarts, setSettings, settingsFound } from "../protocol.js";
import { hoverWhen, script } from "./luaEditor.js";

const KEY = "aet-eaw-edit.lua.analyzerPath";
const SERVER = "Alamo Engine Tools - Empire at War Edit";
const bundled = path.join(client, "server", "emmylua", "emmylua_ls.exe");
const interdictor = script("Gameobject", "Interdictor.lua");

async function restartsOn(value: string): Promise<void> {
    const starts = serverStarts();
    await setSettings({ [KEY]: value });
    await logLine(/Experimental features or executables changed - restarting LSP server\./);
    await browser.waitUntil(async () => serverStarts() > starts, { timeout: 60_000, timeoutMsg: "No restart" });
}

describe("LUA-02", () => {
    before(async () => {
        await serverReady();
    });

    it("is a development setting with its description", async () => {
        expect(await settingsFound("analyzerPath")).toBe(0);
        expect(await settingsFound("@tag:advanced analyzerPath")).toBe(1);
        const manifest = JSON.parse(fs.readFileSync(path.join(client, "package.json"), "utf8"));
        const nls = JSON.parse(fs.readFileSync(path.join(client, "package.nls.json"), "utf8"));
        const declared = manifest.contributes.configuration.find((c: any) => c.properties?.[KEY]);
        const description = declared.properties[KEY].markdownDescription ?? declared.properties[KEY].description;
        // Read as the Settings editor renders it: its markdown's code spans and paragraph break as plain text.
        const shown = (nls[description.slice(1, -1)] ?? description).replace(/`/g, "").replace(/\s+/g, " ");
        expect(shown).toBe(
            "Path to an emmylua_ls executable to run instead of the Lua analyzer bundled with the extension. Empty, the bundled analyzer runs. A change restarts the server.",
        );
    });

    it("restarts on another analyzer and starts it", async () => {
        await restartsOn(bundled);
        await serverReady();
        await hoverWhen(interdictor, { line: 112, text: "FindDeadlyEnemy" }, (c) => c.join("\n").includes("---"));
        expect(await logLine(/Lua analyzer started \(pid \d+\) for /, SERVER)).toBeTruthy();
    });

    it("refuses a missing analyzer and starts again once cleared", async () => {
        await setSettings({ [KEY]: "C:\\nope\\emmylua_ls.exe" });
        await notification(
            (m) =>
                m ===
                'EaWEdit: Lua analyzer not found - C:\\nope\\emmylua_ls.exe. Fix "aet-eaw-edit.lua.analyzerPath", or clear it to use the bundled one',
        );
        await restartsOn("");
        await serverReady();
    });
});
