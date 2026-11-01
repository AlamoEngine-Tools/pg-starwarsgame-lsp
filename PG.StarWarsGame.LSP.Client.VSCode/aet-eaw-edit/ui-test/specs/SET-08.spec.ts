// SET-08: Story Editing restarts the server and adds Edit to the story graph, with its Save button.
import { browser, expect } from "@wdio/globals";
import { logLine, serverReady, serverStarts, setSettings } from "../protocol.js";
import { closeAll, inNewestWebview, openStoryGraph, storyModes } from "./storyGraph.js";

async function toggleRestarts(value: boolean): Promise<void> {
    const starts = serverStarts();
    await setSettings({ "aet-eaw-edit.experimental.storyEditing": value });
    await logLine(/Experimental features or executables changed - restarting LSP server\./);
    await browser.waitUntil(async () => serverStarts() > starts, { timeout: 60_000, timeoutMsg: "No restart" });
    await serverReady();
}

describe("SET-08", () => {
    before(async () => {
        await serverReady();
    });

    it("restarts the server when Story Editing is ticked", async () => {
        await toggleRestarts(true);
    });

    it("offers View and Edit, and Edit shows the Save button with nothing staged", async () => {
        await openStoryGraph("Main_Campaign_Empire", "Empire");
        const save = await inNewestWebview(async () => {
            await (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 });
            const modes = await browser.execute(() =>
                Array.from(document.querySelectorAll("button.rotary-pos")).map((b) => b.getAttribute("title")),
            );
            expect(modes).toEqual(["View", "Edit"]);
            await (await browser.$('button.rotary-pos[title="Edit"]')).click();
            const button = await browser.$('[title^="Save - "]');
            await button.waitForExist({ timeout: 15_000 });
            return { title: await button.getAttribute("title"), enabled: await button.isEnabled() };
        });
        expect(save).toEqual({ title: "Save - nothing is staged", enabled: false });
        await closeAll();
    });

    it("restarts again when it is unticked, and a new graph offers View only", async () => {
        await toggleRestarts(false);
        expect(await storyModes("Main_Campaign_Empire", "Empire")).toEqual(["View"]);
    });
});
