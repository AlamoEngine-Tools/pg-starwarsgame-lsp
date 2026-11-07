// SET-13: an unreachable baseline source falls back to the cached copy; with none cached, the baseline
// is empty and the bug report says so first.
import { expect } from "@wdio/globals";
import { copyBugReport, extensionLog, restartServer, serverReady, setSettings } from "../protocol.js";

const url = "aet-eaw-edit.lsp.source.baseline.url";

describe("SET-13", () => {
    let logged = 0;

    before(async () => {
        await serverReady();
        logged = extensionLog().length;
    });

    it("reads the cached copy when the source cannot be reached", async () => {
        const cached = "http://127.0.0.1:9/aet-pg-swg-lsp-foc-baseline.aet";
        await setSettings({ [url]: cached });
        await restartServer();
        expect(extensionLog()).toContain(`Failed to download baseline from '${cached}'; trying cache`);
        expect(await copyBugReport()).toContain("- Baseline: Cache, custom");
    });

    it("is empty when nothing is cached under the name, and warns first", async () => {
        await setSettings({ [url]: "http://127.0.0.1:9/nothing-cached.aet" });
        await restartServer();
        const report = await copyBugReport();
        expect(report).toMatch(/\*\*Warnings\*\*[\s\S]*- Baseline: Empty/);
        expect(report).toContain("- Baseline: Empty, custom");
    });

    it("looks up no release while a url is set", () => {
        expect(extensionLog().slice(logged)).not.toMatch(/Baseline release v\d|No baseline release of/);
    });

    after(async () => {
        await setSettings({ [url]: undefined });
    });
});
