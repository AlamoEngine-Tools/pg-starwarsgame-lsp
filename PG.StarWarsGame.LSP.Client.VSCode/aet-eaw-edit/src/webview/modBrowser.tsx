// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Mod Browser (#270): everything a project indexes, by what it is. Modelled on Eclipse Scout's
// outline - a rail on the left that is the recursive tree of the lists and of the rows a list has
// loaded, and the open list or entry on the right, with its search form at the bottom. A row's
// relations are asked for when it is opened, never for a whole list.
//
// Every request goes through the host, which sends it to this panel's own project's server.

import "./shared/l10nSetup";
import * as l10n from "@vscode/l10n";
import { useCallback, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import styled from "styled-components";

import {
    GET_SYMBOL_NODE,
    GET_SYMBOL_OUTLINE,
    GET_SYMBOL_PAGE,
    GetSymbolNodeResult,
    GetSymbolOutlineResult,
    GetSymbolPageParams,
    GetSymbolPageResult,
    SymbolOutline,
    SymbolParent,
    SymbolRelation,
    SymbolRow,
} from "../protocol/modBrowser";
import {
    kindChoices,
    originChoices,
    originLabel,
    pageLabel,
    reasonLabel,
    relationLabel,
} from "./modBrowserModel";
import { Button } from "./shared/Button";
import { Icon } from "./shared/Icon";
import { tokensCss } from "./shared/tokens";

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

const vscode = acquireVsCodeApi();

const PAGE_SIZE = 200;

// ── the request relay ──────────────────────────────────────────────────────

let nextRequest = 1;
const waiting = new Map<number, (result: unknown) => void>();

window.addEventListener("message", (event: MessageEvent) => {
    const msg = event.data as { type?: string; id?: number; result?: unknown };
    if (msg.type === "response" && msg.id !== undefined) {
        waiting.get(msg.id)?.(msg.result ?? null);
        waiting.delete(msg.id);
    }
});

/** One request to this panel's project's server; null when the server answered nothing. */
function request<T>(method: string, params: unknown): Promise<T | null> {
    const id = nextRequest++;
    return new Promise((resolve) => {
        waiting.set(id, (result) => resolve(result as T | null));
        vscode.postMessage({ type: "request", id, method, params });
    });
}

// ── state ──────────────────────────────────────────────────────────────────

/** A list on the right: one of the outline's, or one relation of an entry. */
interface ListView {
    page: string;
    parent?: SymbolParent;
    /** The entry's name, when the list is one of its relations. */
    parentName?: string;
}

/** An entry open on the right. */
interface EntryView {
    page: string;
    row: SymbolRow;
    relations: SymbolRelation[];
}

interface Filter {
    query: string;
    kinds: string[];
    origins: string[];
}

const NO_FILTER: Filter = { query: "", kinds: [], origins: [] };

function rowKey(page: string, id: string): string {
    return `${page}|${id}`;
}

function App(): React.JSX.Element {
    const [outline, setOutline] = useState<SymbolOutline | null>(null);
    const [summary, setSummary] = useState<GetSymbolOutlineResult | null>(null);
    const [list, setList] = useState<ListView | null>(null);
    const [entry, setEntry] = useState<EntryView | null>(null);
    const [rows, setRows] = useState<SymbolRow[]>([]);
    const [total, setTotal] = useState(0);
    const [loading, setLoading] = useState(false);
    const [draft, setDraft] = useState<Filter>(NO_FILTER);
    const [filter, setFilter] = useState<Filter>(NO_FILTER);
    const [formOpen, setFormOpen] = useState(true);
    const [opened, setOpened] = useState<Record<string, SymbolRelation[]>>({});
    const [refreshed, setRefreshed] = useState(0);

    useEffect(() => {
        const handle = (event: MessageEvent) => {
            const msg = event.data as {
                type?: string;
                outline?: SymbolOutline;
            };
            if (msg.type === "outline" && msg.outline) {
                setOutline(msg.outline);
                setList(null);
                setEntry(null);
                setOpened({});
            }
            if (msg.type === "refresh") {
                setRefreshed((n) => n + 1);
            }
        };
        window.addEventListener("message", handle);
        vscode.postMessage({ type: "ready" });
        return () => window.removeEventListener("message", handle);
    }, []);

    useEffect(() => {
        if (outline === null) {
            return;
        }
        void request<GetSymbolOutlineResult>(GET_SYMBOL_OUTLINE, {
            outline,
        }).then((result) => {
            setSummary(result);
            setList(
                (current) =>
                    current ??
                    (result?.pages[0] ? { page: result.pages[0].page } : null),
            );
        });
    }, [outline, refreshed]);

    const load = useCallback(
        async (view: ListView, applied: Filter, offset: number) => {
            setLoading(true);
            const params: GetSymbolPageParams = {
                page: view.page,
                parent: view.parent,
                query: applied.query || undefined,
                kinds: applied.kinds.length ? applied.kinds : undefined,
                origins: applied.origins.length ? applied.origins : undefined,
                offset,
                limit: PAGE_SIZE,
            };
            const result = await request<GetSymbolPageResult>(
                GET_SYMBOL_PAGE,
                params,
            );
            setRows((current) =>
                offset === 0
                    ? (result?.rows ?? [])
                    : [...current, ...(result?.rows ?? [])],
            );
            setTotal(result?.total ?? 0);
            setLoading(false);
        },
        [],
    );

    useEffect(() => {
        if (list !== null) {
            void load(list, filter, 0);
        }
    }, [list, filter, refreshed, load]);

    const openList = (view: ListView) => {
        setEntry(null);
        setDraft(NO_FILTER);
        setFilter(NO_FILTER);
        setList(view);
    };

    const openEntry = async (page: string, row: SymbolRow) => {
        const node = await request<GetSymbolNodeResult>(GET_SYMBOL_NODE, {
            page,
            id: row.id,
        });
        const relations = node?.relations ?? [];
        setOpened((current) => ({
            ...current,
            [rowKey(page, row.id)]: relations,
        }));
        setEntry({ page, row: node?.entry ?? row, relations });
    };

    const toggleRow = async (page: string, row: SymbolRow) => {
        const key = rowKey(page, row.id);
        if (opened[key]) {
            setOpened(({ [key]: _, ...rest }) => rest);
            return;
        }
        const node = await request<GetSymbolNodeResult>(GET_SYMBOL_NODE, {
            page,
            id: row.id,
        });
        setOpened((current) => ({ ...current, [key]: node?.relations ?? [] }));
    };

    const openRelation = (
        page: string,
        row: SymbolRow,
        relation: SymbolRelation,
    ) => {
        openList({
            page,
            parent: { page, id: row.id, relation: relation.relation },
            parentName: row.name,
        });
    };

    const layers = summary?.layers ?? [];
    const listPage = list?.parent ? list.parent.page : list?.page;

    if (outline === null || summary === null) {
        return (
            <Shell>
                <p className="empty">{l10n.t("Loading...")}</p>
            </Shell>
        );
    }

    return (
        <Shell>
            <nav className="rail" aria-label={l10n.t("Lists")}>
                {summary.pages.map((p) => {
                    const current = list?.page === p.page && !list.parent;
                    return (
                        <div key={p.page}>
                            <button
                                type="button"
                                className={`node depth0${current && entry === null ? " selected" : ""}`}
                                disabled={p.count === 0}
                                title={
                                    p.count === 0
                                        ? l10n.t("Nothing of this kind")
                                        : undefined
                                }
                                onClick={() => openList({ page: p.page })}
                            >
                                <span className="twisty">
                                    <Icon
                                        name={
                                            current ? "expanded" : "collapsed"
                                        }
                                        size={12}
                                    />
                                </span>
                                <span className="label">
                                    {pageLabel(p.page)}
                                </span>
                                <span className="count">
                                    {p.count.toLocaleString()}
                                </span>
                            </button>
                            {current &&
                                rows.map((row) => (
                                    <RailRow
                                        key={row.id}
                                        page={p.page}
                                        row={row}
                                        relations={
                                            opened[rowKey(p.page, row.id)]
                                        }
                                        selected={entry?.row.id === row.id}
                                        onToggle={() =>
                                            void toggleRow(p.page, row)
                                        }
                                        onOpen={() =>
                                            void openEntry(p.page, row)
                                        }
                                        onRelation={(r) =>
                                            openRelation(p.page, row, r)
                                        }
                                    />
                                ))}
                        </div>
                    );
                })}
            </nav>
            <main className="page">
                <header className="crumbs">
                    {list && (
                        <button
                            type="button"
                            className="crumb"
                            onClick={() =>
                                openList({ page: listPage ?? list.page })
                            }
                        >
                            {pageLabel(listPage ?? list.page)}
                        </button>
                    )}
                    {list?.parentName && entry === null && (
                        <>
                            <Separator />
                            <span className="crumb">{list.parentName}</span>
                            <Separator />
                            <b>{relationLabel(list.parent!.relation)}</b>
                        </>
                    )}
                    {entry && (
                        <>
                            <Separator />
                            <b>{entry.row.name}</b>
                        </>
                    )}
                    {entry === null && (
                        <span className="end">
                            {l10n.t(
                                "{0} of {1}",
                                rows.length.toLocaleString(),
                                total.toLocaleString(),
                            )}
                        </span>
                    )}
                </header>
                {entry ? (
                    <EntryPage
                        entry={entry}
                        layers={layers}
                        onRelation={(r) =>
                            openRelation(entry.page, entry.row, r)
                        }
                    />
                ) : (
                    <>
                        <div className="grid">
                            <table>
                                <thead>
                                    <tr>
                                        <th>{l10n.t("Name")}</th>
                                        <th>{l10n.t("Type")}</th>
                                        <th>{l10n.t("Kinds")}</th>
                                        <th>{l10n.t("Origin")}</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {rows.map((row) => (
                                        <tr
                                            key={row.id}
                                            className={
                                                row.page ? undefined : "inert"
                                            }
                                            title={
                                                row.page
                                                    ? undefined
                                                    : inertReason(row)
                                            }
                                            onClick={() =>
                                                row.page &&
                                                void openEntry(row.page, row)
                                            }
                                        >
                                            <td>{row.name}</td>
                                            <td className="dim">
                                                {row.type ?? ""}
                                            </td>
                                            <td className="dim">
                                                {row.kinds.join(", ")}
                                            </td>
                                            <td>
                                                {originLabel(
                                                    row.origin,
                                                    row.originName,
                                                    layers,
                                                )}
                                                {row.overrides.length > 0 && (
                                                    <span className="chip">
                                                        {l10n.t(
                                                            "Overrides - {0}",
                                                            originLabel(
                                                                row
                                                                    .overrides[0],
                                                                null,
                                                                layers,
                                                            ),
                                                        )}
                                                    </span>
                                                )}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                            {rows.length === 0 && !loading && (
                                <p className="empty">
                                    {l10n.t("Nothing matches")}
                                </p>
                            )}
                        </div>
                        <footer className="pager">
                            <span>
                                {l10n.t(
                                    "Showing {0} of {1}",
                                    rows.length.toLocaleString(),
                                    total.toLocaleString(),
                                )}
                            </span>
                            <Button
                                compact
                                disabled={rows.length >= total || loading}
                                disabledReason={
                                    loading
                                        ? l10n.t("Loading...")
                                        : l10n.t("Everything is loaded")
                                }
                                onClick={() =>
                                    list && void load(list, filter, rows.length)
                                }
                            >
                                {l10n.t("Load more")}
                            </Button>
                        </footer>
                        <SearchForm
                            open={formOpen}
                            onToggle={() => setFormOpen((o) => !o)}
                            title={
                                list?.parentName
                                    ? l10n.t(
                                          "{0} of {1}",
                                          relationLabel(list.parent!.relation),
                                          list.parentName,
                                      )
                                    : pageLabel(list?.page ?? "")
                            }
                            draft={draft}
                            kinds={kindChoices(rows)}
                            origins={originChoices(layers)}
                            layers={layers}
                            onChange={setDraft}
                            onSearch={() => setFilter(draft)}
                            onReset={() => {
                                setDraft(NO_FILTER);
                                setFilter(NO_FILTER);
                            }}
                        />
                    </>
                )}
            </main>
        </Shell>
    );
}

function Separator(): React.JSX.Element {
    return (
        <span className="sep" aria-hidden="true">
            <Icon name="collapsed" size={12} />
        </span>
    );
}

/** Why a row opens nothing: a name nothing defines, or a place rather than an entry. */
function inertReason(row: SymbolRow): string {
    return row.origin === "missing"
        ? l10n.t("Nothing defines this name")
        : l10n.t("Not an entry of its own");
}

function RailRow({
    row,
    relations,
    selected,
    onToggle,
    onOpen,
    onRelation,
}: {
    page: string;
    row: SymbolRow;
    relations: SymbolRelation[] | undefined;
    selected: boolean;
    onToggle: () => void;
    onOpen: () => void;
    onRelation: (relation: SymbolRelation) => void;
}): React.JSX.Element {
    return (
        <>
            <div className={`node depth1${selected ? " selected" : ""}`}>
                <button
                    type="button"
                    className="twisty"
                    title={l10n.t("Show relations")}
                    onClick={onToggle}
                >
                    <Icon
                        name={relations ? "expanded" : "collapsed"}
                        size={12}
                    />
                </button>
                <button type="button" className="label" onClick={onOpen}>
                    {row.name}
                </button>
            </div>
            {relations?.map((r) => (
                <button
                    key={r.relation}
                    type="button"
                    className="node depth2"
                    disabled={r.count === 0}
                    title={r.count === 0 ? reasonLabel(r.reason) : undefined}
                    onClick={() => onRelation(r)}
                >
                    <span className="twisty" />
                    <span className="label">
                        {relationLabel(r.relation)}
                        {r.count === 0 && (
                            <em className="why"> - {reasonLabel(r.reason)}</em>
                        )}
                    </span>
                    <span className="count">{r.count ?? ""}</span>
                </button>
            ))}
        </>
    );
}

function EntryPage({
    entry,
    layers,
    onRelation,
}: {
    entry: EntryView;
    layers: GetSymbolOutlineResult["layers"];
    onRelation: (relation: SymbolRelation) => void;
}): React.JSX.Element {
    const { row } = entry;
    return (
        <section className="entry">
            <h1>{row.name}</h1>
            <p className="line">
                {row.type && <span className="chip">{row.type}</span>}
                {row.kinds.map((k) => (
                    <span key={k} className="chip">
                        {k}
                    </span>
                ))}
            </p>
            <p className="dim">
                {l10n.t(
                    "Defined in - {0}",
                    originLabel(row.origin, row.originName, layers),
                )}
            </p>
            {row.overrides.map((o) => (
                <p key={o} className="dim">
                    {l10n.t("Overrides - {0}", originLabel(o, null, layers))}
                </p>
            ))}
            <ul className="relations">
                {entry.relations.map((r) => (
                    <li key={r.relation}>
                        <Button
                            compact
                            disabled={r.count === 0}
                            disabledReason={reasonLabel(r.reason)}
                            onClick={() => onRelation(r)}
                        >
                            {relationLabel(r.relation)}
                            {r.count !== null &&
                                r.count !== undefined &&
                                ` (${r.count})`}
                        </Button>
                    </li>
                ))}
            </ul>
        </section>
    );
}

function SearchForm({
    open,
    onToggle,
    title,
    draft,
    kinds,
    origins,
    layers,
    onChange,
    onSearch,
    onReset,
}: {
    open: boolean;
    onToggle: () => void;
    title: string;
    draft: Filter;
    kinds: string[];
    origins: string[];
    layers: GetSymbolOutlineResult["layers"];
    onChange: (filter: Filter) => void;
    onSearch: () => void;
    onReset: () => void;
}): React.JSX.Element {
    const toggle = (list: string[], value: string) =>
        list.includes(value)
            ? list.filter((v) => v !== value)
            : [...list, value];
    return (
        <form
            className="search"
            onSubmit={(event) => {
                event.preventDefault();
                onSearch();
            }}
        >
            <button type="button" className="search-head" onClick={onToggle}>
                <b>{l10n.t("Search")}</b>
                <span className="dim">{title}</span>
                <span className="end">
                    <Icon name={open ? "expanded" : "collapsed"} size={12} />
                </span>
            </button>
            {open && (
                <>
                    <div className="search-grid">
                        <label htmlFor="mb-name">{l10n.t("Name")}</label>
                        <input
                            id="mb-name"
                            value={draft.query}
                            onChange={(e) =>
                                onChange({ ...draft, query: e.target.value })
                            }
                        />
                        {kinds.length > 0 && (
                            <>
                                <span>{l10n.t("Kind")}</span>
                                <span className="chips">
                                    {kinds.map((k) => (
                                        <button
                                            key={k}
                                            type="button"
                                            className={`chip${draft.kinds.includes(k) ? " on" : ""}`}
                                            onClick={() =>
                                                onChange({
                                                    ...draft,
                                                    kinds: toggle(
                                                        draft.kinds,
                                                        k,
                                                    ),
                                                })
                                            }
                                        >
                                            {k}
                                        </button>
                                    ))}
                                </span>
                            </>
                        )}
                        <span>{l10n.t("Origin")}</span>
                        <span className="chips">
                            {origins.map((o) => (
                                <button
                                    key={o}
                                    type="button"
                                    className={`chip${draft.origins.includes(o) ? " on" : ""}`}
                                    onClick={() =>
                                        onChange({
                                            ...draft,
                                            origins: toggle(draft.origins, o),
                                        })
                                    }
                                >
                                    {originLabel(o, null, layers)}
                                </button>
                            ))}
                        </span>
                    </div>
                    <div className="search-foot">
                        <Button compact onClick={onReset}>
                            {l10n.t("Reset")}
                        </Button>
                        <Button compact submit className="primary">
                            {l10n.t("Search")}
                        </Button>
                    </div>
                </>
            )}
        </form>
    );
}

// VS Code's own colours, so the panel follows the theme. The rail and the page are a fixed grid;
// the table and the rail scroll in place.
const Shell = styled.div`
    display: grid;
    grid-template-columns: minmax(200px, 280px) minmax(0, 1fr);
    height: 100vh;
    color: var(--vscode-foreground);
    background: var(--vscode-editor-background);
    font: var(--vscode-font-size) / 1.4 var(--vscode-font-family);
    ${tokensCss}

    button {
        font: inherit;
        color: inherit;
    }
    .empty {
        padding: var(--space-12) var(--space-16);
        color: var(--vscode-descriptionForeground);
    }
    .dim {
        color: var(--vscode-descriptionForeground);
    }
    .chip {
        display: inline-flex;
        padding: var(--space-1) var(--space-6);
        margin-left: var(--space-6);
        border-radius: var(--radius-pill);
        border: 0;
        background: var(--vscode-badge-background);
        color: var(--vscode-badge-foreground);
        font-size: var(--font-size-11);
    }
    .chip.on {
        background: var(--vscode-button-background);
        color: var(--vscode-button-foreground);
    }

    .rail {
        overflow: auto;
        border-right: 1px solid var(--vscode-panel-border);
        padding: var(--space-6) 0;
    }
    .node {
        display: grid;
        grid-template-columns: var(--icon-size-16) minmax(0, 1fr) auto;
        align-items: center;
        gap: var(--space-4);
        width: 100%;
        height: 22px;
        padding: 0 var(--space-8) 0 0;
        border: 0;
        background: none;
        text-align: left;
        cursor: pointer;
        content-visibility: auto;
        contain-intrinsic-size: 22px;
    }
    .node:hover {
        background: var(--vscode-list-hoverBackground);
    }
    .node.selected {
        background: var(--vscode-list-activeSelectionBackground);
        color: var(--vscode-list-activeSelectionForeground);
    }
    .node:disabled {
        color: var(--vscode-disabledForeground);
        cursor: default;
    }
    .node.depth0 {
        padding-left: var(--space-8);
    }
    .node.depth1 {
        padding-left: calc(var(--space-8) + var(--space-16));
    }
    .node.depth2 {
        padding-left: calc(var(--space-8) + 2 * var(--space-16));
    }
    .node .twisty {
        display: grid;
        place-items: center;
        width: var(--icon-size-16);
        padding: 0;
        border: 0;
        background: none;
        cursor: pointer;
    }
    .node .label {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        padding: 0;
        border: 0;
        background: none;
        text-align: left;
        cursor: pointer;
    }
    .node .count {
        color: var(--vscode-descriptionForeground);
        font-size: var(--font-size-11);
        font-variant-numeric: tabular-nums;
    }
    .node .why {
        color: var(--vscode-disabledForeground);
        font-style: normal;
    }

    .page {
        display: grid;
        grid-template-rows: auto minmax(0, 1fr) auto auto;
        min-width: 0;
        min-height: 0;
    }
    .crumbs {
        display: flex;
        align-items: center;
        gap: var(--space-6);
        padding: var(--space-8) var(--space-16);
        border-bottom: 1px solid var(--vscode-panel-border);
    }
    .crumbs .crumb {
        border: 0;
        background: none;
        padding: 0;
        cursor: pointer;
        color: var(--vscode-textLink-foreground);
    }
    .crumbs .sep {
        color: var(--vscode-descriptionForeground);
    }
    .crumbs .end {
        margin-left: auto;
        color: var(--vscode-descriptionForeground);
    }

    .grid {
        overflow: auto;
    }
    table {
        width: 100%;
        border-collapse: collapse;
    }
    th {
        position: sticky;
        top: 0;
        text-align: left;
        padding: var(--space-6) var(--space-16);
        font-weight: 600;
        font-size: var(--font-size-11);
        background: var(--vscode-editor-background);
        border-bottom: 1px solid var(--vscode-panel-border);
        color: var(--vscode-descriptionForeground);
    }
    td {
        padding: var(--space-4) var(--space-16);
        border-bottom: 1px solid var(--vscode-panel-border);
        white-space: nowrap;
    }
    tbody tr {
        cursor: pointer;
    }
    tbody tr:hover td {
        background: var(--vscode-list-hoverBackground);
    }
    tbody tr.inert {
        cursor: default;
        color: var(--vscode-disabledForeground);
    }

    .pager {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: var(--space-6) var(--space-16);
        border-top: 1px solid var(--vscode-panel-border);
        color: var(--vscode-descriptionForeground);
    }

    .search {
        border-top: 1px solid var(--vscode-panel-border);
    }
    .search-head {
        display: flex;
        gap: var(--space-8);
        align-items: center;
        width: 100%;
        padding: var(--space-6) var(--space-16);
        border: 0;
        background: var(--vscode-sideBar-background);
        text-align: left;
        cursor: pointer;
    }
    .search-head .end {
        margin-left: auto;
    }
    .search-grid {
        display: grid;
        grid-template-columns: 64px minmax(0, 1fr);
        gap: var(--space-6) var(--space-8);
        padding: var(--space-8) var(--space-16);
        align-items: center;
    }
    .search-grid input {
        background: var(--vscode-input-background);
        color: var(--vscode-input-foreground);
        border: 1px solid var(--vscode-input-border, transparent);
        padding: var(--space-2) var(--space-6);
    }
    .search-grid .chips {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-4);
    }
    .search-grid .chip {
        margin-left: 0;
        cursor: pointer;
    }
    .search-foot {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-8);
        padding: 0 var(--space-16) var(--space-8);
    }

    .entry {
        padding: var(--space-16) var(--space-24);
        overflow: auto;
    }
    .entry h1 {
        margin: 0 0 var(--space-6);
        font-size: var(--font-size-16);
    }
    .entry .line .chip:first-child {
        margin-left: 0;
    }
    .entry .relations {
        list-style: none;
        padding: 0;
        margin: var(--space-12) 0 0;
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-6);
    }

    @media (max-width: 700px) {
        grid-template-columns: minmax(0, 1fr);
        grid-template-rows: 40vh minmax(0, 1fr);
        .rail {
            border-right: 0;
            border-bottom: 1px solid var(--vscode-panel-border);
        }
    }
`;

createRoot(document.getElementById("root")!).render(<App />);
