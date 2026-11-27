// SET-39: a game directory set one folder too deep, or to a folder with no Data in it, is reported (#393).
// The settings ask for the folder that holds the game's Data. Set to Data itself, the server found no
// archive of the game and the preview said nothing about why. The fixture's base game directory is a Data
// folder; its expansion directory holds no Data at all.
import { expect } from "@wdio/globals";
import { notification, serverReady } from "../protocol.js";

const game = "C:\\temp\\aet-test-SET-gamedir\\Game";

describe("SET-39", () => {
    before(async () => {
        await serverReady();
    });

    it("names the folder above a game directory that is a Data folder", async () => {
        const shown = await notification((m) => m.includes("aet-eaw-edit.lsp.source.baseGameDirectory"));
        expect(shown.type).toBe("warning");
        expect(shown.message).toBe(
            `EaWEdit: "aet-eaw-edit.lsp.source.baseGameDirectory" points at the Data folder itself - ${game}\\GameData\\Data. ` +
                `Set it to the folder above - ${game}\\GameData`,
        );
    });

    it("reports a game directory that holds no Data folder", async () => {
        const shown = await notification((m) => m.includes("aet-eaw-edit.lsp.source.expansionDirectory"));
        expect(shown.type).toBe("warning");
        expect(shown.message).toBe(
            `EaWEdit: "aet-eaw-edit.lsp.source.expansionDirectory" holds no Data folder - ${game}\\corruption. ` +
                "Fix it, or clear it to find the game by itself",
        );
    });
});
