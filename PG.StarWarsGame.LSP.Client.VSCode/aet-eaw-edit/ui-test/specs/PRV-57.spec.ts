// PRV-57: a unit and a model open at their HIGHEST detail level - Alamo numbers LOD the other way up, so
// LOD0 is the lowest. A preview closed while its geometry was still on its way recorded the level shown
// until then - the 0 every subject starts at before the opening rule has chosen - and the next open of the
// same subject in the window restored it, so it came back at LOD0. A level the reader picked is still
// restored. On the vanilla EaW workspace: the AT-AT unit, the SPMAT model and the stormtrooper model.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

interface Detail {
    /** The level the viewport draws. */
    lod: number;
    /** The levels the loaded geometry tags, ascending. */
    defined: number[];
    /** The detail slider's position and its last stop. */
    value: string;
    max: string;
    /** The level the slider's plate names. */
    label: string;
}

/** Opens a unit's assembled preview by its object name. */
async function openUnit(name: string): Promise<void> {
    await browser.executeWorkbench((vscode, unit: string) => {
        void vscode.commands.executeCommand(
            "aet-eaw-edit.lsp.previewAssembledUnit",
            unit,
        );
    }, name);
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
                    v.parts.size > 0 &&
                    document.querySelector(".load-cover") === null
                );
            }),
        { timeout: 120_000, timeoutMsg: "The model did not load" },
    );
    await browser.pause(2_000);
}

/** The detail level on screen and the levels on offer. Run inside the preview. */
async function detail(): Promise<Detail> {
    return browser.execute(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const v = (window as any).aetDebugViewport;
        const input = document.querySelector<HTMLInputElement>(
            '.stage-field.detail input[type="range"]',
        );
        return {
            lod: v.levels.lod as number,
            defined: v.definedLevels().lod as number[],
            value: input?.value ?? "",
            max: input?.max ?? "",
            label: (
                document.querySelector(".stage-field.detail .stage-value")
                    ?.textContent ?? ""
            ).trim(),
        };
    });
}

/** Asserts the preview shows the highest level its geometry defines. */
function expectHighest(shown: Detail): void {
    const highest = shown.defined[shown.defined.length - 1];
    expect(shown.defined.length).toBeGreaterThan(1);
    expect(shown.lod).toBe(highest);
    expect(shown.value).toBe(shown.max);
    expect(shown.label.startsWith(`${highest} (`)).toBe(true);
}

/**
 * Opens a subject and closes it once its scene is on screen and before any of its geometry has arrived.
 * Returns how many parts had arrived at the close, which the caller holds at zero.
 */
async function closeBeforeGeometry(open: () => Promise<void>): Promise<number> {
    await open();
    const parts = await inNewestWebview(async () => {
        await browser.waitUntil(
            async () =>
                browser.execute(() =>
                    (
                        document.querySelector(".load-cover")?.textContent ?? ""
                    ).includes("Loading geometry"),
                ),
            {
                timeout: 60_000,
                interval: 20,
                timeoutMsg: "The scene never arrived",
            },
        );
        return browser.execute(() => {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const v = (window as any).aetDebugViewport;
            return v.parts.size as number;
        });
    });
    await closeAll();
    // The panel's last state reaches the host as a message after the close.
    await browser.pause(2_000);
    return parts;
}

describe("PRV-57", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    // First, while nothing is converted yet: a model the server has already converted arrives too fast
    // to close before it.
    it("reopens a model closed before its geometry arrived at its highest detail level", async () => {
        const file = model("Ev_spmat.alo");
        const arrived = await closeBeforeGeometry(() => openInPreview(file));
        console.log(`PRV-57 Ev_spmat closed with ${arrived} parts arrived`);
        expect(arrived).toBe(0);

        await openInPreview(file);
        await inNewestWebview(async () => {
            await loaded();
            const shown = await detail();
            console.log("PRV-57 Ev_spmat reopened: " + JSON.stringify(shown));
            expectHighest(shown);
        });
        await closeAll();
    });

    it("reopens a unit closed before its geometry arrived at its highest detail level", async () => {
        const arrived = await closeBeforeGeometry(() =>
            openUnit("AT_AT_Walker"),
        );
        console.log(`PRV-57 AT_AT_Walker closed with ${arrived} parts arrived`);
        expect(arrived).toBe(0);

        await openUnit("AT_AT_Walker");
        await inNewestWebview(async () => {
            await loaded();
            const shown = await detail();
            console.log(
                "PRV-57 AT_AT_Walker reopened: " + JSON.stringify(shown),
            );
            expectHighest(shown);
        });
        await closeAll();
    });

    it("opens a unit at its highest detail level", async () => {
        await openUnit("AT_ST_Walker");
        await inNewestWebview(async () => {
            await loaded();
            const shown = await detail();
            console.log("PRV-57 AT_ST_Walker opened: " + JSON.stringify(shown));
            expectHighest(shown);
        });
        await closeAll();
    });

    it("reopens a model at the detail level the reader picked", async () => {
        const file = model("Ei_trooper.alo");
        await openInPreview(file);
        await inNewestWebview(async () => {
            await loaded();
            // The slider's first stop, LOD0, the way a drag sets it.
            await browser.execute(() => {
                const input = document.querySelector<HTMLInputElement>(
                    '.stage-field.detail input[type="range"]',
                );
                if (input === null) {
                    throw new Error("No detail slider");
                }
                const setter = Object.getOwnPropertyDescriptor(
                    HTMLInputElement.prototype,
                    "value",
                )!.set!;
                setter.call(input, "0");
                input.dispatchEvent(new Event("input", { bubbles: true }));
            });
            await browser.waitUntil(async () => (await detail()).lod === 0, {
                timeout: 5_000,
                timeoutMsg: "The detail slider did not move",
            });
            // The pick reaches the host as a message after the render.
            await browser.pause(2_000);
        });
        await closeAll();
        await browser.pause(2_000);

        await openInPreview(file);
        await inNewestWebview(async () => {
            await loaded();
            const shown = await detail();
            console.log(
                "PRV-57 Ei_trooper reopened after a pick: " +
                    JSON.stringify(shown),
            );
            expect(shown.lod).toBe(0);
            expect(shown.value).toBe("0");
        });
        await closeAll();
    });
});
