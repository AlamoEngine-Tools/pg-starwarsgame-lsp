// The highlight frames of the What's New page, each 1920x1080: the Mod Browser in the two-project
// workspace, and a Lua file beside an XML file with their findings and a hover. AET_SHOT picks one
// ("browser", "editors", "probe"); unset, both frames are taken.
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { browser } from "@wdio/globals";
import { repo } from "../paths.js";
import { clearNotifications, projectsReady } from "../protocol.js";
import { openOutline, openRow, railClick, railExpand, search, selectRow, tableReady } from "../specs/modBrowser.js";
import { closeAll, inNewestWebview } from "../specs/storyGraph.js";

const EAW = "Empire at War - Vanilla";
const storagePath = path.join("C:\\temp\\aet-vscode", "whats-new-ui");
// The release the frames belong to: the manifest still carries the last one until the ship bump.
const release = process.env.AET_SHOT_RELEASE ?? "0.5.0";
const out = path.resolve(import.meta.dirname, "..", "..", "media", "whats-new", release);
const shot = process.env.AET_SHOT ?? "all";
const WIDTH = 1920;
const HEIGHT = 1080;

const luaFile = process.env.AET_SHOT_LUA ?? path.join(repo, "eaw", "Data", "Scripts", "GameObject", "Sandcrawler.lua");
const xmlFile = process.env.AET_SHOT_XML ?? path.join(repo, "eaw", "Data", "Xml", "Squadrons.xml");
const needle = new RegExp(process.env.AET_SHOT_NEEDLE ?? 'Find_Object_Type\\("Rebel_Light');
// The token under the pointer for the hover card, on the line the needle brings into view.
const hoverToken = process.env.AET_SHOT_HOVER ?? "Find_Object_Type";
const object = process.env.AET_SHOT_OBJECT ?? "Star_Destroyer";
// The vanilla files are clean, so each editor gets a slip of the kind a modder makes, in the buffer only:
// a misspelt tag and a misspelt object name in the XML, a misspelt object name in the script.
type Edits = [pattern: string, replacement: string][];
const xmlEdits: Edits = process.env.AET_SHOT_XML_EDITS
    ? JSON.parse(process.env.AET_SHOT_XML_EDITS)
    : [
          ["<Formation_Priority>1</Formation_Priority>", "<Formation_Priorty>1</Formation_Priorty>"],
          ["<Squadron_Units>X-Wing</Squadron_Units>", "<Squadron_Units>X-Wng</Squadron_Units>"],
      ];
const luaEdits: Edits = process.env.AET_SHOT_LUA_EDITS
    ? JSON.parse(process.env.AET_SHOT_LUA_EDITS)
    : [['Find_Object_Type("Stormtrooper")', 'Find_Object_Type("Stormtroper")']];

/** Sets the window's outer size through the OS; the driver's own window commands fail on Electron. */
function resize(width: number, height: number): void {
    execFileSync(
        "pwsh",
        [
            "-NoProfile",
            "-File",
            path.join(import.meta.dirname, "Set-WindowSize.ps1"),
            "-TitlePart",
            "Empire at War (Workspace)",
            "-Width",
            String(width),
            "-Height",
            String(height),
        ],
        { stdio: "pipe" },
    );
}

/** Sizes the window until the page itself is width x height, since the size set is the outer one. */
async function frame(): Promise<void> {
    let outerWidth = WIDTH;
    let outerHeight = HEIGHT;
    for (let i = 0; i < 4; i++) {
        resize(outerWidth, outerHeight);
        await browser.pause(800);
        const [w, h, dpr] = await browser.execute(() => [window.innerWidth, window.innerHeight, window.devicePixelRatio]);
        if (w === WIDTH && h === HEIGHT) {
            console.log(`frame ${w}x${h} at ${dpr}x`);
            return;
        }
        outerWidth += WIDTH - w;
        outerHeight += HEIGHT - h;
    }
    const [w, h] = await browser.execute(() => [window.innerWidth, window.innerHeight]);
    throw new Error(`The page is ${w}x${h}, not ${WIDTH}x${HEIGHT}`);
}

