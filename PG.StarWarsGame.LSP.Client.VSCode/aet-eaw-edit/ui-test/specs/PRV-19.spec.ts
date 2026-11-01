// PRV-19: the old encyclopedia screen settings are unknown and change nothing.
import { browser, expect } from "@wdio/globals";
import { inNewestWebview } from "./storyGraph.js";
import { command, editUserSettingsJson, serverReady, unknownSettings } from "../protocol.js";

const OLD = ["aet-eaw-edit.encyclopedia.screenWidth", "aet-eaw-edit.encyclopedia.screenHeight"];

describe("PRV-19", () => {
    before(async () => {
        await serverReady();
        editUserSettingsJson({ [OLD[0]]: 1024, [OLD[1]]: 768 });
    });

    it("marks both keys unknown", () => {
        expect(unknownSettings()).toEqual(expect.arrayContaining(OLD));
    });

    it("draws the card for its own Screen field", async () => {
        await command("aet-eaw-edit.lsp.showEncyclopedia", "Nebulon_B_Frigate");
        await inNewestWebview(async () => {
            const field = await browser.$('input[aria-label="Screen"]');
            await field.waitForExist({ timeout: 120_000 });
            expect(await field.getValue()).toBe("1920x1080");
        });
    });

    after(() => {
        editUserSettingsJson({ [OLD[0]]: null, [OLD[1]]: null });
    });
});
