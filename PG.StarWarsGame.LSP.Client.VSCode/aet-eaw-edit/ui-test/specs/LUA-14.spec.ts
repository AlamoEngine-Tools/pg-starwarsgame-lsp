// LUA-14: a script's own definition of an engine function takes the engine's types and documentation; the author's
// own annotation wins; nothing is written into the document.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { diagnostics } from "./xmlEditor.js";
import { hoverWhen, plain, removeScripts, script } from "./luaEditor.js";

const file = script("Miscellaneous", "AetTest_Override.lua");

describe("LUA-14", () => {
    before(async () => {
        await serverReady();
    });

    it("documents and types the override as the engine function", async () => {
        const card = (await hoverWhen(file, { line: 3, text: "Find_Player" }, (c) => c.join("\n").includes("---"))).join("\n");
        const ours = plain([card.split(/\n-{3,}\n/)[0]]);
        expect(ours).toContain("function Find_Player(faction_name)");
        expect(ours).toContain('Finds a player (faction) by name. "local" names the local player, which no XML declares.');
        expect(ours).toMatch(/faction_name.*string\|"local"/);
        expect(ours).toMatch(/Returns.*Player/);

        expect((await hoverWhen(file, { line: 3, text: "faction_name" }, (c) => c.length > 0)).join("\n")).toMatch(/string\|"local"/);
        expect((await hoverWhen(file, { line: 9, text: " p", offset: 1 }, (c) => c.length > 0)).join("\n")).toMatch(/Player/);
        // Read at its use: the analyzer hovers no parameter at a declaration it was given no annotation for.
        expect((await hoverWhen(file, { line: 14, text: "type_name" }, (c) => c.length > 0)).join("\n")).toMatch(/\bany\b/);
    });

    it("writes nothing into the document and publishes no return-type mismatch", async () => {
        const lines: number = await browser.executeWorkbench(
            async (vscode, target: string) => (await vscode.workspace.openTextDocument(vscode.Uri.file(target))).lineCount,
            file,
        );
        expect(lines).toBeLessThanOrEqual(16);
        expect((await diagnostics(file)).filter((d) => d.line === 4)).toEqual([]);
    });

    after(() => removeScripts(file));
});