/**
 * Saves the window and reports the PNG's own size. `settled` says the toasts are already cleared; a
 * frame that is not `shipped` is a stage on the way, kept in C:\temp\aet-vscode\whats-new-frames for a
 * look (the profile folder is emptied on every run), never in media/.
 */
async function capture(name: string, settled = false, shipped = true): Promise<void> {
    const folder = shipped ? out : path.join("C:\\temp\\aet-vscode", "whats-new-frames");
    fs.mkdirSync(folder, { recursive: true });
    if (!settled) await clearNotifications();
    await browser.pause(1500);
    const file = path.join(folder, `${name}.png`);
    await browser.saveScreenshot(file);
    const png = fs.readFileSync(file);
    console.log(`${file}: ${png.readUInt32BE(16)}x${png.readUInt32BE(20)}`);
}

async function command(id: string, ...args: unknown[]): Promise<void> {
    await browser.executeWorkbench(
        (vscode, id: string, args: unknown[]) => vscode.commands.executeCommand(id, ...args),
        id,
        args,
    );
}

/** Folds the named sections of the Empire at War Edit container, so the one left open takes the side bar. */
async function collapseSections(...names: string[]): Promise<void> {
    const workbench = await browser.getWorkbench();
    const container = await workbench.getActivityBar().getViewControl("Empire at War Edit");
    const view = await container!.openView();
    for (const name of names) {
        const pane = await view.getContent().getSection(name);
        await pane.collapse();
    }
}

