// PRJ-21 (mod-bound), part 2 of 3: a leaf over the core writes a snapshot of its own context, leaving the
// core-alone one as it was.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { keep, kept_, snapshots } from "./snapshots.js";

describe("PRJ-21 part 2", () => {
    it("keeps the core-alone snapshot when the leaf starts", async () => {
        await serverReady();
        await browser.pause(5_000);
        const before = kept_("core");
        const now = snapshots();
        for (const [file, written] of Object.entries(before)) expect({ file, written: now[file] }).toEqual({ file, written });
        expect(Object.keys(now).length).toBeLessThanOrEqual(8);
        keep("leaf", now);
    });
});
