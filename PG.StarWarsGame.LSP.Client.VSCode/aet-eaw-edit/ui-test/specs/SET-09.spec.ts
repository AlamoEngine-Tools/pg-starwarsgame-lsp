// SET-09: the story simulator's two options are toggles in its transport; the old setting keys are unknown and
// change nothing. The Empire story pauses on its first battle at tick 0, which freezes One tick and Restart, so
// the run's option surviving them stays a hand check on a campaign that starts on the galaxy.
import { browser, expect } from "@wdio/globals";
import { editUserSettingsJson, serverReady, unknownSettings } from "../protocol.js";
import { closeAll, graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const OLD = ["aet-eaw-edit.storySimulator.autoResume", "aet-eaw-edit.storySimulator.assumeMediaCompletes"];

/** The transport's three toggles, after its run controls. Run inside the graph. */
async function toggles(): Promise<{ title: string; pressed: boolean }[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".sim-transport button[aria-pressed]")).map((b) => ({
            title: b.getAttribute("title") ?? "",
            pressed: b.getAttribute("aria-pressed") === "true",
        })),
    );
}

async function intoSimulation(): Promise<void> {
    await openStoryGraph("Full_Story_Campaign_Empire", "Empire");
}

async function press(title: string): Promise<void> {
    await browser.execute(
        (t: string) => document.querySelector<HTMLButtonElement>(`.sim-transport button[title="${t}"]`)?.click(),
        title,
    );
    await browser.pause(1_000);
}

describe("SET-09", () => {
    before(async () => {
        await serverReady();
        editUserSettingsJson({ [OLD[0]]: false, [OLD[1]]: false });
    });

    it("marks the old keys unknown", () => {
        expect(unknownSettings()).toEqual(expect.arrayContaining(OLD));
    });

    it("offers the options as toggles, both on whatever the old keys say", async () => {
        await intoSimulation();
        await inNewestWebview(async () => {
            await graphLoaded();
            await (await browser.$('button.rotary-pos[title="Simulation"]')).click();
            await (await browser.$(".sim-transport")).waitForExist({ timeout: 60_000 });
            expect(await toggles()).toEqual([
                { title: "Break on gates", pressed: false },
                { title: "Resume after an answer", pressed: true },
                { title: "Speech and movies finish on their own", pressed: true },
            ]);

            await press("Speech and movies finish on their own");
            expect((await toggles())[2].pressed).toBe(false);
            await press("Resume after an answer");
            expect((await toggles())[1].pressed).toBe(false);
            // Kept per viewer: the choice goes to the webview's storage after the render.
            await browser.pause(2_000);
        });
        await closeAll();
    });

    it("keeps 'Resume after an answer' off in a reopened panel", async () => {
        await intoSimulation();
        await inNewestWebview(async () => {
            await graphLoaded();
            await (await browser.$('button.rotary-pos[title="Simulation"]')).click();
            await (await browser.$(".sim-transport")).waitForExist({ timeout: 60_000 });
            expect((await toggles())[1]).toEqual({ title: "Resume after an answer", pressed: false });
        });
    });

    after(() => {
        editUserSettingsJson({ [OLD[0]]: null, [OLD[1]]: null });
    });
});
