// The Mod Browser panel, as the PRV tests read it. Everything but openOutline runs inside the panel's webview.
import { browser } from "@wdio/globals";
import { clickViewItem } from "../protocol.js";

/** Opens a project's browser on an outline, as a click in the Mod Browser view does - under the project's row when several run. */
export async function openOutline(outline: "Game Data" | "Assets" | "Scripts" | "Schema", project?: string): Promise<void> {
    await (project === undefined ? clickViewItem("Mod Browser", outline) : clickViewItem("Mod Browser", project, outline));
}

/** Waits until the table lists rows, bringing the table's own tab forward if an entry's is in front. */
export async function tableReady(timeout = 120_000): Promise<void> {
    await browser.execute(() => {
        const table = document.querySelector<HTMLElement>(".editor-tab.only-name:not(.on) .editor-tab-name");
        table?.click();
    });
    await browser.waitUntil(
        async () => (await browser.execute(() => document.querySelectorAll("table.data-table tbody tr").length)) > 0,
        { timeout, timeoutMsg: "The table lists nothing" },
    );
}

export async function columns(): Promise<string[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll("table.data-table th .th-label")).map((t) => (t.textContent ?? "").trim()),
    );
}

export interface TableRow {
    cells: string[];
    /** The tags in the Origin cell. */
    tags: string[];
    selected: boolean;
}

export async function rows(): Promise<TableRow[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll("table.data-table tbody tr")).map((tr) => {
            const cells = Array.from(tr.querySelectorAll("td")).filter((td) => !td.classList.contains("row-actions-cell"));
            return {
                cells: cells.map((td) => {
                    const own = Array.from(td.childNodes)
                        .filter((n) => n.nodeType === 3 || !(n as Element).classList?.contains("tag"))
                        .map((n) => n.textContent ?? "")
                        .join("");
                    return own.trim();
                }),
                tags: Array.from(tr.querySelectorAll(".tag")).map((t) => (t.textContent ?? "").trim()),
                selected: tr.classList.contains("selected"),
            };
        }),
    );
}

/** The table's foot: its count line, and the Load more button. */
export async function foot(): Promise<{ text: string; more: { disabled: boolean; title: string } }> {
    return browser.execute(() => {
        const button = document.querySelector<HTMLButtonElement>(".data-table-foot button");
        return {
            text: (document.querySelector(".data-table-foot span")?.textContent ?? "").trim(),
            more: { disabled: button?.disabled ?? true, title: button?.getAttribute("title") ?? "" },
        };
    });
}

export interface RailNode {
    label: string;
    count: string;
    depth: number;
    title: string;
    inert: boolean;
    selected: boolean;
    icon: boolean;
}

export async function rail(): Promise<RailNode[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll('nav.rail [role="treeitem"]')).map((n) => ({
            label: (n.querySelector(".label")?.textContent ?? "").trim(),
            count: (n.querySelector(".count")?.textContent ?? "").trim(),
            depth: n.querySelectorAll(".guide").length,
            title: n.getAttribute("title") ?? "",
            inert: n.classList.contains("inert"),
            selected: n.classList.contains("selected"),
            icon: n.querySelector(".node-icon svg, .node-icon .codicon") !== null,
        })),
    );
}

/**
 * Clicks a rail node by its label - a list by default, at the rail's top, since an entry's relation can
 * carry a list's name ('Particle Systems' under a model).
 */
export async function railClick(label: string, depth = 0): Promise<void> {
    const node = await browser.$(railNode(label, depth));
    await node.waitForExist({ timeout: 30_000 });
    await node.click();
}

function railNode(label: string, depth?: number): string {
    const at = depth === undefined ? "" : `[count(.//span[contains(@class,'guide')])=${depth}]`;
    return (
        `//nav[contains(@class,'rail')]//*[@role='treeitem']${at}[.//span[contains(@class,'label') and ` +
        `translate(normalize-space(),'${UPPER}','${LOWER}')='${label.toLowerCase()}']]`
    );
}

