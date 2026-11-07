// PRJ-19 (mod-bound): a dependency's file is diagnosed by the leaf with its layer named; the archives counted by
// the game's rule. The mod's names, files and counts are local.json's.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { localValues } from "../paths.js";
import { findingOn, insertLineAfter, logLine, revertAndClose, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";

const SERVER = "Alamo Engine Tools - Empire at War Edit";
const mod = localValues<{
    mod: string;
    leaf: string;
    dependency: string;
    dependencyFile: string;
    afterLine: number;
    packed: number;
    archives: number;
}>("PRJ-19");
const file = path.join(mod.mod, mod.dependencyFile);

describe("PRJ-19", () => {
    before(async () => {
        await serverReady();
    });

    it("counts the packed assets of the archives the game loads", async () => {
        expect(await logLine(/Asset catalog:/, SERVER)).toContain(`(${mod.packed} from MEG archives)`);
    });

    it("diagnoses the dependency's file from the leaf, naming its layer", async () => {
        await insertLineAfter(file, mod.afterLine - 1, "\t\t<Zzq_Not_A_Tag>1</Zzq_Not_A_Tag>");
        const finding = await findingOn(file, mod.afterLine, "aetswg-006-0012");
        expect(finding.message).toContain("<Zzq_Not_A_Tag> is not a known SpaceUnit tag.");
        expect(finding.source).toBe(`aetswg (${mod.leaf})`);
        expect(finding.related).toContain(`Layer - ${mod.dependency}`);
        await revertAndClose(file);
    });

    it("indexes the archives on the first preview", async () => {
        await browser.executeWorkbench((vscode) => {
            void vscode.commands.executeCommand("aet-eaw-edit.lsp.previewModel", "Ev_lambdashuttle.alo");
        });
        await inNewestWebview(async () => {
            await (await browser.$(".stage-column")).waitForExist({ timeout: 120_000 });
        });
        expect(await logLine(/Indexed \d+ entries from \d+ MEG archive/, SERVER)).toMatch(
            new RegExp(`from ${mod.archives} MEG archive\\(s\\)`),
        );
    });
});
