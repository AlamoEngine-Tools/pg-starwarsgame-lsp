// PRV-13: editor data the game reads past is an information note in the preview, which still draws the
// file. The Problems view half (Re-validate Workspace, the 18 files' 31 entries) is
// EawPreviewProtocolTest.PRV_13_EditorDataTheGameReadsPast_IsInformationOnReValidate.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { model, openInPreview, previewNotes } from "./preview.js";
import { emittersWhen } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

describe("PRV-13", () => {
    before(async () => {
        await serverReady();
    });

    // The file's texture is named by an artist's Max path, which names no file (as PRV-17): a warning beside it.
    it("draws P_smoke_med00 and notes the property the game reads past", async () => {
        await openInPreview(model("P_smoke_med00.alo"));
        await inNewestWebview(async () => {
            await emittersWhen((e) => e.some((x) => x.drawn > 0), 120_000, "The smoke was never drawn");
            const notes = await previewNotes(2);
            const info = notes.filter((n) => n.tone === "info");
            expect(info.map((n) => n.id)).toEqual(["aetswg-014-0023"]);
            expect(info[0].text).toMatch(/: Emitter 0 - property 0x06, which the game reads past$/);
            expect(notes.filter((n) => n.tone === "warning").map((n) => n.text)).toEqual([
                expect.stringMatching(/^Texture 'c:\\art\\textures\\game\\p_particle_master\.tga' was not found\./),
            ]);
        });
    });
});
