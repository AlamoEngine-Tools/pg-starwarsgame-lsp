// LUA-34: the win32-x64 VSIX ships the server and the Lua analyzer. The setup packaged it; the window runs it
// installed into a clean profile, without the extension under development.
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { repo } from "../paths.js";
import { logChannels, logLine, notifications, serverReady } from "../protocol.js";
import { codeLenses } from "./xmlEditor.js";
import { hoverWhen, script } from "./luaEditor.js";

const vsix = path.join("C:\\temp\\aet-vscode", "LUA-34-vsix", "aet-eaw-edit.vsix");
const SERVER = "Alamo Engine Tools - Empire at War Edit";

describe("LUA-34", () => {
    it("packs the bundled server, analyzer and notices", () => {
        const out = execFileSync(
            "pwsh",
            ["-NoProfile", "-File", path.join(repo, "scripts", "check-vsix.ps1"), vsix],
            { encoding: "utf8" },
        );
        expect(out).toContain(`VSIX carries the bundled server, analyzer and notices: ${vsix}`);
    });

    it("runs from the installed package, not the development folder", async () => {
        const at: string | undefined = await browser.executeWorkbench(
            (vscode) => vscode.extensions.getExtension("AlamoEngineTools.aet-eaw-edit")?.extensionPath,
        );
        expect(at?.toLowerCase()).toContain(path.join("LUA-34-ui", "extensions").toLowerCase());
        const notices = fs.readFileSync(path.join(at!, "ThirdPartyNotices.txt"), "utf8");
        expect(notices).toContain("EmmyLua Analyzer (emmylua_ls) 0.25.1, unmodified.");
    });

    it("starts its own server and analyzer with no download prompt", async () => {
        await serverReady();
        const interdictor = script("Gameobject", "Interdictor.lua");
        const cards = await hoverWhen(interdictor, { line: 112, text: "FindDeadlyEnemy" }, (c) => c.join("\n").includes("---"));
        expect(cards.join("\n")).toContain("FindDeadlyEnemy");
        expect((await codeLenses(interdictor)).length).toBeGreaterThan(0);
        expect(await logLine(/Lua analyzer started \(pid \d+\) for /, SERVER)).toMatch(/for .*[\\/]eaw[\\/]Data[\\/]Scripts/i);
        // The VSIX carries both: nothing offers to download a server or an analyzer.
        expect((await notifications()).filter((n) => /download/i.test(JSON.stringify(n)))).toEqual([]);
        expect([...logChannels().values()].join("\n")).not.toMatch(/download/i);
    });
});
