// PRJ-16: a project file inside another project's directory is served as its dependency.
import { browser, expect } from "@wdio/globals";
import { command, copyBugReport, logChannels, notification, notifications, outputChannels, serverReady } from "../protocol.js";

describe("PRJ-16", () => {
    before(async () => {
        await serverReady();
    });

    it("runs one server with one project", async () => {
        await command("aet-eaw-edit.lsp.selectProject");
        await notification((m) => m === "EaWEdit: One project open", 15_000);
        expect([...logChannels().keys()]).toEqual(["Alamo Engine Tools - Empire at War Edit"]);
        expect(outputChannels().get("EaWEdit") ?? "").not.toMatch(/\] Projects:|^Projects:/m);
        await browser.pause(2_000);
        expect((await notifications()).filter((n) => n.type === "error")).toEqual([]);
    });

    it("serves the nested project as one dependency, two layers", async () => {
        const dependencies = (await copyBugReport("Basic + extended stats"))
            .split(/\r?\n/)
            .find((l) => l.startsWith("- Dependencies: "));
        expect(dependencies).toMatch(/^- Dependencies: 1 direct, 1 total, /);
        expect(dependencies).toContain("2 layers");
    });
});
