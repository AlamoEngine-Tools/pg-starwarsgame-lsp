// PRJ-09: encyclopedia and story graph panels are one per project, titled with it, and do not follow
// the editor.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { command, eaw, EAW, foc, FOC, openFile, projectsReady, tabLabels } from "../protocol.js";

describe("PRJ-09", () => {
    before(async () => {
        await projectsReady(2);
    });

    it("opens one encyclopedia panel per project and keeps its title", async () => {
        // The command takes the project of the editor in front.
        for (const folder of [eaw, foc]) {
            await openFile(path.join(folder, "Data", "Xml", "Spaceunitscapital.xml"));
            await command("aet-eaw-edit.lsp.showEncyclopedia", "Star_Destroyer");
            await browser.pause(3_000);
        }
        let encyclopedias: string[] = [];
        await browser.waitUntil(
            async () => (encyclopedias = (await tabLabels()).filter((t) => t.startsWith("Encyclopedia: "))).length === 2,
            { timeout: 60_000, timeoutMsg: `Encyclopedia tabs ${JSON.stringify(encyclopedias)}` },
        );
        expect(encyclopedias.filter((t) => t.endsWith(` - ${EAW}`))).toHaveLength(1);
        expect(encyclopedias.filter((t) => t.endsWith(` - ${FOC}`))).toHaveLength(1);

        await openFile(path.join(eaw, "Data", "Xml", "Spaceunitscapital.xml"));
        await browser.pause(2_000);
        expect((await tabLabels()).filter((t) => t.startsWith("Encyclopedia: "))).toEqual(encyclopedias);
    });

    it("opens one story graph per project, titled with it", async () => {
        // The command takes the project of the editor in front.
        for (const folder of [eaw, foc]) {
            await openFile(path.join(folder, "Data", "Xml", "Spaceunitscapital.xml"));
            await command("aet-eaw-edit.lsp.openStoryGraph", {
                campaignName: "Sandbox_Alderaans_Demise_Rebel",
                factionName: "Rebel",
            });
            await browser.pause(3_000);
        }
        let stories: string[] = [];
        await browser.waitUntil(
            async () => (stories = (await tabLabels()).filter((t) => t.startsWith("Story - "))).length === 2,
            { timeout: 60_000, timeoutMsg: `Story tabs ${JSON.stringify(stories)}` },
        );
        expect(stories.filter((t) => t.startsWith("Story - Sandbox_Alderaans_Demise_Rebel - ") && t.endsWith(` - ${EAW}`))).toHaveLength(1);
        expect(stories.filter((t) => t.startsWith("Story - Sandbox_Alderaans_Demise_Rebel - ") && t.endsWith(` - ${FOC}`))).toHaveLength(1);
    });
});
