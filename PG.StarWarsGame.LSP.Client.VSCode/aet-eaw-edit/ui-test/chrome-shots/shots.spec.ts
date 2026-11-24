// The pictures of the shared chrome: each page in the states its dock and toolbar have, once in the dark
// theme and once in the light one. AET_SHOT picks one page ("story", "text", "encyclopedia", "preview");
// unset, all are taken. See wdio.conf.ts for how to run it.
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { browser } from "@wdio/globals";
import { client, repo } from "../paths.js";
import { clearNotifications, command, serverReady, setSettings } from "../protocol.js";
import { closeAll, graphLoaded, inNewestWebview, openStoryGraph } from "../specs/storyGraph.js";

const label = process.env.AET_SHOT_LABEL ?? "current";
const out = path.join(client, "ui-test", "chrome-shots", "out", label);
const shot = process.env.AET_SHOT ?? "all";
const WIDTH = 1600;
const HEIGHT = 1000;

const THEMES = [
    ["dark", "Default Dark Modern"],
    ["light", "Default Light Modern"],
] as const;

// What each page shows. The vanilla workspace has all of them.
const campaign = process.env.AET_SHOT_CAMPAIGN ?? "Full_Story_Campaign_Empire";
const faction = process.env.AET_SHOT_FACTION ?? "Empire";
const unit = process.env.AET_SHOT_UNIT ?? "Nebulon_B_Frigate";
const object = process.env.AET_SHOT_OBJECT ?? "Star_Destroyer";

/** Sets the window's outer size through the OS; the driver's own window commands fail on Electron. */
function resize(width: number, height: number): void {
    execFileSync(
        "pwsh",
        [
            "-NoProfile",
            "-File",
            path.join(import.meta.dirname, "..", "whats-new", "Set-WindowSize.ps1"),
            "-TitlePart",
            "Chrome Shots (Workspace)",
            "-Width",
            String(width),
            "-Height",
            String(height),
        ],
        { stdio: "pipe" },
    );
}

/** Sizes the window until the page itself is WIDTH x HEIGHT, since the size set is the outer one. */
async function frame(): Promise<void> {
    let outerWidth = WIDTH;
    let outerHeight = HEIGHT;
    for (let i = 0; i < 4; i++) {
        resize(outerWidth, outerHeight);
        await browser.pause(800);
        const [w, h] = await browser.execute(() => [window.innerWidth, window.innerHeight]);
        if (w === WIDTH && h === HEIGHT) return;
        outerWidth += WIDTH - w;
        outerHeight += HEIGHT - h;
    }
    const [w, h] = await browser.execute(() => [window.innerWidth, window.innerHeight]);
    throw new Error(`The page is ${w}x${h}, not ${WIDTH}x${HEIGHT}`);
}

/** Saves the window as it stands in both themes: `<name>-dark.png` and `<name>-light.png`. */
async function capture(name: string): Promise<void> {
    fs.mkdirSync(out, { recursive: true });
    for (const [key, theme] of THEMES) {
        await setSettings({ "workbench.colorTheme": theme });
        await clearNotifications();
        // The webview takes the theme's colours a moment after the workbench does.
        await browser.pause(1500);
        const file = path.join(out, `${name}-${key}.png`);
        await browser.saveScreenshot(file);
        console.log(file);
    }
    await setSettings({ "workbench.colorTheme": THEMES[0][1] });
}

/** Presses the dial's position whose tooltip starts with `title`. Run inside the panel. */
async function dial(title: string): Promise<void> {
    const position = await browser.$(`button.rotary-pos[title^="${title}"]`);
    await position.waitForClickable({ timeout: 120_000 });
    await position.click();
    await browser.pause(1500);
}

/** A text file of the workspace for the localisation grid: the editable one when there is one. */
function textFile(): { filePath: string; label: string } {
    const folder = path.join(repo, "eaw", "Data", "Text");
    const files = fs.readdirSync(folder);
    const name =
        files.find((f) => f.toLowerCase() === "mastertextfile.csv") ??
        files.find((f) => f.toLowerCase() === "mastertextfile_english.dat");
    if (name === undefined) throw new Error(`No master text file under ${folder}`);
    return { filePath: path.join(folder, name), label: name };
}

describe("Chrome pictures", () => {
    before(async () => {
        // One project: its status carries no count, which the several-project wait looks for.
        await serverReady();
        await closeAll();
        // The page is the picture: the side bars and the panel only take its room.
        await browser.executeWorkbench(async (vscode) => {
            await vscode.commands.executeCommand("workbench.action.closeSidebar");
            await vscode.commands.executeCommand("workbench.action.closeAuxiliaryBar");
            await vscode.commands.executeCommand("workbench.action.closePanel");
        });
        await frame();
    });

    afterEach(async () => {
        await closeAll();
        // A panel opened in the moment the last one closes loses its first message.
        await browser.pause(2000);
    });

    if (shot === "all" || shot === "story") {
        it("takes the story graph in View, Edit and Simulation", async () => {
            await openStoryGraph(campaign, faction);
            await inNewestWebview(async () => {
                await graphLoaded();
                await browser.pause(2000);
            });
            await capture("story-1-view");

            await inNewestWebview(() => dial("Edit"));
            await capture("story-2-edit");

            await inNewestWebview(async () => {
                await dial("Simulation");
                await (await browser.$(".sim-transport")).waitForExist({ timeout: 60_000 });
                await browser.pause(1500);
                // A battle that starts with the campaign asks for its outcome at once, over the canvas.
                // The picture is of the dock, so the question is put away unanswered.
                await browser.execute(() => {
                    Array.from(document.querySelectorAll("button"))
                        .find((b) => b.textContent?.trim() === "Close")
                        ?.click();
                });
                await browser.pause(800);
            });
            await capture("story-3-simulation");
        });
    }

    if (shot === "all" || shot === "text") {
        it("takes the localisation grid", async () => {
            const { filePath, label: fileLabel } = textFile();
            await command("aet-eaw-edit.lsp.openLocalisationEditor", {
                project: { filePath, label: fileLabel, category: "text" },
            });
            await inNewestWebview(async () => {
                await (await browser.$(".dock-tile")).waitForExist({ timeout: 120_000 });
                await browser.pause(3000);
            });
            await capture("text-1-grid");
        });
    }

    if (shot === "all" || shot === "encyclopedia") {
        it("takes the encyclopedia preview", async () => {
            await command("aet-eaw-edit.lsp.showEncyclopedia", object);
            await inNewestWebview(async () => {
                await (await browser.$(".right-dock")).waitForExist({ timeout: 120_000 });
                await browser.pause(4000);
            });
            await capture("encyclopedia-1-card");
        });
    }

    if (shot === "all" || shot === "preview") {
        it("takes the unit preview in Model, Animation and Gameplay", async () => {
            await command("aet-eaw-edit.lsp.previewAssembledUnit", unit);
            await inNewestWebview(async () => {
                await dial("Model");
                // The model and its textures arrive after the dock does.
                await browser.pause(8000);
            });
            await capture("preview-1-model");

            await inNewestWebview(() => dial("Animation"));
            await capture("preview-2-animation");

            await inNewestWebview(() => dial("Gameplay"));
            await capture("preview-3-gameplay");
        });
    }
});
