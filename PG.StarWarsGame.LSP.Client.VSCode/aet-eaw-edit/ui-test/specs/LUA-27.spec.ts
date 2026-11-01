// LUA-27: the Lua debugger's refusals that need no game - no program, a mod path with a space, a game exiting
// before the attach, an attach nobody answers. The launch.json is the fixture's; it is removed afterwards.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { clearNotifications, eaw, notifications, serverReady } from "../protocol.js";

const launchJson = path.join(eaw, ".vscode", "launch.json");

/** The errors an earlier start showed: a refusal waits for one it has not seen. */
const shown = new Set<string>();

/** Starts a launch configuration by name; returns the new errors the window then shows, toast or dialog. */
async function refusal(name: string, wait = 8_000): Promise<string[]> {
    await clearNotifications();
    await browser.executeWorkbench((vscode, config: string) => {
        void vscode.debug.startDebugging(vscode.workspace.workspaceFolders![0], config);
    }, name);
    const seen: string[] = [];
    await browser.waitUntil(
        async () => {
            const dialog: string = await browser.execute(
                () => (document.querySelector(".monaco-dialog-box .dialog-message-text")?.textContent ?? "").trim(),
            );
            if (dialog) {
                seen.push(dialog);
                await browser.keys("Escape");
            }
            seen.push(...(await notifications()).filter((n) => n.type === "error").map((n) => n.message));
            return seen.some((m) => !shown.has(m));
        },
        { timeout: wait, interval: 1_000 },
    ).catch(() => undefined);
    await browser.executeWorkbench((vscode) => vscode.debug.stopDebugging());
    const fresh = seen.filter((m) => !shown.has(m));
    for (const m of seen) shown.add(m);
    return fresh;
}

describe("LUA-27", () => {
    before(async () => {
        await serverReady();
    });

    it("refuses a launch with no program", async () => {
        expect((await refusal("Aet no program")).join("\n")).toContain(
            'No game executable is configured. Set "program" in the launch configuration to the debug build of the game',
        );
    });

    it("refuses a mod path with a space", async () => {
        expect((await refusal("Aet space")).join("\n")).toContain(
            "The mod path 'C:\\My Mods\\X' contains a space; the game's command line cannot carry a path with one",
        );
    });

    it("fails when the game exits before the attach", async () => {
        expect((await refusal("Aet early exit", 20_000)).join("\n")).toMatch(/The game exited with code -?\d+ before the debugger could attach/);
    });

    it("fails an attach no game answers", async () => {
        expect((await refusal("Aet attach", 30_000)).join("\n")).toContain(
            "The game at 127.0.0.1:1234 did not complete the debugger handshake within 10 s. Is a debug build running with its Lua debug server started (console command luadebug)?",
        );
    });

    after(() => fs.rmSync(launchJson, { force: true }));
});
