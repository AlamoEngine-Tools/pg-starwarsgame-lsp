// PRJ-08: the navigators list one group per project. (The localisation tab title is checked with the
// localisation editor.)
import { browser, expect } from "@wdio/globals";
import { EAW, FOC, projectsReady } from "../protocol.js";

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

describe("PRJ-08", () => {
    it("groups each navigator by project", async () => {
        await projectsReady(2);
        for (const view of ["Campaign Editor", "Localisation Editor", "Mod Browser"]) {
            const items = await topLevel(view);
            expect(items.filter((i) => i === EAW || i === FOC)).toEqual([EAW, FOC]);
        }
    });
});
