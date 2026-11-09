// STY-01 (#362): a story script whose StoryModeEvents table holds two keys that differ only in case.
// The shipped library registers both and selects a state by the XML event's exact name
// (Pgstorymode.lua: StoryModeEvents[name]), so they are two states. The Campaign Editor loads the
// campaign, and the key no event spells the same way keeps its case-only-match warning.
import { browser, expect } from "@wdio/globals";
import { findingOn, openFile, serverReady, viewItems } from "../protocol.js";

const script = "C:\\temp\\aet-test-STY-dup\\Data\\Scripts\\Story\\Story_Dup.lua";

/** The campaigns under the Campaign Editor's "Ungrouped" row, which lists campaigns declaring no set. */
async function ungroupedCampaigns(): Promise<string[]> {
    const workbench = await browser.getWorkbench();
    const view = await (await workbench.getActivityBar().getViewControl("Empire at War Edit"))!.openView();
    const pane = await view.getContent().getSection("Campaign Editor");
    await pane.expand();
    const items = await (pane as any).openItem("Ungrouped");
    return Promise.all(items.map((i: any) => i.getLabel()));
}

describe("STY-01", () => {
    before(async () => {
        await serverReady();
    });

    it("lists the campaign in the Campaign Editor", async () => {
        const items = await viewItems("Campaign Editor");
        expect(items.filter((i) => i.startsWith("Cannot load"))).toEqual([]);
        expect(await ungroupedCampaigns()).toContain("Dup_Campaign");
    });

    it("warns on the key no event spells the same way", async () => {
        await openFile(script);
        const warning = await findingOn(script, 6, "aetswg-001-0036");
        expect(warning.severity).toBe(1);
        expect(warning.message).toContain("Story_Enter_1_SECTOR_FORCES");
    });
});
