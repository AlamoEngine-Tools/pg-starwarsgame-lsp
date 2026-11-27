// PRV-83: the encyclopedia card's zoom is the shared zoom slider, with the zoom in percent beside it, as the
// story graph's is (#222). It follows the wheel over the card and zooms the card. Before, the zoom was a
// slider under a label that said "Zoom 3.00x".
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";

const SLIDER = ".right-dock .dock-overview .zoom-slider";

async function readout(): Promise<string> {
    return browser.execute(
        (selector: string) => (document.querySelector(`${selector} .zoom-readout`)?.textContent ?? "").trim(),
        SLIDER,
    );
}

describe("PRV-83", () => {
    before(async () => {
        await serverReady();
        await command("aet-eaw-edit.lsp.showEncyclopedia", "Nebulon_B_Frigate");
    });

    it("shows the card's zoom in percent and zooms the card from the slider", async () => {
        await inNewestWebview(async () => {
            await (await browser.$(SLIDER)).waitForExist({ timeout: 120_000 });
            expect(await readout()).toBe("300%");

            await browser.execute((selector: string) => {
                const input = document.querySelector<HTMLInputElement>(`${selector} input`)!;
                Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, "2");
                input.dispatchEvent(new Event("input", { bubbles: true }));
            }, SLIDER);
            await browser.pause(400);
            expect(await readout()).toBe("200%");

            // The wheel over the card moves the slider with it.
            await browser.execute(() => {
                const stage = document.querySelector(".preview-stage")!;
                stage.dispatchEvent(new WheelEvent("wheel", { deltaY: -100, bubbles: true, cancelable: true }));
            });
            await browser.pause(400);
            expect(await readout()).toBe("225%");
        });
    });
});
