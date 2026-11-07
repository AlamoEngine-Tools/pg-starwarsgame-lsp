// PRJ-12: a dependency's localisation file is read-only - saving and exporting are refused and nothing is
// written.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { notification, projectsReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { confirmDialog, editCell, openTextFile, pressTile, save, saveTitle, treeRows } from "./locGrid.js";

const shared = "C:\\temp\\aet-test-PRJ-multi\\shared\\Data\\Text";
const readOnly = (verb: string): RegExp =>
    new RegExp(
        `^EaWEdit: ${verb} - '[^']*shared[\\\\/]Data[\\\\/]Text[\\\\/]MasterTextFile\\.csv' belongs to Shared, which is read-only\\. ` +
            "Copy the file into the project's text directory to override it, then edit the copy\\.$",
        "i",
    );

describe("PRJ-12", () => {
    before(async () => {
        await projectsReady(2);
    });

    it("lists the layers under Text files, the project's first", async () => {
        await openTextFile("Alpha", "Text files", "Alpha");
        const rows = await treeRows();
        const files = rows.findIndex((r, i) => r.label === "Text files" && rows[i - 1]?.label === "Alpha" && r.level === 2);
        const layers = rows.slice(files + 1).filter((r) => r.level === 3);
        expect(layers.slice(0, 2).map((r) => r.label)).toEqual(["Alpha", "Shared"]);
    });

    it("refuses to save or export the shared file", async () => {
        await openTextFile("Alpha", "Text files", "Shared", "MasterTextFile.csv");
        await inNewestWebview(async () => {
            await editCell("TEXT_SHARED_ONE", "Changed");
            expect(await saveTitle()).toMatch(/^Save - /);
            await save();
        });
        const refused = await notification((m) => readOnly("Could not save").test(m));
        expect(refused).toMatchObject({ type: "error", actions: ["Reload"] });

        await inNewestWebview(async () => {
            // The edit stays staged.
            expect(await saveTitle()).not.toBe("Save - nothing is staged");
            await pressTile("Export DAT");
            await confirmDialog("Export");
        });
        const exported = await notification((m) => readOnly("DAT export failed").test(m));
        expect(exported.type).toBe("error");

        expect(fs.readFileSync(`${shared}\\MasterTextFile.csv`, "utf8")).toContain("TEXT_SHARED_ONE,Shared one");
        expect(fs.readdirSync(shared).filter((f) => f.toLowerCase().endsWith(".dat"))).toEqual([]);
    });
});
