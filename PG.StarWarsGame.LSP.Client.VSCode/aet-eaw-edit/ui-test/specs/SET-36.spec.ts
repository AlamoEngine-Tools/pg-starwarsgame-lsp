// SET-36: a set of single-language files is one table, and a save that touches two of its files is one
// edit - each file changed in the edited line and in nothing else, and one undo takes both back (#339).
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { columns, editCell, openTextFile, rowTexts, save, saveTitle } from "./locGrid.js";

const english = "C:\\temp\\aet-test-SET-set\\Data\\Text\\MasterTextFile_ENGLISH.properties";
const german = "C:\\temp\\aet-test-SET-set\\Data\\Text\\MasterTextFile_GERMAN.properties";
const nothingStaged = "Save - nothing is staged";

/** What the window holds for each file of the set while it has it loaded. */
async function buffers(): Promise<{ open: boolean; dirty: boolean; text: string }[]> {
    return browser.executeWorkbench(
        (vscode, files: string[]) =>
            files.map((file) => {
                const document = vscode.workspace.textDocuments.find(
                    (d: { uri: { fsPath: string } }) => d.uri.fsPath.toLowerCase() === file.toLowerCase(),
                );
                return { open: document !== undefined, dirty: document?.isDirty ?? false, text: document?.getText() ?? "" };
            }),
        [english, german],
    );
}

describe("SET-36", () => {
    let originals: string[] = [];
    let expected: string[] = [];

    before(async () => {
        originals = [fs.readFileSync(english, "utf8"), fs.readFileSync(german, "utf8")];
        expected = [
            originals[0].replace("TEXT_SET_ALPHA=Alpha", "TEXT_SET_ALPHA=Alpha One"),
            originals[1].replace("TEXT_SET_ALPHA=Alfa", "TEXT_SET_ALPHA=Alfa Eins"),
        ];
        await serverReady();
    });

    it("saves an edit of two languages into the two files of the set, each changed in its one line", async () => {
        await openTextFile("Text files", "mastertextfile.properties");
        await inNewestWebview(async () => {
            expect(await columns()).toEqual(["Key", "ENGLISH", "GERMAN"]);
            expect(await rowTexts()).toEqual(["TEXT_SET_ALPHA | Alpha | Alfa", "TEXT_SET_BETA | Beta | Beta DE"]);
            await editCell("TEXT_SET_ALPHA", "Alpha One", 1);
            await editCell("TEXT_SET_ALPHA", "Alfa Eins", 2);
            await browser.waitUntil(async () => (await saveTitle()) !== nothingStaged, {
                timeout: 15_000,
                timeoutMsg: "The edits were not staged",
            });
            await save();
            await browser.waitUntil(async () => (await saveTitle()) === nothingStaged, {
                timeout: 30_000,
                timeoutMsg: "The save left the edits staged",
            });
        });

        await browser.waitUntil(async () => fs.readFileSync(german, "utf8") !== originals[1], {
            timeout: 30_000,
            timeoutMsg: "The save did not reach the set's files",
        });
        // The comment line of each file and the row nobody edited stay as written.
        expect(fs.readFileSync(english, "utf8")).toBe(expected[0]);
        expect(fs.readFileSync(german, "utf8")).toBe(expected[1]);
        // Written, not left behind as unsaved documents.
        expect((await buffers()).some((b) => b.dirty)).toBe(false);
    });

    it("takes the save back in both files with one undo, which leaves them unsaved", async () => {
        await browser.executeWorkbench(async (vscode, file: string) => {
            await vscode.window.showTextDocument(vscode.Uri.file(file), { preview: false });
            // Not awaited: an undo that asked first would not come back until it was answered.
            void vscode.commands.executeCommand("undo");
        }, english);
        await browser.waitUntil(async () => (await buffers()).every((b) => b.open && b.dirty), {
            timeout: 15_000,
            timeoutMsg: "One undo did not take the save back in both files",
        });
        // Undone in the file that was not in front, too, and without a question asked.
        expect(await buffers()).toEqual([
            { open: true, dirty: true, text: originals[0] },
            { open: true, dirty: true, text: originals[1] },
        ]);
        expect(await (await browser.$(".monaco-dialog-box")).isDisplayed()).toBe(false);
        // The undo is an edit like any other: the windows hold the originals again, the disk the save.
        expect(fs.readFileSync(english, "utf8")).toBe(expected[0]);
        expect(fs.readFileSync(german, "utf8")).toBe(expected[1]);
    });
});
