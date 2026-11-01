// PRJ-15: a localisation tab opened while one project ran keeps its project when a second starts, and saves to it.
// The setup leaves beta.pgproj renamed to beta.pgproj.off, so Alpha runs alone at first.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { projectsReady, serverReady, tabLabels } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { editCell, openTextFile, save, saveTitle } from "./locGrid.js";

const root = "C:\\temp\\aet-test-PRJ-multi";
const alphaCsv = `${root}\\alpha\\Data\\Text\\MasterTextFile.csv`;
const locTab = async (): Promise<string | undefined> => (await tabLabels()).find((t) => t.startsWith("Loc: "));

describe("PRJ-15", () => {
    before(async () => {
        await serverReady();
    });

    it("keeps the tab's project when a second project starts", async () => {
        await openTextFile("Text files", "Alpha", "MasterTextFile.csv");
        await browser.waitUntil(async () => (await locTab()) !== undefined, { timeout: 60_000 });
        expect(await locTab()).not.toMatch(/ - Alpha$/);

        fs.renameSync(`${root}\\beta\\beta.pgproj.off`, `${root}\\beta\\beta.pgproj`);
        await projectsReady(2);
        await browser.waitUntil(async () => /- Alpha$/.test((await locTab()) ?? ""), {
            timeout: 60_000,
            timeoutMsg: `The tab is titled '${await locTab()}', not with Alpha`,
        });
    });

    it("saves to Alpha's file", async () => {
        const original = fs.readFileSync(alphaCsv, "utf8");
        try {
            await inNewestWebview(async () => {
                await editCell("TEXT_ALPHA_ONE", "Alpha changed");
                await save();
                await browser.waitUntil(async () => (await saveTitle()) === "Save - nothing is staged", {
                    timeout: 30_000,
                    timeoutMsg: "The save left the edit staged",
                });
            });
            expect(fs.readFileSync(alphaCsv, "utf8")).toContain("TEXT_ALPHA_ONE,Alpha changed");
        } finally {
            fs.writeFileSync(alphaCsv, original);
        }
    });
});
