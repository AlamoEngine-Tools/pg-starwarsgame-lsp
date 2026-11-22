// PRV-58: a unit whose parts arrive one by one opens at its HIGHEST detail level. The fixture's unit is the
// AT-AT hull, which tags LOD0 and LOD1, with a hardpoint that attaches a crate pile tagging no level; the
// crate is a sixteenth of the hull's size, so its geometry arrives first. The opening rule chose the level
// on that first arrival, from the levels the crate alone defines - LOD0 - and kept it when the hull arrived,
// so the unit opened at its lowest detail. A level the reader picked is still restored on the next open.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const UNIT = "Prv_Lod_Walker";

interface Detail {
    /** The parts in the order their geometry arrived. */
    parts: string[];
    lod: number;
    defined: number[];
    value: string;
    max: string;
    label: string;
}

async function openUnit(): Promise<void> {
    await browser.executeWorkbench((vscode, unit: string) => {
        void vscode.commands.executeCommand(
            "aet-eaw-edit.lsp.previewAssembledUnit",
            unit,
        );
    }, UNIT);
}

/** Waits for the geometry to arrive and the load cover to come off. Run inside the preview. */
async function loaded(): Promise<void> {
    await browser.waitUntil(
        async () =>
            browser.execute(() => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const v = (window as any).aetDebugViewport;
                return (
                    v !== undefined &&
                    v.parts.size > 1 &&
                    document.querySelector(".load-cover") === null
                );
            }),
        { timeout: 120_000, timeoutMsg: "The unit did not load" },
    );
    await browser.pause(2_000);
}

/** The detail level on screen, the levels on offer and the arrival order. Run inside the preview. */
async function detail(): Promise<Detail> {
    return browser.execute(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const v = (window as any).aetDebugViewport;
        const input = document.querySelector<HTMLSelectElement>(
            ".pill-select.detail select",
        );
        return {
            parts: [...v.parts.keys()] as string[],
            lod: v.levels.lod as number,
            defined: v.definedLevels().lod as number[],
            value: input?.value ?? "",
            max: input?.options[input.options.length - 1]?.value ?? "",
            label: (input?.selectedOptions[0]?.textContent ?? "").trim(),
        };
    });
}

describe("PRV-58", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("opens a unit whose hardpoint model arrives first at its highest detail level", async () => {
        await openUnit();
        await inNewestWebview(async () => {
            await loaded();
            const shown = await detail();
            console.log("PRV-58 opened: " + JSON.stringify(shown));
            // The trigger: the hardpoint's model arrived before the hull.
            expect(shown.parts[0]).toBe("hp:HP_Prv_Lod_Crate");
            expect(shown.parts).toContain("hull");

            expect(shown.defined).toEqual([0, 1]);
            expect(shown.lod).toBe(1);
            expect(shown.value).toBe(shown.max);
            expect(shown.label.startsWith("1 (")).toBe(true);
        });
        await closeAll();
        await browser.pause(2_000);
    });

    it("reopens the unit at the detail level the reader picked", async () => {
        await openUnit();
        await inNewestWebview(async () => {
            await loaded();
            // The list's first row, LOD0, the way a pick sets it.
            await browser.execute(() => {
                const input = document.querySelector<HTMLSelectElement>(
                    ".pill-select.detail select",
                );
                if (input === null) {
                    throw new Error("No detail list");
                }
                const setter = Object.getOwnPropertyDescriptor(
                    HTMLSelectElement.prototype,
                    "value",
                )!.set!;
                setter.call(input, "0");
                input.dispatchEvent(new Event("change", { bubbles: true }));
            });
            await browser.waitUntil(async () => (await detail()).lod === 0, {
                timeout: 5_000,
                timeoutMsg: "The detail level did not change",
            });
            // The pick reaches the host as a message after the render.
            await browser.pause(2_000);
        });
        await closeAll();
        await browser.pause(2_000);

        await openUnit();
        await inNewestWebview(async () => {
            await loaded();
            const shown = await detail();
            console.log(
                "PRV-58 reopened after a pick: " + JSON.stringify(shown),
            );
            expect(shown.parts[0]).toBe("hp:HP_Prv_Lod_Crate");
            expect(shown.lod).toBe(0);
            expect(shown.value).toBe("0");
        });
        await closeAll();
    });
});
