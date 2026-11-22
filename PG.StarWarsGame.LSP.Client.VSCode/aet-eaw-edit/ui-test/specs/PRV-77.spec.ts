// PRV-77: a unit whose hull is drawn opens with no note that nothing is drawn. The Star Destroyer's weapon
// models arrive before its hull does; a part that has arrived but hangs nowhere yet draws nothing, and the
// preview said "This model has no visible mesh" on the strength of that moment and kept saying it.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { press } from "./preview.js";
import { pause } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

/** What the preview draws, and the notes its dock header offers. Run inside the preview. */
async function state(): Promise<{
    meshes: number;
    parts: number;
    tags: string[];
}> {
    return browser.execute(() => {
        const v = (
            window as unknown as {
                aetDebugViewport: {
                    stats: () => { meshes: number; parts: number };
                };
            }
        ).aetDebugViewport;
        const stats = v.stats();
        return {
            meshes: stats.meshes,
            parts: stats.parts,
            tags: Array.from(document.querySelectorAll(".validate-btn")).map(
                (b) => b.getAttribute("title") ?? "",
            ),
        };
    });
}

/**
 * The text of every note, once the severity tag has opened them. The tag is there with nothing to read
 * too, and then offers no notes. Run inside the preview.
 */
async function notes(): Promise<string[]> {
    const offered: boolean = await browser.execute(() =>
        /^Read \d+ notes?$/.test(
            document.querySelector(".validate-btn")?.getAttribute("title") ??
                "",
        ),
    );
    if (!offered) {
        return [];
    }
    await press(".validate-btn");
    await browser
        .$(".preview-problems .problem-row")
        .waitForExist({ timeout: 10_000 });
    return browser.execute(() =>
        Array.from(
            document.querySelectorAll(".preview-problems .problem-row"),
        ).map((row) =>
            ((row as HTMLElement).innerText ?? "").replace(/\s+/g, " ").trim(),
        ),
    );
}

describe("PRV-77", () => {
    before(async () => {
        await serverReady();
    });

    it("says nothing about a model with no visible mesh on a unit whose hull is drawn", async () => {
        await command(
            "aet-eaw-edit.lsp.previewAssembledUnit",
            "Star_Destroyer",
        );
        await inNewestWebview(async () => {
            let quiet = 0;
            await browser.waitUntil(
                async () => {
                    const done: boolean = await browser.execute(() => {
                        const v = (
                            window as unknown as {
                                aetDebugViewport?: {
                                    parts: Map<string, unknown>;
                                };
                            }
                        ).aetDebugViewport;
                        return (
                            v !== undefined &&
                            v.parts.size > 0 &&
                            document.querySelector(".load-cover") === null
                        );
                    });
                    quiet = done ? quiet + 1 : 0;
                    return quiet >= 3;
                },
                {
                    timeout: 180_000,
                    interval: 1000,
                    timeoutMsg: "The Star Destroyer did not finish loading",
                },
            );
            await pause(1_500);

            const loaded = await state();
            const said = await notes();
            console.log("PRV-77 loaded: " + JSON.stringify({ loaded, said }));

            // The hull and its weapons are on screen.
            expect(loaded.parts).toBeGreaterThan(0);
            expect(loaded.meshes).toBeGreaterThan(0);
            expect(
                said.filter((note) => note.includes("no visible mesh")),
            ).toEqual([]);
        });
    });
});
