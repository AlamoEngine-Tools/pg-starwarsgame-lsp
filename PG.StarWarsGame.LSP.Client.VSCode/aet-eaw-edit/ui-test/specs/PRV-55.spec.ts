// PRV-55 (#412): the Game renderer draws no shadow for a model without a shadow mesh, as the game draws
// none. The crate pile W_crates19.alo authors no shadow mesh: under Game no render mesh casts into the
// shadow map, the severity tag carries an information note saying so, and the shadow colour control is
// disabled with its reason. The stand-in shadow in Scene settings casts the shadow-map shadow anyway and the
// header marks it; it is disabled with its reason in the Default renderer and on the Lambda shuttle, which
// authors a shadow mesh, and off again in a new session. Before, the crates cast the Default renderer's
// shadow-map shadow under Game.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, openSceneSettings, press } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const NO_SHADOW_MESH = "No shadow mesh: The game draws no shadow for this model";
const NOTHING_CASTS = "Nothing casts a shadow: The game draws none without a shadow mesh";
const STAND_IN =
    "//*[contains(@class,'field')][.//text()[normalize-space()='Stand-in shadow']]//button[not(contains(@class,'info-badge'))]";

/** How many render meshes cast into the shadow map. Run inside the preview. */
async function casters(): Promise<number> {
    return browser.execute(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const v = (window as unknown as { aetDebugViewport: any }).aetDebugViewport;
        let count = 0;
        for (const part of v.parts.values()) {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            part.root.traverse((node: any) => {
                if (node.isMesh === true && node.userData.aetShadowVolume !== true && node.castShadow === true) {
                    count++;
                }
            });
        }
        return count;
    });
}

/** Waits for the model's parts to arrive. Run inside the preview. */
async function loaded(): Promise<void> {
    await browser.waitUntil(
        async () =>
            browser.execute(() => {
                const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown> } })
                    .aetDebugViewport;
                return v !== undefined && v.parts.size > 0;
            }),
        { timeout: 120_000, timeoutMsg: "The model did not load" },
    );
    await browser.pause(2_000);
}

/** Picks a position of the Renderer switch, by its label. */
async function pick(label: string): Promise<void> {
    await press(`//*[@aria-label='Which renderer draws the model']//button[normalize-space()='${label}']`);
    await browser.pause(1_000);
}

interface Control {
    found: boolean;
    disabled: boolean;
    pressed: string;
    title: string;
}

/** A control's state, by the field label it sits under. Run inside the preview. */
async function control(label: string, selector: string): Promise<Control> {
    return browser.execute(
        (name: string, inner: string) => {
            const field = Array.from(document.querySelectorAll(".field")).find((f) =>
                Array.from(f.querySelector(".field-label")?.childNodes ?? []).some(
                    (n) => n.nodeType === 3 && (n.textContent ?? "").trim() === name,
                ),
            );
            const el = field?.querySelector<HTMLButtonElement | HTMLInputElement>(inner);
            return {
                found: el !== null && el !== undefined,
                disabled: el?.disabled ?? false,
                pressed: el?.getAttribute("aria-pressed") ?? "",
                title: el?.getAttribute("title") ?? "",
            };
        },
        label,
        selector,
    );
}

const standIn = (): Promise<Control> => control("Stand-in shadow", "button:not(.info-badge)");
const shadowColour = (): Promise<Control> => control("Shadow colour", 'input[type="color"]');

/** The header's stand-in marker's title, or null when there is none. Run inside the preview. */
async function marker(): Promise<string | null> {
    return browser.execute(() => document.querySelector(".stand-in-mark")?.getAttribute("title") ?? null);
}

