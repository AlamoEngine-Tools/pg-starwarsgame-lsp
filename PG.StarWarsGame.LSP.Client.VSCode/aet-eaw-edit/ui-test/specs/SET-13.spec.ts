// SET-13: an unreachable baseline source falls back to the cached copy; with none cached, the baseline
// is empty and the bug report says so first. Changing the source restarts the server (#395); a local
// type with no path yet stops no server - it is reported, the server goes on answering, and the path
// restarts it on the local copy.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { repo } from "../paths.js";
import {
    copyBugReport,
    extensionLog,
    lspStatus,
    notification,
    notifications,
    serverReady,
    serverStarts,
    setSettings,
    setStartupSettings,
} from "../protocol.js";

const url = "aet-eaw-edit.lsp.source.baseline.url";
const type = "aet-eaw-edit.lsp.source.baseline.type";
const localPath = "aet-eaw-edit.lsp.source.baseline.localPath";
const INCOMPLETE =
    'EaWEdit: Baseline type is "local" but no path is configured. Set "aet-eaw-edit.lsp.source.baseline.localPath".';

describe("SET-13", () => {
    let logged = 0;

    before(async () => {
        await serverReady();
        logged = extensionLog().length;
    });

    it("reads the cached copy when the source cannot be reached", async () => {
        const cached = "http://127.0.0.1:9/aet-pg-swg-lsp-foc-baseline.aet";
        await setStartupSettings({ [url]: cached });
        expect(extensionLog()).toContain(`Failed to download baseline from '${cached}'; trying cache`);
        expect(await copyBugReport()).toContain("- Baseline: Cache, custom");
    });

    it("is empty when nothing is cached under the name, and warns first", async () => {
        await setStartupSettings({ [url]: "http://127.0.0.1:9/nothing-cached.aet" });
        const report = await copyBugReport();
        expect(report).toMatch(/\*\*Warnings\*\*[\s\S]*- Baseline: Empty/);
        expect(report).toContain("- Baseline: Empty, custom");
    });

    it("looks up no release while a url is set", () => {
        expect(extensionLog().slice(logged)).not.toMatch(/Baseline release v\d|No baseline release of/);
    });

    it("keeps the server running on a local type with no path, and restarts on the path", async () => {
        const starts = serverStarts();
        await setSettings({ [type]: "local" });
        await notification((m) => m === INCOMPLETE, 30_000);
        await browser.pause(5_000);
        expect((await notifications()).filter((n) => n.message === INCOMPLETE).length).toBe(1);
        expect(serverStarts()).toBe(starts);
        expect(await lspStatus()).toBe("$(check) EaWEdit LSP");
        // It answers, on the configuration it started with: the empty baseline the test before left.
        const report = await copyBugReport();
        expect(report).not.toContain("- Server: Not running");
        expect(report).toContain("- Baseline: Empty, custom");

        await setStartupSettings({ [localPath]: path.join(repo, "baseline") });
        expect(await copyBugReport()).toContain("- Baseline: Local");
    });

    after(async () => {
        await setSettings({ [url]: undefined, [type]: undefined, [localPath]: undefined });
    });
});
