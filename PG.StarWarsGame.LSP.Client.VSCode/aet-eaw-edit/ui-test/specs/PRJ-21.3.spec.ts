// PRJ-21 (mod-bound), part 3 of 3: the core again reads its own snapshot and parses nothing.
import { expect } from "@wdio/globals";
import { copyBugReport, serverReady } from "../protocol.js";
import { snapshots } from "./snapshots.js";

describe("PRJ-21 part 3", () => {
    it("starts the core again from its snapshot", async () => {
        await serverReady();
        const report = await copyBugReport("Basic + extended stats");
        expect(report).toMatch(/- Caches: .*index snapshots 1 of 1 layers, \d+ files reused, 0 parsed/);
        expect(Object.keys(snapshots()).length).toBeLessThanOrEqual(8);
    });
});
