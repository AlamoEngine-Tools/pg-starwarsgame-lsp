// SET-35: a save through the credits editor writes the credits file, changed in the edited line and in
// nothing else - the byte order mark, the quoted cell, the cell ending in a space and the order of the
// lines stay - and it does not write over changes the file's own text editor holds unsaved (#339).
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { notifications, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { columns, contentTiles, editCell, openTextFile, save, saveTitle } from "./locGrid.js";

const credits = "C:\\temp\\aet-test-SET-credits\\Data\\Text\\CreditsText.csv";
const nothingStaged = "Save - nothing is staged";

/** Types one edit into the grid and presses Save. */
async function saveCell(key: string, value: string): Promise<void> {
    await editCell(key, value);
    await browser.waitUntil(async () => (await saveTitle()) !== nothingStaged, {
        timeout: 15_000,
        timeoutMsg: "The edit was not staged",
    });
    await save();
}

describe("SET-35", () => {
    let original = "";

    before(async () => {
        original = fs.readFileSync(credits, "utf8");
        await serverReady();
    });

    it("saves the edited line into the credits file and changes nothing else in it", async () => {
        const expected = original.replace("HEADER,Lead Designer", "HEADER,Lead Writer");
        await openTextFile("Credits files", "CreditsText.csv");
        await inNewestWebview(async () => {
            expect(await columns()).toEqual(["Format", "ENGLISH"]);
            // The line kinds of a credits file that is edited, each with the token it becomes.
            expect(await contentTiles()).toEqual([
                { label: "Label", badge: "HEADER" },
                { label: "Name", badge: "CENTER" },
                { label: "Blank line", badge: "[TBL]" },
            ]);
            await saveCell("HEADER", "Lead Writer");
            await browser.waitUntil(async () => (await saveTitle()) === nothingStaged, {
                timeout: 30_000,
                timeoutMsg: "The save left the edit staged",
            });
        });

        await browser.waitUntil(async () => fs.readFileSync(credits, "utf8") !== original, {
            timeout: 30_000,
            timeoutMsg: "The save did not reach the credits file",
        });
        expect(fs.readFileSync(credits, "utf8")).toBe(expected);
        // The mark is not text; the file keeps it because the window that holds the file writes it.
        expect([...fs.readFileSync(credits).subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    });

    it("does not write over changes the file's text editor holds unsaved, and says which file", async () => {
        const saved = fs.readFileSync(credits, "utf8");
        // The credits file as text, with a line typed into it and not saved.
        await browser.executeWorkbench(async (vscode, file: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
            const edit = new vscode.WorkspaceEdit();
            edit.insert(document.uri, new vscode.Position(document.lineCount, 0), "CENTER,Typed by hand\n");
            await vscode.workspace.applyEdit(edit);
        }, credits);

        await inNewestWebview(async () => {
            await saveCell("HEADER", "Lead Author");
            await browser.pause(3_000);
            // Refused: the edit stays staged for another try.
            expect(await saveTitle()).not.toBe(nothingStaged);
        });

        expect(fs.readFileSync(credits, "utf8")).toBe(saved);
        const refusal = (await notifications()).map((n) => n.message).find((m) => m.includes("Could not save"));
        expect(refusal).toContain("Unsaved changes in CreditsText.csv would be overwritten");
    });
});
