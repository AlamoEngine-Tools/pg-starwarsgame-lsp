// PRV-62 (#465): animation files the game data lists for a model, and nothing can read, are said. The mod is
// PRV-61's - the walker alone, none of its clips - with both game folders set to a folder that holds nothing.
// The model opened with no animations and not a word: the thirty clips the game data lists were dropped one by
// one where their bytes were looked for. The preview now names them and says where files are read from.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { aboutClips, clips, loaded, MODEL } from "./animationClips.js";
import { openInPreview, previewNotesWhen } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

describe("PRV-62", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("names the animation files of the game data that could not be read", async () => {
        await openInPreview(MODEL);
        await inNewestWebview(async () => {
            await loaded();
            expect(await clips()).toEqual([]);

            const notes = await previewNotesWhen(
                (n) => aboutClips(n).length > 0,
                "No note spoke of the animation files",
            );
            console.log("PRV-62 notes: " + JSON.stringify(notes));
            const said = aboutClips(notes);
            expect(said).toHaveLength(1);
            expect(said[0].tone).toBe("warning");
            expect(said[0].text).toMatch(/\b30\b/);
            expect(said[0].text).toMatch(/ev_at-at_attack_00\.ala/i);
            // Where files are read from, so the reader knows what to set.
            expect(said[0].text).toMatch(/game/i);
        });
    });
});
