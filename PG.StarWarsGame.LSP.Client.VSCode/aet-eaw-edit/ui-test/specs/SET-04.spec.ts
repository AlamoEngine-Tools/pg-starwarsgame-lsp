// SET-04: with no setting at all, everything but the two experimental modes is on.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { copyBugReport, eaw, hoverWhenReady, serverReady, viewItems } from "../protocol.js";
import { storyModes } from "./storyGraph.js";

describe("SET-04", () => {
    // With no setting, the baseline comes from the newest release; before the 0.5.0 baseline release
    // that is the default branch, which an EaW project refuses (SET-12 records which one was seen).
    before(async function () {
        await serverReady();
        if ((await copyBugReport()).includes("- Project: BaselineRefused")) this.skip();
    });

    it("answers the Lua hover", async () => {
        const script = path.join(eaw, "Data", "Scripts", "Story", "Story_empire_acti_m02_land.lua");
        expect(await hoverWhenReady(script, 185, 18)).toContain("Find_First_Object");
    });

    it("hints the English text after a story dialog key", async () => {
        const dialog = path.join(eaw, "Data", "Scripts", "Story", "Dialog_e_focusdemo.txt");
        let hints: string[] = [];
        await browser.waitUntil(
            async () => {
                hints = await browser.executeWorkbench(async (vscode, file: string) => {
                    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
                    await vscode.window.showTextDocument(document);
                    const found = await vscode.commands.executeCommand(
                        "vscode.executeInlayHintProvider",
                        document.uri,
                        new vscode.Range(8, 0, 9, 0),
                    );
                    return (found ?? []).map((h: any) =>
                        typeof h.label === "string" ? h.label : h.label.map((p: any) => p.value).join(""),
                    );
                }, dialog);
                return hints.length > 0;
            },
            { timeout: 60_000, interval: 2000, timeoutMsg: "No inlay hint on line 9" },
        );
        expect(hints.join(" ").trim().length).toBeGreaterThan(0);
    });

    it("fills the Campaign Editor, the Localisation Editor and the Mod Browser", async () => {
        const campaigns = await viewItems("Campaign Editor");
        expect(campaigns).toContain("Main_Campaign_Rebel");
        expect(campaigns).toContain("Main_Campaign_Empire");
        expect(await viewItems("Localisation Editor")).toContain("Text files");
        expect(await viewItems("Mod Browser")).toEqual(expect.arrayContaining(["Game Data", "Assets", "Scripts", "Schema"]));
    });

    it("offers the story graph in View only", async () => {
        expect(await storyModes("Main_Campaign_Empire", "Empire")).toEqual(["View"]);
    });

    it("reports both experimental modes off", async () => {
        expect(await copyBugReport()).toContain("- Features off: storyEditing, storySimulator");
    });
});
