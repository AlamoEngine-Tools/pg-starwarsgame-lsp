// LUA-29: the Lua Scripts view is there and empty without a debug session, its actions disabled.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { client } from "../paths.js";
import { serverReady } from "../protocol.js";

const COMMANDS = [
    "EaWEdit: Refresh Lua Scripts",
    "EaWEdit: Refresh Script Threads",
    "EaWEdit: Break in Script",
    "EaWEdit: Break in Thread",
];

describe("LUA-29", () => {
    before(async () => {
        await serverReady();
    });

    it("shows the view empty, its commands enabled only in a session", async () => {
        const manifest = JSON.parse(fs.readFileSync(path.join(client, "package.json"), "utf8"));
        const nls = JSON.parse(fs.readFileSync(path.join(client, "package.nls.json"), "utf8"));
        const title = (t: string): string => (t.startsWith("%") ? nls[t.slice(1, -1)] : t);
        const commands = manifest.contributes.commands.filter((c: any) => COMMANDS.includes(title(c.title)));
        expect(commands.map((c: any) => title(c.title)).sort()).toEqual([...COMMANDS].sort());
        for (const c of commands) expect(c.enablement).toContain("aet-eaw-edit.luaDebug.sessionActive");

        await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("workbench.view.debug"));
        const workbench = await browser.getWorkbench();
        const content = (await workbench.getSideBar()).getContent();
        const section = await content.getSection("Lua Scripts");
        await section.expand();
        await browser.pause(1_000);
        // Empty of scripts: its one row says why.
        const placeholder = await Promise.all((await section.getVisibleItems()).map((i: any) => i.getLabel()));
        expect(placeholder).toEqual(["No Lua debug session. Start one from Run and Debug (Attach to Empire at War)"]);

        await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("workbench.action.showCommands"));
        const input = await browser.$(".quick-input-widget .quick-input-box input");
        await input.waitForDisplayed({ timeout: 10_000 });
        await browser.keys("EaWEdit: Refresh Lua Scripts".split(""));
        await browser.pause(1_000);
        const rows: string[] = await browser.execute(() =>
            Array.from(document.querySelectorAll(".quick-input-widget .monaco-list-row")).map((r) => (r.textContent ?? "").trim()),
        );
        expect(rows.filter((r) => r.includes("Refresh Lua Scripts"))).toEqual([]);
        await browser.keys("Escape");
    });
});
