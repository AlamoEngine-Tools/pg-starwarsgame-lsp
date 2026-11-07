// The localisation and credits grids, as the SET and PRJ tests read them. All but openTextFile run inside the
// grid's webview.
import { browser } from "@wdio/globals";

/**
 * Opens a file under the Localisation Editor view by its path of tree labels, as a reader does. Walked by
 * row level: the view expands its rows by itself, and a project and its own layer share a name, so the
 * page object's openItem picked the layer row 'Alpha' for the project row 'Alpha'.
 */
export async function openTextFile(...labels: string[]): Promise<void> {
    const workbench = await browser.getWorkbench();
    const container = await workbench.getActivityBar().getViewControl("Empire at War Edit");
    const view = await container!.openView();
    const pane = await view.getContent().getSection("Localisation Editor");
    await pane.expand();
    await browser.waitUntil(async () => (await pane.getVisibleItems()).length > 0, {
        timeout: 120_000,
        timeoutMsg: "The Localisation Editor lists nothing",
    });
    let after = -1;
    for (let depth = 0; depth < labels.length; depth++) {
        const last = depth === labels.length - 1;
        let found = -1;
        await browser.waitUntil(
            async () => {
                found = await browser.execute(
                    (label: string, level: number, from: number) => {
                        const section = Array.from(document.querySelectorAll(".pane")).find((p) =>
                            /Localisation Editor/i.test(p.querySelector(".pane-header")?.textContent ?? ""),
                        );
                        const rows = Array.from(section?.querySelectorAll(".monaco-list-row") ?? []);
                        document.querySelectorAll("[data-aet-row]").forEach((r) => r.removeAttribute("data-aet-row"));
                        for (let i = from + 1; i < rows.length; i++) {
                            const rowLevel = Number(rows[i].getAttribute("aria-level"));
                            if (rowLevel < level) return -1;
                            const text = (rows[i].querySelector(".label-name")?.textContent ?? "").trim();
                            if (rowLevel === level && text === label) {
                                rows[i].setAttribute("data-aet-row", "");
                                return i;
                            }
                        }
                        return -1;
                    },
                    labels[depth],
                    depth + 1,
                    after,
                );
                return found >= 0;
            },
            { timeout: 30_000, timeoutMsg: `No '${labels.slice(0, depth + 1).join(" > ")}' in the Localisation Editor` },
        );
        const row = await browser.$("[data-aet-row]");
        if (last || (await row.getAttribute("aria-expanded")) === "false") {
            await row.click();
            await browser.pause(800);
        }
        after = found;
    }
}

/** The grid's column headers - a sortable text grid's and an ordered credits grid's alike. */
/** The Localisation Editor view's rows as drawn: label and tree level. */
export async function treeRows(): Promise<{ label: string; level: number }[]> {
    return browser.execute(() => {
        const section = Array.from(document.querySelectorAll(".pane")).find((p) =>
            /Localisation Editor/i.test(p.querySelector(".pane-header")?.textContent ?? ""),
        );
        return Array.from(section?.querySelectorAll(".monaco-list-row") ?? []).map((r) => ({
            label: (r.querySelector(".label-name")?.textContent ?? "").trim(),
            level: Number(r.getAttribute("aria-level")),
        }));
    });
}

/** The grid's column headers - a sortable text grid's and an ordered credits grid's alike. */
export async function columns(): Promise<string[]> {
    await (await browser.$(".head-row")).waitForExist({ timeout: 120_000 });
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".head-row > .cell:not(.column-menu)")).map((c) => (c.textContent ?? "").trim()),
    );
}

/** Whether the header offers sorting, as a text grid's does and an ordered credits grid's does not. */
export async function sortable(): Promise<boolean> {
    return browser.execute(() => document.querySelector(".head-row .cell.sortable") !== null);
}

/** The content tiles a credits file offers: label and badge. */
export async function contentTiles(): Promise<{ label: string; badge: string }[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".dock-tile.with-badge")).map((t) => ({
            label: (t.querySelector(".tile-label")?.textContent ?? "").trim(),
            badge: (t.querySelector(".tile-badge")?.textContent ?? "").trim(),
        })),
    );
}