/** Expands a rail node by its twisty, and waits for what it holds. */
export async function railExpand(label: string, depth?: number): Promise<void> {
    const twisty = await browser.$(`${railNode(label, depth)}//span[contains(@class,'twisty') and @title='Expand']`);
    await twisty.waitForExist({ timeout: 30_000 });
    await twisty.click();
    await browser.waitUntil(
        async () => {
            const nodes = await rail();
            const at = nodes.findIndex((n) => n.label.toLowerCase() === label.toLowerCase());
            return at >= 0 && nodes[at + 1]?.depth > nodes[at].depth;
        },
        { timeout: 30_000, timeoutMsg: `'${label}' holds nothing in the rail` },
    );
}

/** The nodes one level below a rail node: an entry's relations, a relation's rows. */
export async function railChildren(label: string): Promise<RailNode[]> {
    const nodes = await rail();
    const at = nodes.findIndex((n) => n.label.toLowerCase() === label.toLowerCase());
    if (at < 0) return [];
    const children: RailNode[] = [];
    for (const node of nodes.slice(at + 1)) {
        if (node.depth <= nodes[at].depth) break;
        if (node.depth === nodes[at].depth + 1) children.push(node);
    }
    return children;
}

/** Double-clicks a rail node, the way a reader opens one. */
export async function railDoubleClick(label: string, depth?: number): Promise<void> {
    const node = await browser.$(railNode(label, depth));
    await node.waitForExist({ timeout: 30_000 });
    await node.doubleClick();
}

/** Opens the search form if it is folded. */
async function searchForm(): Promise<void> {
    if (!(await (await browser.$("#mb-name")).isExisting())) {
        await (await browser.$("button.search-head")).click();
    }
    await (await browser.$("#mb-name")).waitForExist({ timeout: 10_000 });
}

/** Searches the list by name, with the Behaves like tags pressed that are named. */
export async function search(name: string, kinds: string[] = [], origins: string[] = []): Promise<void> {
    await searchForm();
    await (await browser.$("button=Reset")).click();
    await (await browser.$("#mb-name")).setValue(name);
    for (const kind of kinds) {
        await (await browser.$(`//button[contains(@class,'tag') and starts-with(normalize-space(), '${kind} (')]`)).click();
    }
    for (const origin of origins) {
        await (await browser.$(`(//div[contains(@class,'search-grid')]/span[contains(@class,'tag-row')])[last()]/button[normalize-space()='${origin}']`)).click();
    }
    await (await browser.$("button=Search")).click();
    await browser.pause(1_500);
}

/** The Behaves like tags of the search form. */
export async function kindTags(): Promise<{ label: string; disabled: boolean; title: string; pressed: boolean }[]> {
    await searchForm();
    return browser.execute(() => {
        const row = document.querySelector(".search-grid .tag-row");
        return Array.from(row?.querySelectorAll("button.tag") ?? []).map((b) => ({
            label: (b.textContent ?? "").trim(),
            disabled: (b as HTMLButtonElement).disabled,
            title: b.getAttribute("title") ?? "",
            pressed: b.getAttribute("aria-pressed") === "true",
        }));
    });
}

const UPPER = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
const LOWER = "abcdefghijklmnopqrstuvwxyz";

/**
 * The table row of a name, matched without case: the asset lists spell a name as the catalog keys it,
 * which PRV-16 holds to the file's own spelling - every other test reads past it.
 */
export function rowOf(name: string): string {
    return (
        `//table[contains(@class,'data-table')]//tbody/tr[td[1][translate(normalize-space(text()),'${UPPER}','${LOWER}')=` +
        `'${name.toLowerCase()}']]`
    );
}

export async function selectRow(name: string): Promise<void> {
    const row = await browser.$(rowOf(name));
    await row.waitForExist({ timeout: 30_000 });
    await row.click();
}

export async function openRow(name: string): Promise<void> {
    const row = await browser.$(rowOf(name));
    await row.waitForExist({ timeout: 30_000 });
    await row.doubleClick();
}

export interface Tab {
    name: string;
    on: boolean;
    unpinned: boolean;
    title: string;
}

export async function tabs(): Promise<Tab[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".editor-tabs .editor-tab")).map((t) => ({
            name: (t.querySelector(".editor-tab-name")?.textContent ?? "").trim(),
            on: t.classList.contains("on"),
            unpinned: t.classList.contains("unpinned"),
            title: t.querySelector(".editor-tab-name")?.getAttribute("title") ?? "",
        })),
    );
}

