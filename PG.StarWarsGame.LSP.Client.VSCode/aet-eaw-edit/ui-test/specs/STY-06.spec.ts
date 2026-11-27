// STY-06: the story graph's colour key opens at the pill that opens it (#222) - the gap clear of the pill's
// plate, upwards from the bottom edge, and centred on the pill unless the stage's side edge stops it first,
// with a tail on its edge that points at the pill.
// Before, it hung from a fixed corner of the stage.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { misplaced, openFlyout, tailOff } from "./flyouts.js";
import { graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

describe("STY-06", () => {
    before(async () => {
        await serverReady();
        await openStoryGraph("Sandbox_Alderaans_Demise_Rebel", "Rebel");
    });

    it("opens the colour key above its pill", async () => {
        await inNewestWebview(async () => {
            await graphLoaded();
            const key = await openFlyout('.key-corner button[title="Colour key"]', "Colour key");
            expect(misplaced("Colour key", key)).toEqual([]);
            expect(tailOff("Colour key", key)).toEqual([]);
        });
    });
});
