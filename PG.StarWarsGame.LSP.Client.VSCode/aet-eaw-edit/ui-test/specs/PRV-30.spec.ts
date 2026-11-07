// PRV-30 (mod-bound): the story graph's mode switch dims a mode that cannot be entered - Edit, on a campaign
// with threads in a project the leaf only references (all of them or part: either blocks Edit, in its own
// words). Campaign, faction and owner are local.json's.
import { browser, expect } from "@wdio/globals";
import { localValues } from "../paths.js";
import { serverReady } from "../protocol.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const mod = localValues<{ campaign: string; faction: string; owner: string }>("PRV-30");

interface Position {
    title: string;
    disabled: boolean;
    active: boolean;
    opacity: number;
    cursor: string;
}

/** The switch's positions as drawn now. Run inside the story graph. */
async function positions(): Promise<Position[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll<HTMLButtonElement>("button.rotary-pos")).map((b) => {
            const style = getComputedStyle(b);
            return {
                title: b.getAttribute("title") ?? "",
                disabled: b.disabled,
                active: b.classList.contains("active"),
                opacity: Number(style.opacity),
                cursor: style.cursor,
            };
        }),
    );
}

describe("PRV-30", () => {
    before(async () => {
        await serverReady();
    });

    it("dims Edit on a graph that belongs to a referenced project", async () => {
        await openStoryGraph(mod.campaign, mod.faction);
        await inNewestWebview(async () => {
            await (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 });
            await graphLoaded();
            const before = await positions();
            const edit = before.find(
                (p) =>
                    p.title.includes(`'${mod.owner}', which this workspace only references.`) &&
                    p.title.endsWith("Open the graph in that project to edit it."),
            );
            expect(edit).toMatchObject({ disabled: true, active: false, cursor: "default" });
            const reason = edit!.title;
            const enabled = before.filter((p) => !p.disabled && !p.active);
            expect(enabled.length).toBeGreaterThan(0);
            for (const p of enabled) {
                expect(p.cursor).toBe("pointer");
                expect(edit!.opacity).toBeLessThan(p.opacity);
            }
            const view = before.find((p) => p.active)!;
            expect(view.title.startsWith("View")).toBe(true);

            // Hovered, the dimmed position stays as it is; an enabled one brightens.
            const editButton = await browser.$(`button.rotary-pos[title="${reason}"]`);
            await editButton.moveTo();
            await browser.pause(400);
            expect((await positions()).find((p) => p.title === reason)!.opacity).toBe(edit!.opacity);
            const other = await browser.$(`button.rotary-pos[title="${enabled[0].title}"]`);
            await other.moveTo();
            await browser.pause(400);
            expect((await positions()).find((p) => p.title === enabled[0].title)!.opacity).toBe(1);

            // Clicked, nothing happens: the graph stays in View.
            await editButton.click().catch(() => undefined);
            await browser.pause(1_000);
            expect((await positions()).find((p) => p.active)!.title).toBe(view.title);
        });
    });
});