/** The notes as `tone: message`, or none when the tag is disabled. Run inside the preview. */
async function notes(): Promise<string[]> {
    const tag = await browser.$("button.validate-btn");
    if (!(await tag.isEnabled())) {
        return [];
    }
    await press("button.validate-btn");
    await (await browser.$(".problem-list")).waitForExist({ timeout: 5_000 });
    const rows: { tone: string; text: string }[] = await browser.execute(() =>
        Array.from(document.querySelectorAll(".problem-list .problem-row")).map((row) => ({
            tone: (row.querySelector('[class*="sev-"]')?.className.match(/sev-(\w+)/) ?? [])[1] ?? "",
            text: (row.querySelector(".problem-msg")?.textContent ?? "").trim(),
        })),
    );
    await press("button.validate-btn");
    return rows.map((r) => `${r.tone}: ${r.text}`);
}

describe("PRV-55", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("draws no shadow for the crate pile under Game, notes it and disables the shadow colour", async () => {
        await openInPreview(model("W_crates19.alo"));
        await inNewestWebview(async () => {
            await loaded();
            await openSceneSettings();
            await pick("Game");

            const cast = await casters();
            console.log(`PRV-55 crates under Game: ${cast} render meshes cast into the shadow map`);
            expect(cast).toBe(0);

            let found: string[] = [];
            await browser.waitUntil(async () => (found = await notes()).includes(`info: ${NO_SHADOW_MESH}`), {
                timeout: 10_000,
                timeoutMsg: "No information note about the missing shadow mesh",
            });
            console.log("PRV-55 crates notes: " + JSON.stringify(found));

            const colour = await shadowColour();
            const toggle = await standIn();
            console.log("PRV-55 crates controls: " + JSON.stringify({ colour, toggle }));
            expect(colour.disabled).toBe(true);
            expect(colour.title).toBe(NOTHING_CASTS);

            expect(toggle.found).toBe(true);
            expect(toggle.disabled).toBe(false);
            expect(toggle.pressed).toBe("false");
            expect(await marker()).toBeNull();
        });
    });

    it("casts the stand-in shadow on request, marks it and disables it under Default", async () => {
        await openInPreview(model("W_crates19.alo"));
        await inNewestWebview(async () => {
            await loaded();
            await openSceneSettings();
            await pick("Game");

            await press(STAND_IN);
            await browser.waitUntil(async () => (await standIn()).pressed === "true", { timeout: 5_000 });
            expect(await casters()).toBeGreaterThan(0);
            expect(await marker()).toBe("Stand-in shadow: The game draws none for this model");
            expect((await shadowColour()).title).not.toBe(NOTHING_CASTS);
            expect(await notes()).toContain(`info: ${NO_SHADOW_MESH}`);

            await pick("Default");
            const toggle = await standIn();
            expect(toggle.disabled).toBe(true);
            expect(toggle.title).toBe("The Default renderer always casts a shadow");
            expect(await casters()).toBeGreaterThan(0);
            expect(await marker()).toBeNull();
            expect(await notes()).not.toContain(`info: ${NO_SHADOW_MESH}`);

            await pick("Game");
            // The choice reaches the host as a message after the render; closing at once drops it.
            await browser.pause(2_000);
        });
    });

    it("leaves the shuttle's stencil shadow alone and disables the stand-in there", async () => {
        await closeAll();
        await openInPreview(model("Ev_lambdashuttle.alo"));
        await inNewestWebview(async () => {
            await loaded();
            await openSceneSettings();
            await pick("Game");

            expect(await casters()).toBe(0);
            const toggle = await standIn();
            expect(toggle.disabled).toBe(true);
            expect(toggle.title).toBe("The shadow mesh casts the game's own shadow");
            expect(await marker()).toBeNull();
            expect(await notes()).not.toContain(`info: ${NO_SHADOW_MESH}`);
            await browser.pause(2_000);
        });
    });

    it("starts a new session with the stand-in shadow off", async () => {
        await closeAll();
        await openInPreview(model("W_crates19.alo"));
        await inNewestWebview(async () => {
            await loaded();
            await openSceneSettings();
            await pick("Game");

            expect((await standIn()).pressed).toBe("false");
            expect(await casters()).toBe(0);
            expect(await marker()).toBeNull();
        });
    });
});
