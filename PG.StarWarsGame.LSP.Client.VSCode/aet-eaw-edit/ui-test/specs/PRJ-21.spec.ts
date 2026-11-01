// PRJ-21 (mod-bound), part 1 of 3: the shared core library alone writes its snapshot. Folders are local.json's.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { keep, snapshots } from "./snapshots.js";

describe("PRJ-21 part 1", () => {
    it("indexes the core alone and keeps its snapshot", async () => {
        await serverReady();
        const seen = snapshots();
        expect(Object.keys(seen).length).toBeGreaterThan(0);
        keep("core", seen);
    });
});