/** The grid's footer line: rows, rows filtered out, filters active. */
export async function footer(): Promise<string> {
    return browser.execute(() => (document.querySelector(".grid-footer")?.textContent ?? "").trim());
}

/** Every row drawn, its cells joined with ' | ' - an input cell (a key, a value) by its value. */
export async function rowTexts(): Promise<string[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".rows .data-row")).map((r) =>
            Array.from(r.querySelectorAll(".cell"))
                .map((c) => (c.querySelector("input")?.value ?? c.textContent ?? "").trim())
                .join(" | "),
        ),
    );
}

/** Types into the grid's row filter. */
export async function filterRows(text: string): Promise<void> {
    await (await browser.$("input.filter")).setValue(text);
    await browser.pause(1_000);
}

/** The Inherited toggle: its tooltip, and whether it is ticked. */
export async function inherited(): Promise<{ title: string; checked: boolean }> {
    return browser.execute(() => {
        const label = document.querySelector("label.inherited-toggle");
        return {
            title: label?.getAttribute("title") ?? "",
            checked: label?.querySelector("input")?.checked ?? false,
        };
    });
}

export async function toggleInherited(): Promise<void> {
    await browser.execute(() => document.querySelector<HTMLInputElement>("label.inherited-toggle input")?.click());
    await browser.pause(1_000);
}

/** The languages the column menu offers. */
export async function columnChoices(): Promise<string[]> {
    await browser.execute(() =>
        document.querySelector<HTMLButtonElement>('button[title="Choose which language columns to show"]')?.click(),
    );
    await (await browser.$('.column-flyout[role="menu"]')).waitForExist({ timeout: 5_000 });
    const choices: string[] = await browser.execute(() =>
        Array.from(document.querySelectorAll(".column-flyout label.check-field")).map((l) => (l.textContent ?? "").trim()),
    );
    await browser.keys("Escape");
    return choices;
}

/** Presses a dock tile by its label. */
export async function pressTile(label: string): Promise<void> {
    await browser.execute((name: string) => {
        const tile = Array.from(document.querySelectorAll<HTMLButtonElement>(".dock-tile")).find(
            (t) => (t.querySelector(".tile-label")?.textContent ?? "").trim() === name,
        );
        if (!tile) throw new Error(`No tile '${name}'`);
        tile.click();
    }, label);
}

/** Types a value into a row's language cell, by the row's key, and commits it as Enter does. */
export async function editCell(key: string, value: string): Promise<void> {
    await browser.waitUntil(
        async () =>
            browser.execute((wanted: string) => {
                for (const row of Array.from(document.querySelectorAll(".rows .data-row"))) {
                    const cells = Array.from(row.querySelectorAll(".cell"));
                    const keyText = (cells[0]?.querySelector("input")?.value ?? cells[0]?.textContent ?? "").trim();
                    if (keyText !== wanted) continue;
                    const input = cells[1]?.querySelector("input");
                    if (!input) return false;
                    document.querySelectorAll("[data-aet-target]").forEach((e) => e.removeAttribute("data-aet-target"));
                    input.setAttribute("data-aet-target", "");
                    return true;
                }
                return false;
            }, key),
        { timeout: 60_000, timeoutMsg: `No row '${key}' with a language cell` },
    );
    // Selected and typed over, as a reader does: setValue appends to what a controlled input holds.
    const input = await browser.$("[data-aet-target]");
    await input.click();
    await browser.keys(["Control", "a"]);
    await browser.keys(value.split(""));
    await browser.keys("Enter");
    await browser.pause(500);
}

/** Presses Save in the dock header. */
export async function save(): Promise<void> {
    await browser.execute(() => document.querySelector<HTMLButtonElement>('button[title^="Save - "]')?.click());
}

/** The Save button's tooltip, which says what is staged. */
export async function saveTitle(): Promise<string> {
    return browser.execute(() => document.querySelector('button[title^="Save - "]')?.getAttribute("title") ?? "");
}

/** Confirms the panel's own dialog by its button's label. */
export async function confirmDialog(label: string): Promise<void> {
    const button = await browser.$(`//div[contains(@class,'modal')]//button[normalize-space()='${label}']`);
    await button.waitForExist({ timeout: 10_000 });
    await button.click();
}
