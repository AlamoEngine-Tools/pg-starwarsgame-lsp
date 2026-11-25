// PRV-80: the fields of a stage flyout stay inside it (#222). The weapon flyout's projectile search is a text
// field as wide as its row, and the flyout's own padding for a field came on top of that width: the field ran
// past the flyout's edge, and the flyout scrolled sideways.
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { openFlyout } from "./flyouts.js";
import { inNewestWebview } from "./storyGraph.js";

interface Overflow {
    /** How far the body's content runs past its own width. */
    sideways: number;
    /** The fields whose box ends right of the body's content edge, by placeholder or type. */
    past: string[];
}

/** What of the weapon flyout does not fit in it. Run inside the preview. */
async function overflow(): Promise<Overflow> {
    return browser.execute(() => {
        const flyout = Array.from(document.querySelectorAll<HTMLElement>(".stage-flyout")).find(
            (f) => f.getAttribute("aria-label") === "Weapon",
        );
        const body = flyout?.querySelector<HTMLElement>(".stage-flyout-body");
        if (!body) throw new Error("No weapon flyout");
        const edge = body.getBoundingClientRect().left + body.clientLeft + body.clientWidth - parseFloat(getComputedStyle(body).paddingRight);
        return {
            sideways: body.scrollWidth - body.clientWidth,
            past: Array.from(body.querySelectorAll<HTMLElement>("input, select"))
                .filter((field) => field.getBoundingClientRect().right > edge + 0.5)
                .map((field) => field.getAttribute("placeholder") ?? field.getAttribute("type") ?? field.tagName.toLowerCase()),
        };
    });
}

describe("PRV-80", () => {
    before(async () => {
        await serverReady();
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Nebulon_B_Frigate");
    });

    it("keeps every field of the weapon flyout inside it, so it does not scroll sideways", async () => {
        await inNewestWebview(async () => {
            const position = await browser.$('button.rotary-pos[title^="Gameplay"]');
            await position.waitForClickable({ timeout: 120_000 });
            await position.click();
            await openFlyout('button[title="Weapon settings"]', "Weapon");
            expect(await overflow()).toEqual({ sideways: 0, past: [] });
        });
    });
});
