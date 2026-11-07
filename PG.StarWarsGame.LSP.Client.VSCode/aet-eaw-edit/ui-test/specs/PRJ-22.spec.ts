// PRJ-22 (mod-bound): the mod's five projects in one window - all indexed, no snapshot clash, each file
// diagnosed by its own project. The migration's modal question cannot be answered under the UI test (VS Code
// refuses a modal to an extension under test), so its decline stays a hand check. Names and files are local.json's.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { localValues } from "../paths.js";
import { findingOn, insertLineAfter, logChannels, logLine, projectsReady, revertAndClose, statusTooltip } from "../protocol.js";

const mod = localValues<{
    mod: string;
    projects: string[];
    leafFile: string;
    leafAfterLine: number;
    leafName: string;
    sharedFile: string;
    sharedAfterLine: number;
    sharedName: string;
}>("PRJ-22");

describe("PRJ-22", () => {
    before(async () => {
        await projectsReady(mod.projects.length, 600_000);
    });

    it("indexes the five projects, each ready", async () => {
        const tooltip = await statusTooltip();
        for (const name of mod.projects) expect(tooltip).toContain(`${name} - ready`);
        const line = await logLine(/Projects: /);
        expect(line.slice(line.indexOf("Projects: ") + "Projects: ".length).trim()).toBe(mod.projects.join(", "));
        expect([...logChannels().values()].join("\n")).not.toContain("Could not save project index snapshot");
    });

    it("diagnoses the leaf's file from the leaf, with no layer row", async () => {
        const file = path.join(mod.mod, mod.leafFile);
        await insertLineAfter(file, mod.leafAfterLine - 1, "\t\t<Zzq_Not_A_Tag>1</Zzq_Not_A_Tag>");
        const finding = await findingOn(file, mod.leafAfterLine, "aetswg-006-0012");
        expect(finding.source).toBe(`aetswg (${mod.leafName})`);
        expect(finding.related.filter((r) => r.startsWith("Layer - "))).toEqual([]);
        await revertAndClose(file);
    });

    // The shared project alone is a library no registry of its own names the file in: the game never reads it
    // there, so its project says so (aetswg-006-0017) and does not validate the tags a leaf would.
    it("diagnoses the shared project's file from that project alone, as a file it never reads", async () => {
        const file = path.join(mod.mod, mod.sharedFile);
        await insertLineAfter(file, mod.sharedAfterLine - 1, "\t\t<Zzq_Not_A_Tag>1</Zzq_Not_A_Tag>");
        await findingOn(file, 1, "aetswg-006-0017");
        await browser.pause(5_000);
        const all: { source: string; code: string }[] = await browser.executeWorkbench(
            (vscode, target: string) =>
                vscode.languages.getDiagnostics(vscode.Uri.file(target)).map((d: any) => ({
                    source: d.source ?? "",
                    code: String(typeof d.code === "object" ? d.code.value : (d.code ?? "")),
                })),
            file,
        );
        expect([...new Set(all.map((d) => d.source))]).toEqual([`aetswg (${mod.sharedName})`]);
        expect(all.filter((d) => d.code === "aetswg-006-0012")).toEqual([]);
        await revertAndClose(file);
    });
});
