// LUA-26: the Lua debugger's configuration lives in launch.json - the old game settings are unknown, the created
// configurations, IntelliSense and snippets are the debugger contribution's. Clicking 'create a launch.json file'
// itself stays a hand check: the manifest's initial configurations are what it writes.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { client } from "../paths.js";
import { eaw, editUserSettingsJson, serverReady, unknownSettings } from "../protocol.js";

const OLD = {
    "aet-eaw-edit.game.executable": "x",
    "aet-eaw-edit.game.luaDebugPort": 1234,
    "aet-eaw-edit.game.luaDebugHost": "127.0.0.1",
    "aet-eaw-edit.game.unsafeTableExpansion": false,
    "aet-eaw-edit.game.arguments": [],
};
const launchJson = path.join(eaw, ".vscode", "launch.json");

const manifest = (): any => JSON.parse(fs.readFileSync(path.join(client, "package.json"), "utf8"));
const nls = (): Record<string, string> => JSON.parse(fs.readFileSync(path.join(client, "package.nls.json"), "utf8"));
const localised = (text: string): string => (text.startsWith("%") ? nls()[text.slice(1, -1)] : text);

/**
 * The property completions VS Code offers inside a configuration object of the launch.json - asked again until
 * the JSON service has the launch schema (before it, the document's own words come back).
 */
async function propertiesOffered(config: string): Promise<string[]> {
    const text = `{\n  "version": "0.2.0",\n  "configurations": [\n    ${config}\n  ]\n}\n`;
    fs.mkdirSync(path.dirname(launchJson), { recursive: true });
    if (!fs.existsSync(launchJson)) fs.writeFileSync(launchJson, text);
    let offered: string[] = [];
    await browser.waitUntil(
        async () => {
            // The text is set in the editor's document: a file written behind an open one is not reread at once.
            offered = await browser.executeWorkbench(async (vscode, file: string, text: string) => {
                const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
                await vscode.window.showTextDocument(document);
                if (document.getText() !== text) {
                    const edit = new vscode.WorkspaceEdit();
                    edit.replace(document.uri, new vscode.Range(0, 0, document.lineCount, 0), text);
                    await vscode.workspace.applyEdit(edit);
                }
                const line = 3;
                const column = document.lineAt(line).text.lastIndexOf("}");
                const list: any = await vscode.commands.executeCommand(
                    "vscode.executeCompletionItemProvider",
                    document.uri,
                    new vscode.Position(line, column),
                );
                return (list?.items ?? [])
                    .filter((i: any) => i.kind === vscode.CompletionItemKind.Property)
                    .map((i: any) => (typeof i.label === "string" ? i.label : i.label.label).replace(/"/g, ""));
            }, launchJson, text);
            return offered.length > 0;
        },
        { timeout: 60_000, interval: 2_000, timeoutMsg: "The launch.json offers no properties" },
    );
    return offered;
}

describe("LUA-26", () => {
    before(async () => {
        await serverReady();
        editUserSettingsJson(OLD);
    });

    it("marks the five old game settings unknown", () => {
        expect(unknownSettings()).toEqual(expect.arrayContaining(Object.keys(OLD)));
    });

    it("creates an attach and a launch configuration, and offers the two snippets", () => {
        const debuggerOf = manifest().contributes.debuggers.find((d: any) => d.type === "eaw-lua");
        expect(debuggerOf.initialConfigurations.map((c: any) => ({ ...c, name: localised(c.name) }))).toEqual([
            { type: "eaw-lua", request: "attach", name: "Attach to Empire at War (Lua)" },
            { type: "eaw-lua", request: "launch", name: "Launch Empire at War (Lua)", program: "" },
        ]);
        expect(debuggerOf.configurationSnippets.map((s: any) => localised(s.label))).toEqual([
            "Empire at War: Attach to the Lua debugger",
            "Empire at War: Launch with the Lua debugger",
        ]);
        expect(debuggerOf.configurationSnippets[1].body.program).toContain("StarWarsI.exe");
        expect(localised(debuggerOf.configurationAttributes.attach.properties.port.description)).toBe(
            "The game's Lua debug UDP port. The game takes the first free port from 1234 upward; defaults to 1234",
        );
    });

    it("completes an attach and a launch configuration's properties", async () => {
        const attach = await propertiesOffered('{ "type": "eaw-lua", "request": "attach", "name": "a", }');
        expect(attach).toEqual(
            expect.arrayContaining(["host", "port", "sourceRoots", "clientName", "unsafeTableExpansion", "dropDuplicateOutermostFrame"]),
        );
        expect(attach).not.toContain("program");
        const launch = await propertiesOffered('{ "type": "eaw-lua", "request": "launch", "name": "l", }');
        expect(launch).toEqual(expect.arrayContaining(["program", "args", "cwd", "modPaths", "attachTimeoutSeconds"]));
    });

    after(() => {
        editUserSettingsJson(Object.fromEntries(Object.keys(OLD).map((k) => [k, null])));
        fs.rmSync(launchJson, { force: true });
    });
});
