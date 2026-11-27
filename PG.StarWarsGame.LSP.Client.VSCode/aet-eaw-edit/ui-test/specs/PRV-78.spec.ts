// PRV-78: the dial carries what a lens does to everything it shows (#222). The scene reset is in the
// same slot in every lens; Gameplay adds Destroy all and Repair all at the other end, which left the
// Hardpoints section's title; and the reset puts a destroyed unit back.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import { press } from "./preview.js";

const RESET = "Reset the scene - put the subject back as it loaded";
const DESTROY = "Destroy all hardpoints";
const REPAIR = "Repair all hardpoints";
const NOTHING_DESTROYED = "Nothing is destroyed";

interface Action {
    slot: string;
    title: string;
    disabled: boolean;
}

/** The dial's actions as drawn: the slot each sits in, its tooltip, and whether it can be pressed. */
async function actions(): Promise<Action[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll<HTMLButtonElement>(".rotary button.rotary-action")).map((b) => ({
            slot: Array.from(b.classList).find((c) => c.startsWith("at-")) ?? "",
            title: b.getAttribute("title") ?? "",
            disabled: b.disabled,
        })),
    );
}

async function lens(name: string): Promise<void> {
    const position = await browser.$(`button.rotary-pos[title^="${name}"]`);
    await position.waitForClickable({ timeout: 120_000 });
    await position.click();
    await browser.pause(1000);
}

describe("PRV-78", () => {
    before(async () => {
        await serverReady();
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Nebulon_B_Frigate");
    });

    it("has the scene reset alone on the dial in Model and Animation, in its slot", async () => {
        await inNewestWebview(async () => {
            for (const name of ["Model", "Animation"]) {
                await lens(name);
                expect(await actions()).toEqual([{ slot: "at-start", title: RESET, disabled: false }]);
            }
        });
    });

    it("adds Destroy all and Repair all in Gameplay, and takes them out of the Hardpoints title", async () => {
        await inNewestWebview(async () => {
            await lens("Gameplay");
            await browser.waitUntil(async () => (await actions()).length === 3, {
                timeout: 30_000,
                timeoutMsg: "Gameplay does not have three dial actions",
            });
            expect(await actions()).toEqual([
                { slot: "at-start", title: RESET, disabled: false },
                { slot: "at-end", title: DESTROY, disabled: false },
                // Nothing is destroyed yet, so there is nothing to repair, and it says so.
                { slot: "at-end-2", title: NOTHING_DESTROYED, disabled: true },
            ]);
            const inTitle: number = await browser.execute(
                () => document.querySelectorAll(".dock-section-title button, .dock-section-title + button").length,
            );
            expect(inTitle).toBe(0);
        });
    });

    it("destroys every hardpoint from the dial, and the scene reset puts them back", async () => {
        await inNewestWebview(async () => {
            await press(`button.rotary-action[title="${DESTROY}"]`);
            await browser.waitUntil(async () => (await actions())[2]?.title === REPAIR, {
                timeout: 30_000,
                timeoutMsg: "Destroy all destroyed nothing",
            });
            expect((await actions())[2]).toEqual({ slot: "at-end-2", title: REPAIR, disabled: false });

            await press(`button.rotary-action[title="${RESET}"]`);
            await browser.waitUntil(async () => (await actions())[2]?.title === NOTHING_DESTROYED, {
                timeout: 30_000,
                timeoutMsg: "The scene reset left hardpoints destroyed",
            });
            // Still Gameplay, and still the same three in the same slots: the reset is of the subject.
            expect((await actions()).map((a) => a.slot)).toEqual(["at-start", "at-end", "at-end-2"]);
        });
    });
});
