// PRV-75: the damage state and the detail level are picked from a pill's list on the stage's bottom edge,
// not along a slider. The pill shows the level alone; what the level draws is in the list's rows and in the
// tooltip. On the vanilla EaW workspace: the stormtrooper model, which defines three detail levels.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

interface Pill {
    /** The level the viewport draws, and the levels its geometry defines. */
    lod: number;
    defined: number[];
    /** Whether the pill is there, and what it shows and says. */
    found: boolean;
    shown: string;
    title: string;
    /** The list's rows, and the value it holds. */
    rows: { value: string; text: string }[];
    value: string;
    /** How many sliders the model state plate still holds, and how tall the plate is. */
    sliders: number;
    plateHeight: number;
}

/** Waits for the geometry to arrive and the load cover to come off. Run inside the preview. */
async function loaded(): Promise<void> {
    await browser.waitUntil(
        async () =>
            browser.execute(() => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const v = (window as any).aetDebugViewport;
                return v !== undefined && v.parts.size > 0 && document.querySelector(".load-cover") === null;
            }),
        { timeout: 120_000, timeoutMsg: "The model did not load" },
    );
    await browser.pause(2_000);
}

/** The detail pill as it stands. Run inside the preview. */
async function pill(): Promise<Pill> {
    return browser.execute(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const v = (window as any).aetDebugViewport;
        const plate = document.querySelector(".subject-state");
        const label = document.querySelector<HTMLElement>(".pill-select.detail");
        const list = label?.querySelector("select") ?? null;
        return {
            lod: v.levels.lod as number,
            defined: v.definedLevels().lod as number[],
            found: label !== null,
            shown: (label?.querySelector(".pill-value")?.textContent ?? "").trim(),
            title: label?.title ?? "",
            rows: [...(list?.options ?? [])].map((option) => ({ value: option.value, text: option.text })),
            value: list?.value ?? "",
            sliders: plate?.querySelectorAll('input[type="range"]').length ?? -1,
            plateHeight: Math.round(plate?.getBoundingClientRect().height ?? -1),
        };
    });
}

describe("PRV-75", () => {
    before(async () => {
        await serverReady();
    });

    it("picks the detail level from a pill's list, with what each level draws in its rows", async () => {
        await openInPreview(model("Ei_trooper.alo"));
        await inNewestWebview(async () => {
            await loaded();
            const opened = await pill();
            console.log("PRV-75 opened: " + JSON.stringify(opened));

            const highest = opened.defined[opened.defined.length - 1];
            expect(opened.found).toBe(true);
            expect(opened.sliders).toBe(0);
            expect(opened.defined.length).toBeGreaterThan(1);

            // The pill holds the level alone; the tooltip and the rows say what it draws.
            expect(opened.shown).toBe(String(highest));
            expect(opened.value).toBe(String(highest));
            expect(opened.title.startsWith(`LOD ${highest} (`)).toBe(true);
            expect(opened.title).toContain("tris)");
            expect(opened.title.endsWith("0 is lowest detail in EaW")).toBe(true);
            expect(opened.rows.map((row) => row.value)).toEqual(opened.defined.map(String));
            expect(opened.rows.every((row) => /^\d+ \([\d,]+ mesh(es)?, [\d,]+ tris?\)$/.test(row.text))).toBe(true);

            // One row of 22px controls inside the plate's padding and border.
            expect(opened.plateHeight).toBeLessThanOrEqual(30);

            // A pick with the keyboard, on the list itself: one row up is the next level down.
            await browser.execute(() =>
                document.querySelector<HTMLSelectElement>(".pill-select.detail select")?.focus(),
            );
            await browser.keys("ArrowUp");
            const below = opened.defined[opened.defined.length - 2];
            await browser.waitUntil(async () => (await pill()).lod === below, {
                timeout: 5_000,
                timeoutMsg: "The detail level did not change",
            });
            const picked = await pill();
            console.log("PRV-75 picked: " + JSON.stringify(picked));

            expect(picked.shown).toBe(String(below));
            expect(picked.title.startsWith(`LOD ${below} (`)).toBe(true);
        });
        await closeAll();
    });
});
