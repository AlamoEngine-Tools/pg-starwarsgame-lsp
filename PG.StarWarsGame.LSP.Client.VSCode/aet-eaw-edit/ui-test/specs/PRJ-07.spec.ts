// PRJ-07: commands act on the project in front, else the remembered pick, else a picker; Select Project
// changes the pick. The restore after a reload is not here: editor state is in memory under the driver
// (see run.mjs), so no tab is restored by any window.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { activeTab, answerInput, command, eaw, EAW, foc, FOC, openFile, pickQuickItem, projectsReady, quickPick, tabLabels } from "../protocol.js";
import { closeAll } from "./storyGraph.js";

const tab = (project: string) => `Star_Destroyer - ${project} (effective preview, read-only).xml`;

async function showEffectiveObject(): Promise<void> {
    await command("aet-eaw-edit.lsp.showEffectiveObject");
    await answerInput("Star_Destroyer");
}

describe("PRJ-07", () => {
    before(async () => {
        await projectsReady(2);
    });

    it("acts on the project of the editor in front", async () => {
        await openFile(path.join(eaw, "Data", "Xml", "Spaceunitscapital.xml"));
        await showEffectiveObject();
        await browser.waitUntil(async () => (await tabLabels()).includes(tab(EAW)), { timeout: 30_000 });
        await openFile(path.join(foc, "Data", "Xml", "Spaceunitscapital.xml"));
        await showEffectiveObject();
        await browser.waitUntil(async () => (await tabLabels()).includes(tab(FOC)), { timeout: 30_000 });
        expect(await tabLabels()).toContain(tab(EAW));
    });

    it("asks which project on Select Project", async () => {
        await closeAll();
        await command("aet-eaw-edit.lsp.selectProject");
        const pick = await quickPick();
        expect(pick.title).toBe("EaWEdit project");
        expect(pick.placeholder).toBe("Select a project");
        expect(pick.rows.map((r) => r.label)).toEqual([EAW, FOC]);
        for (const row of pick.rows) expect(row.description).toMatch(/\.pgproj$/);
        await pickQuickItem(FOC);
    });

    it("uses the picked project with no editor in front", async () => {
        await showEffectiveObject();
        await browser.waitUntil(async () => (await activeTab()) === tab(FOC), { timeout: 30_000 });
    });

    it("follows a new pick", async () => {
        await command("aet-eaw-edit.lsp.selectProject");
        await quickPick();
        await pickQuickItem(EAW);
        await closeAll();
        await showEffectiveObject();
        await browser.waitUntil(async () => (await activeTab()) === tab(EAW), { timeout: 30_000 });
    });
});
