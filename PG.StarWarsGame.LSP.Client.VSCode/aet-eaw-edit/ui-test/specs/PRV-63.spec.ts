// PRV-63 (#466): a hardpoint that names no Attachment_Bone is a finding only where something is lost. The
// fixture's unit is the base game's corvette with three hardpoints nobody can target, none of them naming an
// attachment bone: one fires from the hull's own bones and attaches no model, as the shipped corvette's
// guns do; one names no fire bone either; one names a model. The first was reported as an error beside
// the other two, though the game places it at its fire bone and has no model to leave off.
import { expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { type Note, previewNotesWhen } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const about = (notes: Note[], hardpoint: string): Note[] => notes.filter((n) => n.text.includes(`'${hardpoint}'`));

describe("PRV-63", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("says nothing of a hardpoint placed at its fire bone with no model to attach", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Prv_NoBone_Unit");
        await inNewestWebview(async () => {
            // The two that stay are the sign that the notes are in.
            const notes = await previewNotesWhen(
                (n) => about(n, "HP_Prv_NoBone_Nowhere").length > 0 && about(n, "HP_Prv_NoBone_Model").length > 0,
                "The notes of the two other hardpoints did not arrive",
            );
            console.log("PRV-63 notes: " + JSON.stringify(notes));

            const nowhere = about(notes, "HP_Prv_NoBone_Nowhere");
            expect(nowhere).toHaveLength(1);
            expect(nowhere[0].tone).toBe("error");
            expect(nowhere[0].text).toMatch(/map origin/i);

            const model = about(notes, "HP_Prv_NoBone_Model");
            expect(model).toHaveLength(1);
            expect(model[0].tone).toBe("error");
            expect(model[0].text).toMatch(/never attached/i);

            expect(about(notes, "HP_Prv_NoBone_Fires")).toEqual([]);
        });
    });
});
