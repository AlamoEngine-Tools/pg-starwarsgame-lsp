// PRV-61 (#465): a mod that ships a model of the base game under its own name, and none of its animation
// files, is offered the base game's clips. The fixture's mod holds the walker alone, spelt EV_AT-AT.ALO; its
// thirty clips are the base game's, listed by the game data and read from the game folder. PRV-62 is the same
// mod with game folders that hold nothing.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { aboutClips, clips, loaded, MODEL } from "./animationClips.js";
import { openInPreview, previewNotesWhen } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

describe("PRV-61", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("offers a mod's copy of a base-game model the base game's clips", async () => {
        await openInPreview(MODEL);
        await inNewestWebview(async () => {
            await loaded();
            const names = await clips();
            console.log("PRV-61 clips: " + JSON.stringify(names));
            expect(names).toHaveLength(30);
            expect(names).toContain("ev_at-at_idle_00");
            expect(names).toContain("ev_at-at_die_02");

            // Every clip was read, so nothing is said of unread ones.
            const notes = await previewNotesWhen(
                () => true,
                "The notes never opened",
            );
            console.log("PRV-61 notes: " + JSON.stringify(notes));
            expect(aboutClips(notes)).toEqual([]);
        });
    });
});
