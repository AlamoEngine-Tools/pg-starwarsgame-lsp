// SET-12: with no baseline setting the baseline comes from the newest release, else the default branch. The
// branch seen is recorded in the run's output; before the 0.5.0 baseline release an EaW project refuses the
// default branch's baseline, so the rest skips as SET-04 does.
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { command, copyBugReport, logLine, restartServer, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";

const RELEASED = /Baseline release v5\.\d+\.0 of AlamoEngine-Tools\/eaw-baseline resolved/;
const BRANCH = /No baseline release of AlamoEngine-Tools\/eaw-baseline inside >=5\.0\.0 <6\.0\.0 available; using its default branch/;
const cached = path.join(os.homedir(), ".aetswg", "baselines", "aet-pg-swg-lsp-eaw-baseline.aet");

describe("SET-12", () => {
    let before = 0;

    it("resolves the baseline from the newest release or the default branch", async () => {
        await serverReady();
        before = fs.existsSync(cached) ? fs.statSync(cached).mtimeMs : 0;
        await restartServer();
        const seen = await logLine(new RegExp(`${RELEASED.source}|${BRANCH.source}`), "Alamo Engine Tools - Empire at War Edit");
        console.log(`SET-12 baseline lookup: ${RELEASED.test(seen) ? "release" : "default branch"} - ${seen.trim()}`);
    });

    it("reports a network baseline and loads the icon pack on a preview", async function () {
        const report = await copyBugReport();
        if (report.includes("- Project: BaselineRefused")) this.skip();
        expect(report).toMatch(/- Baseline: Network, official, built .+, manifest \S+/);
        expect(report).toContain("- Icon pack: NotLoaded, official");
        expect(fs.existsSync(cached) && fs.statSync(cached).mtimeMs >= before).toBe(true);

        await command("aet-eaw-edit.lsp.showEncyclopedia", "Corellian_Corvette");
        await inNewestWebview(async () => {
            await (await browser.$('input[aria-label="Screen"]')).waitForExist({ timeout: 120_000 });
        });
        expect(await copyBugReport()).toContain("- Icon pack: Network, official");
    });
});
