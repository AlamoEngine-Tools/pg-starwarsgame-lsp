// PRV-38 (#166): a mod whose mega texture is a Windows bitmap under the .tga name - how two shipped mods keep
// theirs, and the game shows their icons - gets its icons read: the card draws the mod's own portrait from the
// atlas, titled as its own, rather than the missing-icon placeholder the fallback to the base game's icons left
// it with (that fallback drew no portrait with the icon's name at all).
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const icon = "i_button_bmp.tga";

interface Portrait {
    alt: string;
    title: string;
    src: string;
}

describe("PRV-38", () => {
    before(async () => {
        await serverReady();
    });

    it("draws the mod's own portrait on the card", async () => {
        await command("aet-eaw-edit.lsp.showEncyclopedia", "Bmp_Unit");
        const portraits = await inNewestWebview<Portrait[]>(async () => {
            await browser.waitUntil(
                async () => (await browser.execute((sel: string) => document.querySelectorAll(sel).length, `img[alt="${icon}" i]`)) > 0,
                { timeout: 60_000, timeoutMsg: "The card drew no portrait" },
            );
            return browser.execute((name: string) =>
                Array.from(document.querySelectorAll("img"))
                    .filter((i) => (i.getAttribute("alt") ?? "").toLowerCase() === name)
                    .map((i) => ({
                        alt: i.getAttribute("alt") ?? "",
                        title: i.parentElement?.getAttribute("title") ?? "",
                        src: (i.getAttribute("src") ?? "").slice(0, 22),
                    })),
            icon);
        });
        expect(portraits).toHaveLength(1);
        expect(portraits[0].src).toMatch(/^data:image\/png/);
        expect(portraits[0].title).toBe(`${portraits[0].alt} - from this project's mega texture`);
    });

    after(async () => {
        await closeAll();
    });
});
