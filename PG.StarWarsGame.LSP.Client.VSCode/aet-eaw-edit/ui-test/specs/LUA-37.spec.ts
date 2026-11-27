// LUA-37: an analyzer completion item resolves in a script the server writes annotations into (#489).
// Two faults, both seen here: a keyword the analyzer offers carries null as its data, and resolving it in
// such a script failed with an internal error; and on about half the starts the server advertised the XML
// handler's completion options, without a resolve, so no item was resolved at all. Raidplan.lua declares a
// task force, so the server annotates it. The suggest list resolves the item it focuses, which is how a
// reader meets the resolve. The keyword is `repeat`, which the script never writes, so the list focuses the
// analyzer's item rather than a word of the script.
import { browser, expect } from "@wdio/globals";
import { logChannels, outputChannels, serverReady } from "../protocol.js";
import { completions, hoverWhen, insertLine, revert, script } from "./luaEditor.js";

const file = script("AI", "Raidplan.lua");

function resolveFailures(): string[] {
    return [...logChannels().values()]
        .flatMap((text) => text.split(/\r?\n/))
        .filter((line) => line.includes("completionItem/resolve failed"));
}

// The resolve requests the trace shows: sent, and answered.
function resolves(): { sent: number; answered: number } {
    const trace = [...outputChannels()].filter(([name]) => name.includes("Trace")).map(([, text]) => text).join("\n");
    return {
        sent: trace.split("Sending request 'completionItem/resolve").length - 1,
        answered: trace.split("Received response 'completionItem/resolve").length - 1,
    };
}

describe("LUA-37", () => {
    before(async () => {
        await serverReady();
    });

    it("resolves the analyzer's items in an annotated script", async () => {
        // The annotation is in once the analyzer knows RaidForce as a TaskForce.
        await hoverWhen(file, { line: 68, text: "RaidForce" }, (c) => c.join("\n").includes("TaskForce"));
        const before = resolveFailures().length;
        const sentBefore = resolves().sent;
        const answeredBefore = resolves().answered;

        const line = await insertLine(file, 68, "\trepe");
        expect((await completions(file, { line, text: "repe", offset: 4 })).map((i) => i.label)).toContain("repeat");

        // The caret after "repe", then Ctrl+Space as a reader presses it: the list focuses "repeat" and
        // resolves it.
        await browser.waitUntil(
            async () => {
                await browser.executeWorkbench(
                    async (vscode, target: string, at: number) => {
                        const editor = await vscode.window.showTextDocument(vscode.Uri.file(target), { preview: false });
                        const end = editor.document.lineAt(at - 1).range.end;
                        editor.selection = new vscode.Selection(end, end);
                        await vscode.commands.executeCommand("workbench.action.focusActiveEditorGroup");
                    },
                    file,
                    line,
                );
                await browser.keys(["Escape"]);
                await browser.keys(["Control", "Space"]);
                await browser.pause(2_000);
                const focused = await browser.execute(
                    () => document.querySelector(".suggest-widget.visible .monaco-list-row.focused")?.textContent ?? null,
                );
                console.log("LUA-37 focused: " + JSON.stringify({ focused, ...resolves() }));
                return resolves().sent > sentBefore;
            },
            { timeout: 60_000, interval: 500, timeoutMsg: "The suggest list never resolved an item" },
        );
        await browser.pause(2_000);
        await browser.executeWorkbench(async (vscode) => vscode.commands.executeCommand("hideSuggestWidget"));
        await revert(file);

        expect(resolveFailures().slice(before)).toEqual([]);
        expect(resolves().answered).toBeGreaterThan(answeredBefore);
    });
});
