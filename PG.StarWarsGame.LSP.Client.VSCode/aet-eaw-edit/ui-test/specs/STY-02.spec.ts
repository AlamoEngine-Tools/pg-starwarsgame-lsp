// STY-02 (#192): a mod whose GameConstants rename the good side to Republic. Rebel_Story_Name attaches its plot
// to the faction Good_Side_Name names - the campaign start of the 64-bit game loads that tag's plots for the
// player of that faction - so a generic pair for Republic attaches the same faction twice and is reported
// redundant, while a generic pair for Rebel, a faction that is no side here, is not.
import { expect } from "@wdio/globals";
import { findingOn, openFile, serverReady } from "../protocol.js";
import { diagnosticsWhen } from "./xmlEditor.js";

const campaigns = "C:\\temp\\aet-test-STY-sides\\Data\\XML\\Campaigns_Sides.xml";
const REDUNDANT = "aetswg-009-0010";

describe("STY-02", () => {
    before(async () => {
        await serverReady();
        await openFile(campaigns);
    });

    it("reports the Republic pair as attaching the side's faction again", async () => {
        const redundant = await findingOn(campaigns, 5, REDUNDANT);
        expect(redundant.message).toContain("Republic");
    });

    it("does not report the Rebel pair, which attaches another faction", async () => {
        const all = await diagnosticsWhen(campaigns, (d) => d.some((x) => x.code === REDUNDANT));
        expect(all.filter((x) => x.line === 7 && x.code === REDUNDANT)).toEqual([]);
    });
});
