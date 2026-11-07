// PRJ-02: memory telemetry in the extended status; the same per-layer document counts on a cold and a
// warm start, the warm one parsing nothing.
import fs from "node:fs";
import path from "node:path";
import { expect } from "@wdio/globals";
import { copyBugReport, eaw, notification, restartServer, serverReady } from "../protocol.js";

function line(report: string, prefix: string): string {
    return report.split(/\r?\n/).find((l) => l.startsWith(prefix)) ?? "";
}

function documentsPerLayer(report: string): number[] {
    return [...line(report, "- Layers: ").matchAll(/rank \d+ (\d+) documents/g)].map((m) => Number(m[1]));
}

describe("PRJ-02", () => {
    let cold = "";
    let warm = "";

    before(async () => {
        await serverReady();
        fs.rmSync(path.join(eaw, ".aetswg", "indices"), { recursive: true, force: true });
        await restartServer();
        cold = await copyBugReport("Basic + extended stats");
        await notification((m) => m === "EaWEdit: Bug report info copied", 15_000);
        await restartServer();
        warm = await copyBugReport("Basic + extended stats");
    });

    it("reports memory, layers and cache entries", () => {
        for (const report of [cold, warm]) {
            expect(report).toContain("**Extended stats**");
            const memory = line(report, "- Memory: heap ");
            for (const part of ["committed", "fragmented", "large object heap", "working set", "gen2 collections"])
                expect(memory).toContain(part);
            expect(line(report, "- Layers: ")).toMatch(/^- Layers: rank 0 \d+ documents, \d+ symbols, \d+ references/);
            expect(line(report, "- Cache entries: ")).not.toBe("");
        }
    });

    it("counts the same documents per layer cold and warm", () => {
        expect(documentsPerLayer(cold).length).toBeGreaterThan(0);
        expect(documentsPerLayer(warm)).toEqual(documentsPerLayer(cold));
    });

    it("parses on the cold start and reuses on the warm one", () => {
        expect(line(cold, "- Caches: ")).toMatch(/[1-9]\d* parsed/);
        expect(line(warm, "- Caches: ")).toMatch(/\b0 parsed/);
        expect(line(warm, "- Caches: ")).toMatch(/[1-9]\d* files reused/);
    });
});
