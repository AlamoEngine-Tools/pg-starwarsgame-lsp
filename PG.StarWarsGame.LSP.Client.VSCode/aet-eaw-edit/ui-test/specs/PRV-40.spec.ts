// PRV-40 (#380): a sample an SFXEvent names by its game-relative path is counted among the file's references on
// the Audio page, which lists the file by its name - as a texture named by path already is. The lookup took the
// written path as its key, so a sample's References read 0 while the event plainly named it.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";
import {
    openOutline,
    rail,
    railChildren,
    railClick,
    railExpand,
    rows,
    search,
    selectRow,
    tableReady,
} from "./modBrowser.js";

const sample = "sample_speed1_eng.wav";

describe("PRV-40", () => {
    before(async () => {
        await serverReady();
    });

    it("counts the SFXEvent naming the sample by path among its references", async () => {
        await openOutline("Assets");
        await inNewestWebview(async () => {
            await tableReady();
            await railClick("Audio");
            await tableReady();
            await search(sample);
            const row = (await rows()).find(
                (r) => r.cells[0].toLowerCase() === sample,
            );
            expect(row).toBeDefined();
            const name = row!.cells[0];

            await selectRow(name);
            await railExpand(name);
            // An asset's users are counted when the relation is opened, which is how the report
            // reached its 0: the node reads nothing until then.
            await railExpand("References");
            expect(
                (await railChildren("References")).map((n) => n.label),
            ).toEqual(["Unit_Speed_Sample"]);
        });
    });
});