export interface Action {
    label: string;
    title: string;
    disabled: boolean;
}

/** The open entry's action column. */
export async function actions(): Promise<Action[]> {
    await (await browser.$("nav.preview-actions")).waitForExist({ timeout: 60_000 });
    return browser.execute(() =>
        Array.from(document.querySelectorAll("nav.preview-actions .dock-tile")).map((b) => ({
            label: (b.querySelector(".tile-label")?.textContent ?? "").trim(),
            title: b.getAttribute("title") ?? "",
            disabled: (b as HTMLButtonElement).disabled,
        })),
    );
}

/** A table row's own action buttons, by their titles ('<action> - <why not>' when disabled). */
export async function rowActions(name: string): Promise<{ title: string; disabled: boolean }[]> {
    const row = await browser.$(rowOf(name));
    await row.waitForExist({ timeout: 30_000 });
    return browser.execute(
        (tr: HTMLElement) =>
            Array.from(tr.querySelectorAll(".row-actions button")).map((b) => ({
                title: b.getAttribute("title") ?? "",
                disabled: (b as HTMLButtonElement).disabled,
            })),
        row as unknown as HTMLElement,
    );
}

/** The open entry's panels by their names. */
export async function panels(): Promise<string[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll("section.preview section.panel")).map(
            (p) =>
                p.getAttribute("aria-label") ??
                (p.querySelector(".entry-title") ? "Header" : (p.querySelector(".dock-section-name")?.textContent ?? "").trim()),
        ),
    );
}

/** The open entry's header: its tags and its key-value rows. */
export async function entryHead(): Promise<{ title: string; tags: string[]; rows: Record<string, string> }> {
    return browser.execute(() => {
        const head = document.querySelector("section.entry-head");
        const rows: Record<string, string> = {};
        for (const dt of Array.from(head?.querySelectorAll("dt") ?? [])) {
            const own = Array.from(dt.childNodes)
                .filter((n) => n.nodeType === 3)
                .map((n) => n.textContent ?? "")
                .join("")
                .trim();
            const value = dt.querySelector("button, a") ?? dt.nextElementSibling;
            rows[own] = (value?.textContent ?? "").trim();
        }
        return {
            title: (head?.querySelector(".entry-title")?.textContent ?? "").trim(),
            tags: Array.from(head?.querySelectorAll(".tag") ?? []).map((t) => (t.textContent ?? "").trim()),
            rows,
        };
    });
}

/** An item of a context menu. */
export interface MenuItem {
    label: string;
    disabled: boolean;
    /** The key that does the same, or why a disabled item is. */
    side: string;
}

/** The context menu of a table row: each item's label, and whether it is disabled. */
export async function contextMenu(name: string): Promise<MenuItem[]> {
    return menuOf(rowOf(name));
}

/** The context menu of a rail node. */
export async function railMenu(label: string, depth?: number): Promise<MenuItem[]> {
    return menuOf(railNode(label, depth));
}

/** Picks an item of a rail node's context menu. */
export async function railMenuPick(label: string, item: string, depth?: number): Promise<void> {
    await menuOf(railNode(label, depth), false);
    await (await browser.$(`//*[@role='menuitem'][normalize-space(text())='${item}']`)).click();
}

async function menuOf(selector: string, close = true): Promise<MenuItem[]> {
    const target = await browser.$(selector);
    await target.waitForExist({ timeout: 30_000 });
    await target.click({ button: "right" });
    await (await browser.$('[role="menuitem"]')).waitForExist({ timeout: 5_000 });
    const items: MenuItem[] = await browser.execute(() =>
        Array.from(document.querySelectorAll('[role="menuitem"]')).map((m) => ({
            label: Array.from(m.childNodes)
                .filter((n) => n.nodeType === 3)
                .map((n) => n.textContent ?? "")
                .join("")
                .trim(),
            disabled: (m as HTMLButtonElement).disabled,
            side: (m.querySelector(".keys")?.textContent ?? "").trim(),
        })),
    );
    if (close) await browser.keys("Escape");
    return items;
}
