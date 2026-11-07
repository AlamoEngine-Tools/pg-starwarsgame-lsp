// PRJ-13: localisation tabs are filed per project, and the editor's own Export acts on its own project.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { notification, pickQuickItem, projectsReady, tabLabels } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";
import { confirmDialog, openTextFile, pressTile } from "./locGrid.js";

const root = "C:\\temp\\aet-test-PRJ-multi";
const dats = (project: string): string[] =>
    fs.readdirSync(`${root}\\${project}\\Data\\Text`).filter((f) => f.toLowerCase().endsWith(".dat"));
const locTabs = async (): Promise<string[]> => (await tabLabels()).filter((t) => t.startsWith("Loc: "));

describe("PRJ-13", () => {
    before(async () => {
        await projectsReady(2);
        await closeAll();
    });

    it("opens a tab per project for the shared file", async () => {
        await openTextFile("Alpha", "Text files", "Shared", "MasterTextFile.csv");
        await browser.waitUntil(async () => (await locTabs()).length === 1, { timeout: 60_000 });
        expect((await locTabs())[0]).toMatch(/ - Alpha$/);
        await openTextFile("Beta", "Text files", "Shared", "MasterTextFile.csv");
        await browser.waitUntil(async () => (await locTabs()).length === 2, {
            timeout: 60_000,
            timeoutMsg: "Beta's shared file revealed Alpha's tab instead of opening its own",
        });
        expect((await locTabs())[1]).toMatch(/ - Beta$/);
        await openTextFile("Alpha", "Text files", "Alpha", "MasterTextFile.csv");
        await browser.waitUntil(async () => (await locTabs()).length === 3, { timeout: 60_000 });
    });

    it("exports Alpha's own file without asking for a project, whatever project is selected", async () => {
        const beta = dats("beta");
        await browser.executeWorkbench((vscode) => {
            void vscode.commands.executeCommand("aet-eaw-edit.lsp.selectProject");
        });
        await pickQuickItem("Beta");
        // Alpha's own tab is the one opened last, so it is the newest panel.
        await inNewestWebview(async () => {
            await pressTile("Export DAT");
            await confirmDialog("Export");
        });
        await notification((m) => /^EaWEdit: Exported \d+ DAT files?\.$/.test(m));
        expect(dats("alpha").length).toBeGreaterThan(0);
        expect(dats("beta")).toEqual(beta);
        for (const file of dats("alpha")) fs.rmSync(`${root}\\alpha\\Data\\Text\\${file}`);
    });
});
