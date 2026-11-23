// SET-35: a save through the credits editor changes the credits file in the edited line and in
// nothing else (#339) - the quoted cell, the cell ending in a space and the order of the lines stay.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { columns, editCell, openTextFile, save, saveTitle } from "./locGrid.js";

const credits = "C:\\temp\\aet-test-SET-credits\\Data\\Text\\CreditsText.csv";

/** What the window holds for the credits file, when it has loaded it. */
async function buffer(): Promise<string | undefined> {
    return browser.executeWorkbench((vscode, file: string) => {
        const document = vscode.workspace.textDocuments.find(
            (d: { uri: { fsPath: string } }) => d.uri.fsPath.toLowerCase() === file.toLowerCase(),
        );
        return document?.getText();
    }, credits);
}

describe("SET-35", () => {
    let original = "";
    let expected = "";

    before(async () => {
        original = fs.readFileSync(credits, "utf8");
        expected = original.replace("HEADER,Lead Designer", "HEADER,Lead Writer");
        await serverReady();
    });

    it("saves the edited line into the credits file and changes nothing else in it", async () => {
        await openTextFile("Credits files", "CreditsText.csv");
        await inNewestWebview(async () => {
            expect(await columns()).toEqual(["Format", "ENGLISH"]);
            await editCell("HEADER", "Lead Writer");
            await browser.waitUntil(async () => (await saveTitle()) !== "Save - nothing is staged", {
                timeout: 15_000,
                timeoutMsg: "The edit was not staged",
            });
            await save();
            await browser.waitUntil(async () => (await saveTitle()) === "Save - nothing is staged", {
                timeout: 30_000,
                timeoutMsg: "The save left the edit staged",
            });
        });

        // The saved credits are read where the save put them: the window when it holds the file, the disk otherwise.
        expect((await buffer()) ?? fs.readFileSync(credits, "utf8")).toBe(expected);
    });
});