describe("What's New frames", () => {
    before(async () => {
        console.log("waiting for both projects");
        await projectsReady(2, 900_000);
        console.log("both projects indexed; sizing the window");
        await closeAll();
        // The secondary side bar renders as an empty black column while the chat it held is disabled.
        await command("workbench.action.closeAuxiliaryBar");
        await frame();
    });

    if (shot === "probe") {
        it("lists the files with findings", async () => {
            // Files are diagnosed when opened, so the candidates are opened as documents first.
            const candidates = [
                ...fs
                    .readdirSync(path.join(repo, "eaw", "Data", "Xml"))
                    .filter((f) => f.toLowerCase().endsWith(".xml"))
                    .map((f) => path.join(repo, "eaw", "Data", "Xml", f)),
                ...["GameObject", "Story", "Library", "AI"].flatMap((dir) => {
                    const folder = path.join(repo, "eaw", "Data", "Scripts", dir);
                    return fs.existsSync(folder)
                        ? fs
                              .readdirSync(folder)
                              .filter((f) => f.toLowerCase().endsWith(".lua"))
                              .map((f) => path.join(folder, f))
                        : [];
                }),
            ];
            console.log(`opening ${candidates.length} candidate files`);
            for (let i = 0; i < candidates.length; i += 25) {
                await browser.executeWorkbench(async (vscode, files: string[]) => {
                    for (const file of files) await vscode.workspace.openTextDocument(vscode.Uri.file(file));
                }, candidates.slice(i, i + 25));
                await browser.pause(4000);
            }
            await browser.pause(30_000);
            const found: { file: string; errors: number; warnings: number; infos: number }[] = await browser.executeWorkbench(
                (vscode) =>
                    vscode.languages.getDiagnostics().map(([uri, all]: [any, any[]]) => ({
                        file: uri.fsPath,
                        errors: all.filter((d) => d.severity === 0).length,
                        warnings: all.filter((d) => d.severity === 1).length,
                        infos: all.filter((d) => d.severity > 1).length,
                    })),
            );
            const report = found
                .filter((f) => f.errors + f.warnings + f.infos > 0)
                .sort((a, b) => b.errors + b.warnings - (a.errors + a.warnings));
            fs.writeFileSync(path.join(storagePath, "probe.json"), JSON.stringify(report, null, 1));
            console.log(`${report.length} files with findings; see ${path.join(storagePath, "probe.json")}`);
            for (const f of report.slice(0, 40)) console.log(`  ${f.errors}E ${f.warnings}W ${f.infos}I  ${f.file}`);
        });
    }

    if (shot === "all" || shot === "browser") {
        it("frames the Mod Browser in the two-project workspace", async () => {
            await openOutline("Game Data", EAW);
            // The browser is the frame's subject: the other sections fold to their headers.
            await collapseSections("Localisation Editor", "Campaign Editor");
            await inNewestWebview(async () => {
                await tableReady();
                await railClick("Game Objects");
                await tableReady();
                await search(object);
                await selectRow(object);
                await railExpand(object, 1);
                await browser.pause(1000);
            });
            await capture("mod-browser-table", false, false);
            await inNewestWebview(async () => {
                await openRow(object);
                await (await browser.$("nav.preview-actions")).waitForExist({ timeout: 60_000 });
                await browser.pause(3000);
            });
            // The entry at full width: head, references, encyclopedia card and actions. The unit preview
            // is a known sight and would only take the room from the browser.
            await capture("mod-browser");
            await closeAll();
        });
    }

    if (shot === "all" || shot === "editors") {
        it("frames a Lua file beside an XML file", async () => {
            await command("workbench.view.explorer");
            await command("workbench.files.action.collapseExplorerFolders");
            await browser.executeWorkbench(
                async (vscode, lua: string, xml: string, needle: string, xmlEdits: Edits, luaEdits: Edits) => {
                    const xmlDoc = await vscode.workspace.openTextDocument(vscode.Uri.file(xml));
                    const xmlEditor = await vscode.window.showTextDocument(xmlDoc, { viewColumn: vscode.ViewColumn.Two, preview: false });
                    const luaDoc = await vscode.workspace.openTextDocument(vscode.Uri.file(lua));
                    const editor = await vscode.window.showTextDocument(luaDoc, { viewColumn: vscode.ViewColumn.One, preview: false });
                    // No inner named function here: this body is serialized into the extension host, where
                    // the name-keeping helper the TypeScript loader adds for one does not exist.
                    for (const [target, edits] of [
                        [xmlEditor, xmlEdits],
                        [editor, luaEdits],
                    ] as [any, Edits][]) {
                        await target.edit((b: any) => {
                            for (const [pattern, replacement] of edits) {
                                const at = target.document.getText().indexOf(pattern);
                                if (at < 0) continue;
                                b.replace(
                                    new vscode.Range(target.document.positionAt(at), target.document.positionAt(at + pattern.length)),
                                    replacement,
                                );
                            }
                        });
                    }
                },
                luaFile,
                xmlFile,
                needle.source,
                xmlEdits,
                luaEdits,
            );
            await command("workbench.actions.view.problems");
            await browser.pause(20_000);
            // A short Problems view: the slips and a few of the file's own findings, not a page of repeats.
            // With the panel focused, "increase view height" grows the editors above it, about 60px a step.
            await command("workbench.action.focusPanel");
            for (let i = 0; i < Number(process.env.AET_SHOT_PANEL_SHRINK ?? 3); i++) {
                await command("workbench.action.increaseViewHeight");
            }
            await clearNotifications();
            // Last, once the panel has taken its height: the cursor on the call, the hover over it.
            await command("workbench.action.focusFirstEditorGroup");
            await browser.executeWorkbench(
                async (vscode, needle: string) => {
                    const editor = vscode.window.activeTextEditor;
                    const at = editor.document.getText().search(new RegExp(needle));
                    if (at < 0) return;
                    const position = editor.document.positionAt(at + 1);
                    editor.selection = new vscode.Selection(position, position);
                    editor.revealRange(new vscode.Range(position, position), vscode.TextEditorRevealType.InCenter);
                },
                needle.source,
            );
            await browser.pause(1000);
            // A real mouse hover, which stays while the pointer rests; the command form needs editor focus
            // the panel took and went away before the capture.
            const token = await browser.$(
                `//div[contains(@class,'view-lines')]//span[contains(text(),'${hoverToken}')]`,
            );
            await token.waitForExist({ timeout: 30_000, timeoutMsg: `No '${hoverToken}' token on screen` });
            await token.moveTo();
            await browser.pause(2500);
            await capture("lua-and-xml", true);
        });
    }
});
