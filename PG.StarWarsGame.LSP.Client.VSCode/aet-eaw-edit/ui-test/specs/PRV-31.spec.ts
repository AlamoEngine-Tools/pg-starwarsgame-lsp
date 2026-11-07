// PRV-31: data the game's reader has no case for is a warning in the preview, which still draws the file.
// The Problems view half (Re-validate Workspace) is
// FocPreviewProtocolTest.PRV_31_DataTheGamesReaderHasNoCaseFor_IsAWarningOnReValidate.
import path from "node:path";
import { expect } from "@wdio/globals";
import { foc, serverReady } from "../protocol.js";
import { openInPreview, previewNotes } from "./preview.js";
import { emittersWhen } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

describe("PRV-31", () => {
    before(async () => {
        await serverReady();
    });

    it("draws P_tactical_corrupt and warns of the property each emitter skips", async () => {
        await openInPreview(path.join(foc, "Data", "Art", "Models", "P_tactical_corrupt.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.drawn > 0), 120_000, "The effect was never drawn");
            const notes = await previewNotes(2);
            expect(notes.map((n) => n.tone)).toEqual(["warning", "warning"]);
            expect(notes.map((n) => n.id)).toEqual(["aetswg-014-0022", "aetswg-014-0022"]);
            expect(notes[0].text).toMatch(/: Emitter 0 - property 0x49 not read by the game, skipped$/);
            expect(notes[1].text).toMatch(/: Emitter 1 - property 0x49 not read by the game, skipped$/);
        });
    });
});
