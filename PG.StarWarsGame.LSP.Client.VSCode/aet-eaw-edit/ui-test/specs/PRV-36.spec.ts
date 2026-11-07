// PRV-36: a particle file with the Particle Editor's link group opens and plays; an unknown chunk is skipped
// with a warning; a foreign chunk in the emitter list is refused. The files are the setup's, made from
// P_fire_small00. The Problems view half is
// ParticleEditorProtocolTest.PRV_36_TheLinkGroupIsReadPast_AnUnknownChunkSkipped_AForeignListChunkRefused.
import path from "node:path";
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { emitters, openInPreview, previewNotes } from "./preview.js";
import { emitterProbes, emittersWhen } from "./particles.js";
import { inNewestWebview } from "./storyGraph.js";

const models = path.join("C:\\temp\\aet-prv-pe", "Data", "Art", "Models");

describe("PRV-36", () => {
    before(async () => {
        await serverReady();
    });

    it("plays the linked file like P_fire_small00 and notes the link group and the skipped chunk", async () => {
        await openInPreview(path.join(models, "P_prv_linked.alo"));
        await inNewestWebview(async () => {
            expect(await emitters()).toEqual(["smoke", "fire", "firesoft", "sparks"]);
            await emittersWhen((e) => e.some((x) => x.drawn > 0), 120_000, "The effect was never drawn");
            const notes = await previewNotes(2);
            expect(notes.map((n) => n.tone).sort()).toEqual(["info", "warning"]);
            expect(notes.find((n) => n.tone === "info")?.id).toBe("aetswg-014-0023");
            expect(notes.find((n) => n.tone === "warning")?.id).toBe("aetswg-014-0022");
            expect(notes.find((n) => n.tone === "info")?.text).toMatch(
                /: Emitter 0 - link group 3, Particle Editor data the game does not read$/,
            );
            expect(notes.find((n) => n.tone === "warning")?.text).toMatch(/: Emitter 0 - chunk 0x7F not read by the game, skipped$/);
        });
    });

    // The refusal is the preview's one note, an error; nothing is drawn.
    it("refuses the broken file and says why", async () => {
        await openInPreview(path.join(models, "P_prv_broken.alo"));
        await inNewestWebview(async () => {
            const notes = await previewNotes(1);
            expect(notes.map((n) => n.tone)).toEqual(["error"]);
            expect(notes[0].text).toContain("Malformed ALO file: the emitter list holds unexpected chunk 0x7F.");
            expect(await emitterProbes()).toEqual([]);
        });
    });
});
