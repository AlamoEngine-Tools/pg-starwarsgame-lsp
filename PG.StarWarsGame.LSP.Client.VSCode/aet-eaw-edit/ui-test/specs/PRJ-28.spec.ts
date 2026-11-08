// PRJ-28 (#364): a monorepo - the library's project file at the repository root, the mods in
// subfolders, none of them referenced by the root - opened as the root plus two of its mods. Every
// project runs its own server; the root's server serves nothing inside the mods.
import { browser, expect } from "@wdio/globals";
import { findingOn, logChannels, openFile, outputChannels, projectsReady } from "../protocol.js";

const mono = "C:\\temp\\aet-test-PRJ-mono";
const coreUnits = `${mono}\\Data\\Xml\\CoreUnits.xml`;
const trUnits = `${mono}\\TR\\Data\\Xml\\TRUnits.xml`;

/** The top-level rows of a view, each collapsed so the next is rendered (a long tree renders a part). */
async function topLevel(section: string): Promise<string[]> {
    const workbench = await browser.getWorkbench();
    const view = await (await workbench.getActivityBar().getViewControl("Empire at War Edit"))!.openView();
    const pane = await view.getContent().getSection(section);
    await pane.expand();
    await browser.waitUntil(async () => (await pane.getVisibleItems()).length > 0, { timeout: 60_000 });
    for (let i = 0; i < 10; i++) {
        const expanded = await browser.execute((title: string) => {
            const pane = Array.from(document.querySelectorAll(".pane")).find(
                (p) => (p.querySelector(".pane-header .title")?.textContent ?? "").trim().toLowerCase() === title.toLowerCase(),
            );
            const rows = Array.from(pane?.querySelectorAll('.monaco-list-row[aria-level="1"][aria-expanded="true"]') ?? []);
            rows.forEach((r) => (r.querySelector(".monaco-tl-twistie") as HTMLElement | null)?.click());
            return rows.length;
        }, section);
        if (expanded === 0) break;
        await browser.pause(500);
    }
    return browser.execute((title: string) => {
        const pane = Array.from(document.querySelectorAll(".pane")).find(
            (p) => (p.querySelector(".pane-header .title")?.textContent ?? "").trim().toLowerCase() === title.toLowerCase(),
        );
        return Array.from(pane?.querySelectorAll('.monaco-list-row[aria-level="1"]') ?? []).map((r) =>
            (r.querySelector(".label-name, .monaco-highlighted-label")?.textContent ?? "").trim(),
        );
    }, section);
}

/** Every finding on a file, as the Problems view holds them. */
async function sourcesOf(file: string): Promise<string[]> {
    const all: { source: string }[] = await browser.executeWorkbench(
        (vscode, file: string) =>
            vscode.languages.getDiagnostics(vscode.Uri.file(file)).map((d: any) => ({ source: d.source ?? "" })),
        file,
    );
    return [...new Set(all.map((d) => d.source))].sort();
}

describe("PRJ-28", () => {
    before(async () => {
        await projectsReady(3);
    });

    it("starts one server per project, none served as a dependency", async () => {
        const log = outputChannels().get("EaWEdit") ?? "";
        expect(log).toMatch(/\] Projects: Mono Core, Mono Rev, Mono TR$/m);
        expect(log).not.toContain("served as a dependency");
        const channels = [...logChannels().keys()];
        for (const project of ["Mono Core", "Mono Rev", "Mono TR"]) {
            expect(channels).toContain(`Alamo Engine Tools - Empire at War Edit (${project})`);
        }
    });

    it("lists the three projects in the navigators", async () => {
        for (const view of ["Campaign Editor", "Localisation Editor", "Mod Browser"]) {
            const items = await topLevel(view);
            expect(items.filter((i) => i.startsWith("Mono "))).toEqual(["Mono Core", "Mono Rev", "Mono TR"]);
        }
    });

    it("gives a mod's file to the mod's server alone, and the root's file to the root's", async () => {
        await openFile(trUnits);
        const tr = await findingOn(trUnits, 3, "aetswg-006-0012");
        expect(tr.source).toBe("aetswg (Mono TR)");
        await browser.pause(5_000);
        expect(await sourcesOf(trUnits)).toEqual(["aetswg (Mono TR)"]);

        await openFile(coreUnits);
        const core = await findingOn(coreUnits, 3, "aetswg-006-0012");
        expect(core.source).toBe("aetswg (Mono Core)");
        await browser.pause(5_000);
        expect(await sourcesOf(coreUnits)).toEqual(["aetswg (Mono Core)"]);
    });
});
