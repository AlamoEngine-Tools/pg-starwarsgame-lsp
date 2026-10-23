// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Mod Browser (#270): everything a project indexes, by what it is. Modelled on Eclipse Scout's
// outline - a rail on the left that is the recursive tree of lists and entries, and the open list or
// entry on the right. A list's children are its loaded rows, an entry's are its relations, and a
// relation is a list again; the path to the open node is the breadcrumb. A list carries its search
// form at the bottom; an entry shows its header and, for a game object, its encyclopedia card.
//
// A click selects a row, in the table and the rail alike; a double click or Enter opens the entry
// in a tab beside the table's own, which keeps the table as it was left. One preview tab is reused
// until it is pinned, as VS Code's italic preview tab. F12 goes to the definition, and the twisty
// only expands. What else an entry offers - the code lens set - is on its context menu, which lists
// only what can be done, on the row's hover icons and on its page.
//
// Every request goes through the host, which sends it to this panel's own project's server.

import "./shared/l10nSetup";
import * as l10n from "@vscode/l10n";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import styled from "styled-components";

import { GET_ENCYCLOPEDIA_ENTRY } from "../protocol/encyclopedia";
import type { GetEncyclopediaEntryResult } from "../protocol/encyclopedia";
import { GET_MODEL_DETAIL } from "../protocol/modelPreview";
import type { GetModelDetailResult } from "../protocol/modelPreview";
import {
    GET_SYMBOL_NODE,
    GET_SYMBOL_OUTLINE,
    GET_SYMBOL_PAGE,
    GET_SYMBOL_PREVIEW,
    GetSymbolNodeResult,
    GetSymbolOutlineResult,
    GetSymbolPageParams,
    GetSymbolPageResult,
    GetSymbolPreviewResult,
    SymbolKindCount,
    SymbolOutline,
    SymbolParent,
    SymbolRelation,
    SymbolRow,
} from "../protocol/modBrowser";
import { pickShipName } from "../shipNamePick";
import { EncyclopediaCard } from "./encyclopediaCard";
import { factionModes, factionSlotOf } from "./encyclopediaModes";
import { ColumnFilter, DataColumn, DataTable } from "./shared/DataTable";
import { menuAction, RowMenuFrame } from "./shared/RowMenu";
import { TabStrip } from "./shared/TabStrip";
import {
    ActionChoice,
    actionChoices,
    KindChoice,
    kindChoices,
    listColumns,
    menuChoices,
    originChoices,
    originLabel,
    pageIcon,
    pageLabel,
    reasonLabel,
    relationIcon,
    relationLabel,
    rowInertReason,
    rowTypeLabel,
} from "./modBrowserModel";
import { ACTION_ICONS, PreviewPage } from "./modBrowserPreview";
import {
    closeTab,
    NO_TABS,
    openPreview,
    pinTab,
    showTable,
    Tabs,
} from "./shared/editorTabs";
import { Button } from "./shared/Button";
import { IconButton } from "./shared/Button";
import {
    dockChromeCss,
    dockHeaderCss,
    problemsPanelCss,
} from "./shared/dockChrome";
import { Field } from "./shared/Field";
import { Icon, IconName } from "./shared/Icon";
import { ModeSelector } from "./shared/ModeSelector";
import { readPanelSize, writePanelSize } from "./shared/panelLayout";
import { initPanelLayout } from "./shared/panelLayoutBridge";
import { treeStep } from "./shared/rovingFocus";
import {
    moveColumn,
    readColumnLayout,
    writeColumnOrder,
    writeColumnWidth,
} from "./shared/tableColumns";
import type { ColumnLayout } from "./shared/tableColumns";
import type { SortState } from "./shared/tableSort";
import { useEdgeResize } from "./useEdgeResize";

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

const vscode = acquireVsCodeApi();
initPanelLayout(vscode);

/** The rail's and the search form's remembered sizes, as every panel keeps its own. */
const RAIL_WIDTH_KEY = "modBrowser.rail";
const SEARCH_HEIGHT_KEY = "modBrowser.search";

const PAGE_SIZE = 200;
/** The card at twice the game's size: legible, and still small enough to sit beside the header. */
const CARD_ZOOM = 2;
/** How long the pointer rests on a row before its actions are asked for. */
const HOVER_DELAY_MS = 250;

// ── the request relay ──────────────────────────────────────────────────────

let nextRequest = 1;
const waiting = new Map<number, (result: unknown) => void>();

window.addEventListener("message", (event: MessageEvent) => {
    const msg = event.data as { type?: string; id?: unknown; result?: unknown };
    if (msg.type !== "response" || typeof msg.id !== "number") {
        return;
    }
    // Only an answer to a request this page made has a callback to call.
    const answer = waiting.get(msg.id);
    if (typeof answer === "function") {
        waiting.delete(msg.id);
        answer(msg.result ?? null);
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

/** A model's detail for its preview's tree: one function, so the tile asks once per model. */
function loadModelDetail(model: string): Promise<GetModelDetailResult | null> {
    return request<GetModelDetailResult>(GET_MODEL_DETAIL, {
        modelReference: model,
    });
}

// ── the tree ───────────────────────────────────────────────────────────────

/** A list: one of the outline's, or one relation of an entry. */
interface ListNode {
    kind: "list";
    /** The node's path in the rail, unique even when one entry is reached by two paths. */
    key: string;
    page: string;
    parent?: SymbolParent;
    label: string;
    count?: number | null;
    reason?: string | null;
    trail: RailNode[];
}

/** An entry of a list. */
interface EntryNode {
    kind: "entry";
    key: string;
    page: string;
    row: SymbolRow;
    trail: RailNode[];
}

type RailNode = ListNode | EntryNode;

/** One list's loaded rows, as the rail shows them. */
interface Loaded {
    rows: SymbolRow[];
    total: number;
}

function entryOf(list: ListNode, row: SymbolRow): EntryNode {
    return {
        kind: "entry",
        key: `${list.key}\n${row.id}`,
        page: row.page ?? list.page,
        row,
        trail: [...list.trail, list],
    };
}

function relationOf(entry: EntryNode, relation: SymbolRelation): ListNode {
    return {
        kind: "list",
        key: `${entry.key}\n${relation.relation}`,
        page: entry.page,
        parent: {
            page: entry.page,
            id: entry.row.id,
            relation: relation.relation,
        },
        label: relationLabel(relation.relation),
        count: relation.count,
        reason: relation.reason,
        trail: [...entry.trail, entry],
    };
}

function nodeName(node: RailNode): string {
    return node.kind === "entry" ? node.row.name : node.label;
}

/**
 * Where a list's column arrangement is kept: per list - an outline's list by its page, a relation by
 * its page and relation - so every Models relation shares one arrangement.
 */
function columnsOf(list: ListNode): string {
    return list.parent
        ? `modBrowser.columns.${list.page}.${list.parent.relation}`
        : `modBrowser.columns.${list.page}`;
}

/** A name filter: what to match, applied on Enter or its button, and cleared by its other. */
function NameFilter({
    value,
    onApply,
}: {
    value: string;
    onApply: (query: string) => void;
}): React.JSX.Element {
    const [typed, setTyped] = useState(value);
    return (
        <form
            className="name-filter"
            onSubmit={(event) => {
                event.preventDefault();
                onApply(typed.trim());
            }}
        >
            <input
                autoFocus
                aria-label={l10n.t("Name")}
                value={typed}
                onChange={(e) => setTyped(e.target.value)}
            />
            <Button
                compact
                disabled={value === ""}
                disabledReason={l10n.t("No name filter")}
                onClick={() => {
                    setTyped("");
                    onApply("");
                }}
            >
                {l10n.t("Clear")}
            </Button>
            <Button compact submit className="primary">
                {l10n.t("Filter")}
            </Button>
        </form>
    );
}

/** A node's picture: an entry and an outline list by their type, a relation by what it holds. */
function nodeIcon(node: RailNode): IconName {
    return node.kind === "list" && node.parent
        ? relationIcon(node.parent.relation)
        : pageIcon(node.page);
}

/**
 * One visible row of the rail, top to bottom - what the tree's keys walk. A list with more rows
 * than it has loaded ends in a row that opens it in the table.
 */
interface RailRow {
    key: string;
    node: RailNode;
    depth: number;
    open: boolean;
    /** It has nothing to open: a list of none, or an entry the index cannot open. */
    inert: boolean;
    /** The rows of the list not loaded, on its last row. */
    more?: number;
}

/** An entry's tab: one per entry, however many paths in the rail lead to it. */
function tabKey(node: EntryNode): string {
    return `${node.page}\n${node.row.id}`;
}

/** An open context menu: where, and on which entry. */
interface Menu {
    x: number;
    y: number;
    node: EntryNode;
    /** The table row it was opened on, followed so the menu closes when it scrolls away; -1 in the rail. */
    anchor: number;
}

// ── state ──────────────────────────────────────────────────────────────────

interface Filter {
    query: string;
    kinds: string[];
    origins: string[];
}

const NO_FILTER: Filter = { query: "", kinds: [], origins: [] };

/**
 * The ship name drawn for each object, kept for the panel's life - the encyclopedia panel's rule:
 * the engine picks at random, and re-picking on every open would reshuffle the card.
 */
const shipNames = new Map<string, string | null>();

function App(): React.JSX.Element {
    const [outline, setOutline] = useState<SymbolOutline | null>(null);
    const [summary, setSummary] = useState<GetSymbolOutlineResult | null>(null);
    /** What is selected - in the rail and, when it is one of its rows, in the table. */
    const [current, setCurrent] = useState<RailNode | null>(null);
    /** The list the table's tab shows. */
    const [tableList, setTableList] = useState<ListNode | null>(null);
    const [tabs, setTabs] = useState<Tabs<EntryNode>>(NO_TABS);
    const [expanded, setExpanded] = useState<Set<string>>(new Set());
    /**
     * Every list's loaded rows, the table's among them. The rail draws a list's children from here,
     * so a list keeps its rows - searched and paged as the table left them - after the table moves
     * on to one of its entries or their relations.
     */
    const [loaded, setLoaded] = useState<Record<string, Loaded>>({});
    const [nodes, setNodes] = useState<Record<string, GetSymbolNodeResult>>({});
    /** Each opened entry's preview, by its tab. */
    const [previews, setPreviews] = useState<
        Record<string, GetSymbolPreviewResult>
    >({});
    const [kinds, setKinds] = useState<SymbolKindCount[]>([]);
    const [loading, setLoading] = useState(false);
    /** The search form as typed, for the list the table shows. */
    const [draft, setDraft] = useState<Filter>(NO_FILTER);
    /**
     * Each list's search, by its key: going back to a list finds it as it was left, with the same
     * rows the rail draws for it.
     */
    const [filters, setFilters] = useState<Record<string, Filter>>({});
    /** Each list's sort, by its key, as its header was last clicked; sorted on the server. */
    const [sorts, setSorts] = useState<Record<string, SortState>>({});
    /** Bumped when a table's columns are sized or moved: the layout is read from the size store. */
    const [, setColumnsMoved] = useState(0);
    const [formOpen, setFormOpen] = useState(true);
    const [refreshed, setRefreshed] = useState(0);
    const [menu, setMenu] = useState<Menu | null>(null);
    const closeMenu = useCallback(() => setMenu(null), []);
    /** The rail row the keys act on, as VS Code's tree focuses one; the selection follows it. */
    const [focusKey, setFocusKey] = useState<string | null>(null);
    const railRef = useRef<HTMLElement>(null);
    // The rail is dragged by its right edge, the search form by its top; each size is kept.
    const railWidth = useEdgeResize(
        readPanelSize(RAIL_WIDTH_KEY, 260),
        160,
        640,
        "e",
        (width) => writePanelSize(RAIL_WIDTH_KEY, width),
    );
    const searchHeight = useEdgeResize(
        readPanelSize(SEARCH_HEIGHT_KEY, 200),
        96,
        600,
        "n",
        (height) => writePanelSize(SEARCH_HEIGHT_KEY, height),
    );

    useEffect(() => {
        const handle = (event: MessageEvent) => {
            const msg = event.data as {
                type?: string;
                outline?: SymbolOutline;
            };
            if (msg.type === "outline" && msg.outline) {
                setOutline(msg.outline);
                setCurrent(null);
                setTableList(null);
                setTabs(NO_TABS);
                setExpanded(new Set());
                setLoaded({});
                setNodes({});
                setPreviews({});
                setFilters({});
                setSorts({});
                setDraft(NO_FILTER);
            }
            // The index changed: what was loaded may be stale, so it is asked again.
            if (msg.type === "refresh") {
                setLoaded({});
                setNodes({});
                setPreviews({});
                setRefreshed((n) => n + 1);
            }
        };
        window.addEventListener("message", handle);
        vscode.postMessage({ type: "ready" });
        return () => window.removeEventListener("message", handle);
    }, []);

    const tops = useMemo<ListNode[]>(
        () =>
            (summary?.pages ?? []).map((p) => ({
                kind: "list",
                key: p.page,
                page: p.page,
                label: pageLabel(p.page),
                count: p.count,
                reason: p.count === 0 ? "none" : null,
                trail: [],
            })),
        [summary],
    );

    useEffect(() => {
        if (outline === null) {
            return;
        }
        void request<GetSymbolOutlineResult>(GET_SYMBOL_OUTLINE, {
            outline,
        }).then((result) => {
            setSummary(result);
            const first = result?.pages[0];
            if (first) {
                const firstList: ListNode = {
                    kind: "list",
                    key: first.page,
                    page: first.page,
                    label: pageLabel(first.page),
                    count: first.count,
                    trail: [],
                };
                setCurrent((open) => open ?? firstList);
                setTableList((open) => open ?? firstList);
                setExpanded((open) => new Set(open).add(first.page));
            }
        });
    }, [outline, refreshed]);

    const list = tableList;
    const filter = (list && filters[list.key]) ?? NO_FILTER;
    const sort = list ? sorts[list.key] : undefined;

    const load = useCallback(
        async (
            node: ListNode,
            applied: Filter,
            sorted: SortState | undefined,
            offset: number,
        ) => {
            setLoading(true);
            const params: GetSymbolPageParams = {
                page: node.page,
                parent: node.parent,
                query: applied.query || undefined,
                kinds: applied.kinds.length ? applied.kinds : undefined,
                origins: applied.origins.length ? applied.origins : undefined,
                sort: sorted?.column,
                descending: sorted?.direction === "desc" || undefined,
                offset,
                limit: PAGE_SIZE,
            };
            const result = await request<GetSymbolPageResult>(
                GET_SYMBOL_PAGE,
                params,
            );
            setLoaded((known) => ({
                ...known,
                [node.key]: {
                    rows:
                        offset === 0
                            ? (result?.rows ?? [])
                            : [
                                  ...(known[node.key]?.rows ?? []),
                                  ...(result?.rows ?? []),
                              ],
                    total: result?.total ?? 0,
                },
            }));
            setKinds(result?.kinds ?? []);
            setLoading(false);
        },
        [],
    );

    useEffect(() => {
        if (list !== null) {
            void load(list, filter, sort, 0);
        }
        // The list is followed by its key: a new object for the same list must not reload it.
    }, [list?.key, filter, sort, refreshed, load]);

    const shownOf = (node: ListNode): Loaded =>
        loaded[node.key] ?? { rows: [], total: 0 };
    const { rows, total } = list ? shownOf(list) : { rows: [], total: 0 };

    const childrenOf = (node: RailNode): RailNode[] =>
        node.kind === "entry"
            ? (nodes[node.key]?.relations ?? []).map((r) => relationOf(node, r))
            : shownOf(node).rows.map((row) => entryOf(node, row));

    const moreOf = (node: ListNode): number => {
        const shown = shownOf(node);
        return Math.max(0, shown.total - shown.rows.length);
    };

    /** The entry opened on the server - its relations and actions - asked for once per path. */
    const fetchNode = async (
        node: EntryNode,
    ): Promise<GetSymbolNodeResult | null> => {
        if (!node.row.page) {
            return null;
        }
        const known = nodes[node.key];
        if (known) {
            return known;
        }
        const result = await request<GetSymbolNodeResult>(GET_SYMBOL_NODE, {
            page: node.page,
            id: node.row.id,
        });
        if (result) {
            setNodes((known) => ({ ...known, [node.key]: result }));
        }
        return result;
    };

    const fetchList = async (node: ListNode): Promise<void> => {
        if (loaded[node.key]) {
            return;
        }
        const result = await request<GetSymbolPageResult>(GET_SYMBOL_PAGE, {
            page: node.page,
            parent: node.parent,
            sort: sorts[node.key]?.column,
            descending: sorts[node.key]?.direction === "desc" || undefined,
            limit: PAGE_SIZE,
        });
        setLoaded((known) => ({
            ...known,
            [node.key]: {
                rows: result?.rows ?? [],
                total: result?.total ?? 0,
            },
        }));
    };

    const expand = (node: RailNode, open: boolean) =>
        setExpanded((keys) => {
            const next = new Set(keys);
            if (open) {
                next.add(node.key);
            } else {
                next.delete(node.key);
            }
            return next;
        });

    const toggle = (node: RailNode) => {
        const open = !expanded.has(node.key);
        expand(node, open);
        if (open) {
            void (node.kind === "entry" ? fetchNode(node) : fetchList(node));
        }
    };

    /** The table's tab shows a list, with the search that list was left with. */
    const showList = (list: ListNode) => {
        if (list.key !== tableList?.key) {
            setDraft(filters[list.key] ?? NO_FILTER);
        }
        setTableList(list);
        setTabs((open) => showTable(open));
    };

    const select = (node: RailNode) => {
        if (node.kind === "list") {
            showList(node);
            expand(node, true);
        } else {
            // An entry is selected in the list it is a row of: selecting it again after one of its
            // own relations brings that list back, the entry's row selected.
            const home = node.trail[node.trail.length - 1];
            if (home?.kind === "list") {
                showList(home);
            }
            void fetchNode(node);
        }
        setCurrent(node);
    };

    /** Goes to where a row's definition is, for one that names a place rather than a page. */
    const goToRow = (row: SymbolRow): void => {
        if (row.location) {
            vscode.postMessage({
                type: "openLocation",
                uri: row.location.uri,
                line: row.location.line,
            });
        }
    };

    /**
     * Opens an entry in the foreground, in its own tab or the one unpinned preview tab. An entry
     * whose type has no preview is only selected: a tab with its header alone would show nothing.
     */
    const openEntry = async (node: EntryNode): Promise<void> => {
        if (!node.row.page) {
            goToRow(node.row);
            return;
        }
        if ((await fetchNode(node))?.open?.reason) {
            select(node);
            return;
        }
        expand(node, true);
        setCurrent(node);
        setTabs((known) => openPreview(known, tabKey(node), node));
        void fetchPreview(node);
    };

    /** The entry's preview, asked for once per entry and index. */
    const fetchPreview = async (node: EntryNode): Promise<void> => {
        const key = tabKey(node);
        if (previews[key]) {
            return;
        }
        const result = await request<GetSymbolPreviewResult>(
            GET_SYMBOL_PREVIEW,
            { page: node.page, id: node.row.id },
        );
        setPreviews((known) => ({
            ...known,
            [key]: result ?? { adapter: null, tiles: [] },
        }));
    };

    /** Does one item of an entry's menu. */
    const act = async (action: string, node: EntryNode): Promise<void> => {
        if (action === "copyName") {
            vscode.postMessage({ type: "copy", text: node.row.name });
            return;
        }
        if (!node.row.page) {
            // An overridden definition has no page; opening it, or going to it, is its place.
            if (action === "open" || action === "definition") {
                goToRow(node.row);
            }
            return;
        }
        if (action === "open") {
            await openEntry(node);
        } else if (action === "reveal") {
            const file = (await fetchNode(node))?.file;
            if (file) {
                vscode.postMessage({ type: "reveal", path: file });
            }
        } else if (action === "definition") {
            const location = (await fetchNode(node))?.location;
            if (location) {
                vscode.postMessage({
                    type: "openLocation",
                    uri: location.uri,
                    line: location.line,
                });
            }
        } else {
            vscode.postMessage({
                type: "run",
                action,
                id: node.row.id,
                name: node.row.name,
            });
        }
    };

    const openMenu = (
        x: number,
        y: number,
        node: EntryNode,
        anchor: number,
    ) => {
        setMenu({ x, y, node, anchor });
        void fetchNode(node);
    };

    const layers = summary?.layers ?? [];

    /**
     * A column's filter in the table's header. It is the search form's own filter - a change in
     * either is the other's - and applies at once. The type has none: a list holds one type, or
     * its entries' types are what the relation is.
     */
    const columnFilter = (
        column: string,
        applied: Filter,
        apply: (next: Filter) => void,
    ): ColumnFilter | undefined => {
        const toggle = (values: string[], value: string): string[] =>
            values.includes(value)
                ? values.filter((v) => v !== value)
                : [...values, value];
        switch (column) {
            case "name":
                return {
                    active: applied.query !== "",
                    content: (
                        <NameFilter
                            value={applied.query}
                            onApply={(query) => apply({ ...applied, query })}
                        />
                    ),
                };
            case "kinds": {
                const choices = kindChoices(kinds, applied.kinds);
                return {
                    active: applied.kinds.length > 0,
                    reason:
                        choices.length === 0
                            ? l10n.t("Only a game object behaves like a kind")
                            : undefined,
                    content: (
                        <span className="tag-row">
                            {choices.map((k) => (
                                <button
                                    key={k.kind}
                                    type="button"
                                    className="tag"
                                    aria-pressed={applied.kinds.includes(
                                        k.kind,
                                    )}
                                    disabled={k.disabled}
                                    title={
                                        k.disabled
                                            ? l10n.t("None in this list")
                                            : undefined
                                    }
                                    onClick={() =>
                                        apply({
                                            ...applied,
                                            kinds: toggle(
                                                applied.kinds,
                                                k.kind,
                                            ),
                                        })
                                    }
                                >
                                    {k.label}
                                </button>
                            ))}
                        </span>
                    ),
                };
            }
            case "origin":
                return {
                    active: applied.origins.length > 0,
                    content: (
                        <span className="tag-row">
                            {originChoices(layers).map((o) => (
                                <button
                                    key={o}
                                    type="button"
                                    className="tag"
                                    aria-pressed={applied.origins.includes(o)}
                                    onClick={() =>
                                        apply({
                                            ...applied,
                                            origins: toggle(applied.origins, o),
                                        })
                                    }
                                >
                                    {originLabel(o, null, layers)}
                                </button>
                            ))}
                        </span>
                    ),
                };
            case "type":
                return {
                    active: false,
                    reason: l10n.t("No filter for the type"),
                    content: null,
                };
            case "value":
                return {
                    active: false,
                    reason: l10n.t("No filter for the value"),
                    content: null,
                };
            default:
                return undefined;
        }
    };

    if (outline === null || summary === null || list === null) {
        return (
            <Shell>
                <p className="empty">{l10n.t("Loading...")}</p>
            </Shell>
        );
    }

    // The rail, as VS Code's tree: its visible rows in order, one of them focused, keys moving the
    // focus and the selection with it.
    const railRows: RailRow[] = [];
    const walk = (node: RailNode, depth: number): void => {
        const open = expanded.has(node.key);
        const inert =
            node.kind === "entry"
                ? rowInertReason(node.row) !== undefined
                : node.count === 0;
        railRows.push({ key: node.key, node, depth, open, inert });
        if (!open || inert) {
            return;
        }
        childrenOf(node).forEach((child) => walk(child, depth + 1));
        const more = node.kind === "list" ? moreOf(node) : 0;
        if (more > 0) {
            railRows.push({
                key: `${node.key}\nmore`,
                node,
                depth: depth + 1,
                open: false,
                inert: false,
                more,
            });
        }
    };
    tops.forEach((top) => walk(top, 0));
    const expandable = (row: RailRow): boolean =>
        row.more === undefined && !row.inert;
    const focused =
        railRows.find((row) => row.key === focusKey) ??
        railRows.find((row) => row.key === current?.key) ??
        railRows[0];

    /** A row chosen: a list shows in the table - the more row its own list - and an entry is selected. */
    const chooseRow = (row: RailRow): void => {
        setFocusKey(row.key);
        if (!row.inert) {
            select(row.node);
        }
    };

    /**
     * A row the keys moved to: the selection follows, as in VS Code's tree, but walking past a list
     * shows it without unfolding it, and the more row waits for Enter.
     */
    const focusRow = (row: RailRow): void => {
        setFocusKey(row.key);
        if (row.inert || row.more !== undefined) {
            return;
        }
        if (row.node.kind === "list") {
            showList(row.node);
            setCurrent(row.node);
        } else {
            select(row.node);
        }
    };

    const onRailKey = (e: React.KeyboardEvent): void => {
        const at = railRows.indexOf(focused);
        if (at < 0) {
            return;
        }
        const node = focused.node;
        if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            if (e.key === "Enter" && node.kind === "entry" && !focused.more) {
                // Enter opens; a click and the space bar only select.
                void openEntry(node);
            } else {
                chooseRow(focused);
            }
            return;
        }
        if (e.key === "F12" && node.kind === "entry" && !focused.more) {
            e.preventDefault();
            void act("definition", node);
            return;
        }
        const page = Math.max(
            1,
            Math.floor((railRef.current?.clientHeight ?? 220) / 22) - 1,
        );
        const step = treeStep(
            railRows.map((row) => ({
                depth: row.depth,
                expandable: expandable(row),
                expanded: row.open,
            })),
            at,
            e.key,
            page,
        );
        if (step === undefined) {
            return;
        }
        e.preventDefault();
        if ("to" in step) {
            focusRow(railRows[step.to]);
            railRef.current
                ?.querySelector<HTMLElement>(`[data-index="${step.to}"]`)
                ?.focus();
        } else {
            toggle(node);
        }
    };

    const renderRow = (row: RailRow, index: number): React.JSX.Element => {
        const { node } = row;
        const canOpen = expandable(row);
        const selected = row.more === undefined && current?.key === node.key;
        return (
            <div
                key={row.key}
                role="treeitem"
                data-index={index}
                className={
                    "node" +
                    (selected ? " selected" : "") +
                    (row.more !== undefined ? " more" : "") +
                    (row.inert ? " inert" : "")
                }
                style={{ "--depth": row.depth } as React.CSSProperties}
                tabIndex={row === focused ? 0 : -1}
                aria-level={row.depth + 1}
                aria-expanded={canOpen ? row.open : undefined}
                aria-selected={selected}
                aria-disabled={row.inert || undefined}
                title={row.inert ? inertReason(node) : undefined}
                onFocus={() => setFocusKey(row.key)}
                onClick={() => chooseRow(row)}
                onDoubleClick={() =>
                    node.kind === "entry" &&
                    row.more === undefined &&
                    openEntry(node)
                }
                onContextMenu={(e) => {
                    if (node.kind === "entry" && row.more === undefined) {
                        e.preventDefault();
                        openMenu(e.clientX, e.clientY, node, -1);
                    }
                }}
            >
                {Array.from({ length: row.depth }, (_, level) => (
                    <span
                        key={level}
                        className="guide"
                        style={{ "--level": level } as React.CSSProperties}
                    />
                ))}
                <span
                    className="twisty"
                    title={
                        canOpen
                            ? row.open
                                ? l10n.t("Collapse")
                                : l10n.t("Expand")
                            : undefined
                    }
                    onClick={(e) => {
                        // The twisty only folds; the row around it selects.
                        e.stopPropagation();
                        if (canOpen) {
                            toggle(node);
                        }
                    }}
                >
                    {canOpen && (
                        <Icon
                            name={row.open ? "expanded" : "collapsed"}
                            size={16}
                        />
                    )}
                </span>
                {row.more === undefined ? (
                    <span className="node-icon">
                        <Icon name={nodeIcon(node)} size={16} />
                    </span>
                ) : (
                    <span />
                )}
                <span className="label">
                    {row.more !== undefined
                        ? l10n.t("{0} more", row.more.toLocaleString())
                        : nodeName(node)}
                </span>
                {node.kind === "list" && row.more === undefined ? (
                    <span className="count">
                        {node.count?.toLocaleString() ?? ""}
                    </span>
                ) : (
                    <span />
                )}
            </div>
        );
    };

    const activeTab =
        tabs.previews.find((t) => t.key === tabs.active)?.item ?? null;
    const shown: RailNode = activeTab ?? list;
    const crumbs = [...shown.trail, shown];
    const selectedRow =
        current?.kind === "entry" && current.key.startsWith(`${list.key}\n`)
            ? current.row.id
            : null;

    return (
        <Shell
            style={
                {
                    "--rail-width": `${railWidth.size}px`,
                } as React.CSSProperties
            }
        >
            <div className="rail-frame">
                <nav
                    ref={railRef}
                    className="rail"
                    role="tree"
                    aria-label={l10n.t("Lists")}
                    onKeyDown={onRailKey}
                >
                    {railRows.map(renderRow)}
                </nav>
                <div
                    className="resize-handle-e"
                    title={l10n.t("Drag to resize")}
                    {...railWidth.handleProps}
                />
            </div>
            <main className="page">
                <TabStrip
                    label={l10n.t("Open entries")}
                    home={list.label}
                    tabs={tabs.previews.map((t) => ({
                        key: t.key,
                        label: t.item.row.name,
                        pinned: t.pinned,
                    }))}
                    active={tabs.active}
                    onHome={() => setTabs((known) => showTable(known))}
                    onSelect={(key) =>
                        setTabs((known) => ({ ...known, active: key }))
                    }
                    onPin={(key) => setTabs((known) => pinTab(known, key))}
                    onClose={(key) => setTabs((known) => closeTab(known, key))}
                />
                <header className="crumbs">
                    {crumbs.map((node, i) => (
                        <span key={node.key} className="crumb-step">
                            {i > 0 && <Separator />}
                            {i < crumbs.length - 1 ? (
                                <button
                                    type="button"
                                    className="crumb"
                                    onClick={() =>
                                        node.kind === "list"
                                            ? select(node)
                                            : openEntry(node)
                                    }
                                >
                                    {nodeName(node)}
                                </button>
                            ) : (
                                <b>{nodeName(node)}</b>
                            )}
                        </span>
                    ))}
                    {!activeTab && (
                        <span className="end">
                            {l10n.t(
                                "{0} of {1}",
                                rows.length.toLocaleString(),
                                total.toLocaleString(),
                            )}
                        </span>
                    )}
                </header>
                {activeTab ? (
                    <TabContent
                        node={activeTab}
                        preview={previews[tabKey(activeTab)]}
                        result={nodes[activeTab.key] ?? null}
                        layers={layers}
                        onOpen={openEntry}
                        onAct={(action) => void act(action, activeTab)}
                    />
                ) : (
                    <ListPage
                        label={list.label}
                        rows={rows}
                        total={total}
                        loading={loading}
                        layers={layers}
                        selectedId={selectedRow}
                        nodeOf={(row) => nodes[entryOf(list, row).key]}
                        onHover={(row) => void fetchNode(entryOf(list, row))}
                        onSelect={(row) => select(entryOf(list, row))}
                        onAct={(action, row) =>
                            void act(action, entryOf(list, row))
                        }
                        onMenu={(x, y, row, index) =>
                            openMenu(x, y, entryOf(list, row), index)
                        }
                        onMore={() =>
                            void load(list, filter, sort, rows.length)
                        }
                        sort={sort ?? null}
                        onSort={(next) =>
                            setSorts((known) => {
                                const rest = { ...known };
                                delete rest[list.key];
                                return next
                                    ? { ...rest, [list.key]: next }
                                    : rest;
                            })
                        }
                        columnKeys={listColumns(list.parent?.relation)}
                        layout={readColumnLayout(
                            columnsOf(list),
                            listColumns(list.parent?.relation),
                        )}
                        onResize={(widths) => {
                            for (const [key, width] of Object.entries(widths)) {
                                writeColumnWidth(columnsOf(list), key, width);
                            }
                            setColumnsMoved((n) => n + 1);
                        }}
                        onMove={(from, to) => {
                            const table = columnsOf(list);
                            writeColumnOrder(
                                table,
                                moveColumn(
                                    readColumnLayout(
                                        table,
                                        listColumns(list.parent?.relation),
                                    ).order,
                                    from,
                                    to,
                                ),
                            );
                            setColumnsMoved((n) => n + 1);
                        }}
                        filter={(column) =>
                            columnFilter(column, filter, (next) => {
                                setDraft(next);
                                setFilters((known) => ({
                                    ...known,
                                    [list.key]: next,
                                }));
                            })
                        }
                    />
                )}
                {!activeTab && (
                    <SearchForm
                        open={formOpen}
                        onToggle={() => setFormOpen((o) => !o)}
                        title={
                            list.parent
                                ? l10n.t(
                                      "{0} of {1}",
                                      list.label,
                                      nodeName(
                                          list.trail[list.trail.length - 1],
                                      ),
                                  )
                                : list.label
                        }
                        height={searchHeight.size}
                        resize={searchHeight.handleProps}
                        draft={draft}
                        kinds={kindChoices(kinds, draft.kinds)}
                        origins={originChoices(layers)}
                        layers={layers}
                        onChange={setDraft}
                        onSearch={() =>
                            setFilters((known) => ({
                                ...known,
                                [list.key]: draft,
                            }))
                        }
                        onReset={() => {
                            setDraft(NO_FILTER);
                            setFilters((known) => ({
                                ...known,
                                [list.key]: NO_FILTER,
                            }));
                        }}
                    />
                )}
            </main>
            {menu && (
                <EntryMenu
                    menu={menu}
                    choices={menuChoices(
                        actionChoices(
                            !!menu.node.row.page,
                            nodes[menu.node.key]?.actions,
                            nodes[menu.node.key]?.open,
                        ),
                    )}
                    onAct={(action) => void act(action, menu.node)}
                    onClose={closeMenu}
                />
            )}
        </Shell>
    );
}

/** An entry's context menu: each item with the key that does the same, a disabled one with why. */
function EntryMenu({
    menu,
    choices,
    onAct,
    onClose,
}: {
    menu: Menu;
    choices: ActionChoice[][];
    onAct: (action: string) => void;
    onClose: () => void;
}): React.JSX.Element {
    return (
        <RowMenuFrame
            x={menu.x}
            y={menu.y}
            anchorIndex={menu.anchor}
            onClose={onClose}
            focusFirst
        >
            {choices.map((group, g) => (
                <div key={g} className="menu-group">
                    {g > 0 && <div className="sep" />}
                    {group.map((c) => (
                        <button
                            key={c.action}
                            type="button"
                            role="menuitem"
                            disabled={c.disabled}
                            onClick={menuAction(() => onAct(c.action), onClose)}
                        >
                            {c.label}
                            <span className="keys">
                                {c.disabled ? c.reason : c.keys}
                            </span>
                        </button>
                    ))}
                </div>
            ))}
        </RowMenuFrame>
    );
}

function Separator(): React.JSX.Element {
    return (
        <span className="sep" aria-hidden="true">
            <Icon name="collapsed" size={12} />
        </span>
    );
}

/** Why a node opens nothing: a name nothing defines, a place rather than an entry, or an empty list. */
function inertReason(node: RailNode): string {
    if (node.kind === "list") {
        return reasonLabel(node.reason);
    }
    return node.row.origin === "missing"
        ? l10n.t("Nothing defines this name")
        : l10n.t("Not an entry of its own");
}

/** A list's rows as the shared table: a click selects, a double click or Enter opens, F12 goes to the definition. */
function ListPage({
    label,
    rows,
    total,
    loading,
    layers,
    selectedId,
    nodeOf,
    onHover,
    onSelect,
    onAct,
    onMenu,
    onMore,
    sort,
    onSort,
    layout,
    onResize,
    onMove,
    filter,
    columnKeys,
}: {
    label: string;
    rows: SymbolRow[];
    total: number;
    loading: boolean;
    layers: GetSymbolOutlineResult["layers"];
    /** The selected row's id, when the selection is one of this list's rows. */
    selectedId: string | null;
    /** The row's entry as the server opened it, once it has. */
    nodeOf: (row: SymbolRow) => GetSymbolNodeResult | undefined;
    onHover: (row: SymbolRow) => void;
    onSelect: (row: SymbolRow) => void;
    onAct: (action: string, row: SymbolRow) => void;
    onMenu: (x: number, y: number, row: SymbolRow, index: number) => void;
    onMore: () => void;
    /** The column the server sorts the list by, as its header was last clicked. */
    sort: SortState | null;
    onSort: (next: SortState | null) => void;
    /** The list's columns as the reader arranged them, and the drags that arrange them. */
    layout: ColumnLayout;
    onResize: (widths: Record<string, number>) => void;
    onMove: (from: string, to: string) => void;
    filter: (column: string) => ColumnFilter | undefined;
    /** The columns this list shows, in its own order. */
    columnKeys: string[];
}): React.JSX.Element {
    const hover = useRef<number | undefined>(undefined);
    const rest = (row: SymbolRow) => {
        window.clearTimeout(hover.current);
        if (row.page) {
            hover.current = window.setTimeout(
                () => onHover(row),
                HOVER_DELAY_MS,
            );
        }
    };
    useEffect(() => () => window.clearTimeout(hover.current), []);

    return (
        <DataTable<SymbolRow>
            label={label}
            columns={(
                [
                    { key: "name", label: l10n.t("Name"), cell: (r) => r.name },
                    {
                        key: "value",
                        label: l10n.t("Value"),
                        cell: (r) => r.value ?? "",
                    },
                    {
                        key: "type",
                        label: l10n.t("Type"),
                        cell: (r) => rowTypeLabel(r),
                        dim: true,
                    },
                    {
                        key: "kinds",
                        label: l10n.t("Behaves like"),
                        cell: (r) => r.kinds.join(", "),
                        dim: true,
                    },
                    {
                        key: "origin",
                        label: l10n.t("Origin"),
                        cell: (r) => (
                            <>
                                {originLabel(r.origin, r.originName, layers)}
                                {r.overrides.length > 0 && (
                                    <span className="tag outline">
                                        {l10n.t(
                                            "Overrides - {0}",
                                            originLabel(
                                                r.overrides[0],
                                                null,
                                                layers,
                                            ),
                                        )}
                                    </span>
                                )}
                            </>
                        ),
                    },
                ] satisfies DataColumn<SymbolRow>[]
            )
                .filter((c) => columnKeys.includes(c.key))
                .sort(
                    (a, b) =>
                        columnKeys.indexOf(a.key) - columnKeys.indexOf(b.key),
                )}
            rows={rows}
            rowKey={(r) => r.id}
            selected={selectedId}
            sortable={columnKeys}
            sort={sort}
            onSort={onSort}
            layout={layout}
            onResize={onResize}
            onMove={onMove}
            filter={filter}
            inert={rowInertReason}
            onSelect={onSelect}
            onOpen={(r) => onAct("open", r)}
            onKey={(key, r) => {
                if (key !== "F12" || rowInertReason(r) !== undefined) {
                    return false;
                }
                onAct("definition", r);
                return true;
            }}
            onMenu={onMenu}
            onRest={rest}
            actions={(r, index) => (
                <RowActions
                    row={r}
                    result={nodeOf(r)}
                    onAct={(action) => onAct(action, r)}
                    onMore={(x, y) => onMenu(x, y, r, index)}
                />
            )}
            empty={loading ? l10n.t("Loading...") : l10n.t("Nothing matches")}
            foot={
                <>
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
                        onClick={onMore}
                    >
                        {l10n.t("Load more")}
                    </Button>
                </>
            }
        />
    );
}

/**
 * A row's icons: go to definition and what the entry offers, disabled ones saying why, then the
 * whole menu. Drawn once the server has opened the entry, which resting on the row asks for.
 */
function RowActions({
    row,
    result,
    onAct,
    onMore,
}: {
    row: SymbolRow;
    result: GetSymbolNodeResult | undefined;
    onAct: (action: string) => void;
    onMore: (x: number, y: number) => void;
}): React.JSX.Element | null {
    if (!row.page || !result) {
        return null;
    }
    const [[, definition], offered = []] = actionChoices(true, result.actions);
    return (
        <>
            {[
                definition,
                ...offered.filter((c) => c.action !== "copyName"),
            ].map((c) => (
                <IconButton
                    key={c.action}
                    icon={ACTION_ICONS[c.action] ?? "open"}
                    title={c.label}
                    disabled={c.disabled}
                    disabledReason={`${c.label} - ${c.reason ?? ""}`}
                    onClick={(e) => {
                        e.stopPropagation();
                        onAct(c.action);
                    }}
                />
            ))}
            <IconButton
                icon="moreActions"
                title={l10n.t("More actions")}
                onClick={(e) => {
                    e.stopPropagation();
                    const box = e.currentTarget.getBoundingClientRect();
                    onMore(box.left, box.bottom);
                }}
            />
        </>
    );
}

/**
 * An open tab's content: the preview the server's adapter lists. An entry whose preview would be its
 * header alone has none, and is only ever selected; a tab can still meet one when the index changes
 * under it, and then says so.
 */
function TabContent({
    node,
    preview,
    result,
    layers,
    onOpen,
    onAct,
}: {
    node: EntryNode;
    preview: GetSymbolPreviewResult | undefined;
    result: GetSymbolNodeResult | null;
    layers: GetSymbolOutlineResult["layers"];
    onOpen: (node: EntryNode) => void;
    onAct: (action: string) => void;
}): React.JSX.Element {
    // Reached from this entry, so the way back is this entry.
    const reached = (row: SymbolRow): EntryNode => ({
        kind: "entry",
        key: `${node.key}\n${row.id}`,
        page: row.page ?? node.page,
        row,
        trail: [...node.trail, node],
    });
    const openBase = (base: string) =>
        onOpen(
            reached({
                id: base,
                name: base,
                kinds: [],
                origin: "",
                overrides: [],
                page: node.page,
            }),
        );

    if (preview === undefined) {
        return <p className="empty">{l10n.t("Loading...")}</p>;
    }
    if (!preview.adapter) {
        return <p className="empty">{l10n.t("No preview for this type")}</p>;
    }
    return (
        <PreviewPage
            tiles={preview.tiles}
            columns={preview.columns ?? 6}
            rows={preview.rows ?? 6}
            actions={result?.actions}
            layers={layers}
            handlers={{
                onOpenRow: (row) => onOpen(reached(row)),
                onOpenBase: openBase,
                onOpenLocation: (location) =>
                    vscode.postMessage({
                        type: "openLocation",
                        uri: location.uri,
                        line: location.line,
                    }),
                onAct,
                card: (objectId, width) => (
                    <CardSection objectId={objectId} fitWidth={width} />
                ),
                modelDetail: loadModelDetail,
            }}
        />
    );
}

/**
 * The object's encyclopedia card with its faction switch, asked for when the entry opens. In a
 * preview tile it is scaled to the tile's width, never above twice the game's size.
 */
function CardSection({
    objectId,
    fitWidth,
}: {
    objectId: string;
    fitWidth?: number;
}): React.JSX.Element {
    const [card, setCard] = useState<GetEncyclopediaEntryResult | null>(null);
    const [factionSlot, setFactionSlot] = useState(0);

    useEffect(() => {
        let live = true;
        setCard(null);
        void request<GetEncyclopediaEntryResult>(GET_ENCYCLOPEDIA_ENTRY, {
            objectId,
        }).then((result) => {
            if (live) {
                setCard(result);
            }
        });
        return () => {
            live = false;
        };
    }, [objectId]);

    if (card === null) {
        return <p className="empty">{l10n.t("Loading...")}</p>;
    }
    if (!card.found) {
        return (
            <p className="empty">
                {l10n.t("No encyclopedia entry for '{0}'.", objectId)}
            </p>
        );
    }

    if (!shipNames.has(objectId)) {
        shipNames.set(objectId, pickShipName(card.shipNames?.names ?? []));
    }
    const shipName = shipNames.get(objectId) ?? null;
    const zoom = fitWidth
        ? Math.min(CARD_ZOOM, fitWidth / card.layout.width)
        : CARD_ZOOM;

    return (
        <div className="card">
            <EncyclopediaCard
                entry={
                    shipName === null ? card : { ...card, unitClass: shipName }
                }
                zoom={zoom}
                factionSlot={factionSlot}
            />
            <Field label={l10n.t("Faction frame")}>
                <ModeSelector
                    label={l10n.t("Faction frame")}
                    value={String(factionSlot)}
                    options={factionModes(card.chrome?.factionFrames ?? [])}
                    onSelect={(id) => setFactionSlot(factionSlotOf(id))}
                />
            </Field>
        </div>
    );
}

function SearchForm({
    open,
    onToggle,
    title,
    height,
    resize,
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
    /** Its height while open, dragged by the handle along its top edge; shut, it is its head. */
    height: number;
    resize: Record<string, unknown>;
    draft: Filter;
    kinds: KindChoice[];
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
            style={open ? { height } : undefined}
            onSubmit={(event) => {
                event.preventDefault();
                onSearch();
            }}
        >
            {open && (
                <div
                    className="resize-handle-n"
                    title={l10n.t("Drag to resize")}
                    {...resize}
                />
            )}
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
                                <span>{l10n.t("Behaves like")}</span>
                                <span className="tag-row">
                                    {kinds.map((k) => (
                                        <button
                                            key={k.kind}
                                            type="button"
                                            className="tag"
                                            aria-pressed={draft.kinds.includes(
                                                k.kind,
                                            )}
                                            disabled={k.disabled}
                                            title={
                                                k.disabled
                                                    ? l10n.t(
                                                          "None in this list",
                                                      )
                                                    : undefined
                                            }
                                            onClick={() =>
                                                onChange({
                                                    ...draft,
                                                    kinds: toggle(
                                                        draft.kinds,
                                                        k.kind,
                                                    ),
                                                })
                                            }
                                        >
                                            {k.label}
                                        </button>
                                    ))}
                                </span>
                            </>
                        )}
                        <span>{l10n.t("Origin")}</span>
                        <span className="tag-row">
                            {origins.map((o) => (
                                <button
                                    key={o}
                                    type="button"
                                    className="tag"
                                    aria-pressed={draft.origins.includes(o)}
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

// VS Code's own colours, so the panel follows the theme, on the chrome the other panels share. The
// rail and the page are a fixed grid; the table and the rail scroll in place.
const Shell = styled.div`
    ${dockChromeCss}
    ${dockHeaderCss}
    ${problemsPanelCss}

    display: grid;
    grid-template-columns: var(--rail-width, 260px) minmax(0, 1fr);
    height: 100vh;
    color: var(--colour-ink);
    background: var(--colour-editor-ground);
    font: var(--font-size-body) / 1.4 var(--vscode-font-family, sans-serif);

    button {
        font: inherit;
        color: inherit;
    }
    .empty {
        padding: var(--space-12) var(--space-16);
        color: var(--colour-muted);
    }

    /* The rail: VS Code's tree. 22px rows, 8px per level, 16px chevrons, a picture of what each node
       is, and an indent guide per level, shown while the pointer is over the tree or it has the
       focus, as the editor shows its own by default. Dragged wider by its right edge. */
    .rail-frame {
        position: relative;
        display: grid;
        min-height: 0;
        border-right: var(--space-1) solid var(--colour-line);
        background: var(--colour-sidebar);
    }
    .resize-handle-e {
        position: absolute;
        top: 0;
        right: -3px;
        width: 6px;
        height: 100%;
        cursor: ew-resize;
        z-index: 2;
    }
    .resize-handle-e:hover,
    .resize-handle-e:active {
        background: var(--vscode-sash-hoverBorder, var(--vscode-focusBorder));
    }
    .rail {
        overflow: auto;
        padding: var(--space-4) 0;
        outline: none;
    }
    .node {
        position: relative;
        display: grid;
        grid-template-columns:
            var(--icon-size-16) var(--icon-size-16) minmax(0, 1fr)
            auto;
        align-items: center;
        gap: var(--space-4);
        box-sizing: border-box;
        height: 22px;
        padding: 0 var(--space-8) 0
            calc(var(--space-8) + var(--depth, 0) * var(--space-8));
        cursor: pointer;
        user-select: none;
        content-visibility: auto;
        contain-intrinsic-size: auto 22px;
    }
    .node:hover {
        background: var(--colour-hover);
    }
    .node.selected {
        background: var(--colour-selected);
        color: var(--colour-selected-ink);
    }
    .node:focus {
        outline: var(--space-1) solid
            var(--vscode-list-focusOutline, var(--vscode-focusBorder));
        outline-offset: calc(-1 * var(--space-1));
    }
    .node.inert {
        color: var(--colour-faint);
        cursor: default;
    }
    .node.more {
        color: var(--colour-link);
    }
    /* One line per level, through the middle of that level's chevron. */
    .node .guide {
        position: absolute;
        top: 0;
        bottom: 0;
        left: calc(
            var(--space-8) + var(--level) * var(--space-8) +
                var(--icon-size-16) / 2
        );
        width: var(--space-1);
        background: var(
            --vscode-tree-indentGuidesStroke,
            var(--colour-line-strong)
        );
        opacity: 0;
    }
    .rail:hover .node .guide,
    .rail:focus-within .node .guide {
        opacity: 1;
    }
    .node .twisty,
    .node .node-icon {
        display: grid;
        place-items: center;
    }
    .node .node-icon {
        color: var(--colour-muted);
    }
    .node .label {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }
    .node .count {
        color: var(--colour-muted);
        font-size: var(--font-size-11);
        font-variant-numeric: tabular-nums;
    }

    /* The page: its tabs, the breadcrumb, what the active tab shows, and the open list's search. */
    .page {
        display: grid;
        grid-template-rows: auto auto minmax(0, 1fr) auto;
        min-width: 0;
        min-height: 0;
    }
    .crumbs {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-6);
        padding: var(--space-6) var(--space-16);
        border-bottom: var(--space-1) solid var(--colour-line);
    }
    .crumb-step {
        display: inline-flex;
        align-items: center;
        gap: var(--space-6);
    }
    .crumbs .crumb {
        border: 0;
        background: none;
        padding: 0;
        cursor: pointer;
        color: var(--colour-link);
    }
    .crumbs .sep {
        display: inline-flex;
        color: var(--colour-muted);
    }
    .crumbs .end {
        margin-left: auto;
        color: var(--colour-muted);
    }
    .data-table .tag {
        margin-left: var(--space-6);
    }

    /* Open, the form keeps the height it was dragged to by its top edge, its fields scrolling and
       its buttons at its foot. */
    .search {
        position: relative;
        display: flex;
        flex-direction: column;
        box-sizing: border-box;
        min-height: 0;
        border-top: var(--space-1) solid var(--colour-line);
        background: var(--colour-sidebar);
    }
    .search .search-grid {
        flex: 1;
        min-height: 0;
        overflow: auto;
        align-content: start;
    }
    .search-head {
        display: flex;
        gap: var(--space-8);
        align-items: center;
        width: 100%;
        padding: var(--space-6) var(--space-16);
        border: 0;
        background: none;
        text-align: left;
        cursor: pointer;
    }
    .search-head .end {
        margin-left: auto;
    }
    .search-head .dim {
        color: var(--colour-muted);
    }
    .search-grid {
        display: grid;
        grid-template-columns: max-content minmax(0, 1fr);
        gap: var(--space-6) var(--space-8);
        padding: var(--space-8) var(--space-16);
        align-items: center;
    }
    .search-grid input {
        background: var(--colour-input);
        color: var(--colour-input-ink);
        border: var(--space-1) solid var(--colour-input-line);
        padding: var(--space-2) var(--space-6);
    }
    .search-foot {
        display: flex;
        justify-content: flex-end;
        gap: var(--space-8);
        padding: 0 var(--space-16) var(--space-8);
    }

    /* An entry's preview: the tile grid and the action tiles beside it - or above it, as one row,
       when the panel is too narrow. Cell sizes come in as inline styles from PREVIEW_CELL. */
    /* The grid and the actions take the height they need and no more; the tab scrolls when they
       need more than it has. */
    .preview {
        display: grid;
        grid-template-rows: auto auto;
        align-content: start;
        gap: var(--space-16);
        padding: var(--space-16);
        overflow: auto;
        min-height: 0;
    }
    .preview:not(.beside) .preview-actions {
        order: -1;
    }
    .preview.beside {
        grid-template-columns: minmax(0, 1fr) auto;
        grid-template-rows: auto;
        align-items: start;
    }
    .preview-grid {
        display: grid;
        min-width: 0;
        min-height: 0;
    }
    /* The action tiles keep the shared grid's centring in their column, set apart from the
       content tiles beside them. */

    /* The entry's header is the one tile set apart, as the reference's identity tile is: the same
       panel frame as its neighbours, washed with the theme's accent and edged along the top in it,
       as the editor marks its active tab. A tint rather than a fill, so the tags and the links in
       it keep their own colours and their contrast in every theme. */
    .entry-head {
        border-color: color-mix(
            in srgb,
            var(--colour-action) 45%,
            var(--colour-line-strong)
        );
        border-top: var(--space-2) solid var(--colour-action);
        background: color-mix(
            in srgb,
            var(--colour-action) 14%,
            var(--colour-panel)
        );
    }
    .entry-head .panel-body {
        display: grid;
        align-content: start;
        gap: var(--space-8);
        padding: var(--space-12);
    }
    .entry-title {
        margin: 0;
        font-size: var(--font-size-16);
        font-weight: 600;
        overflow-wrap: anywhere;
    }
    .card {
        display: inline-grid;
        gap: var(--space-8);
    }

    /* A schema element's documentation: its properties, then its description and its notes, which
       wrap. An enum value is said, not opened, so it reads as text rather than as a link. */
    .documentation {
        display: grid;
        gap: var(--space-8);
        align-content: start;
    }
    .documentation .doc-text {
        margin: 0;
        white-space: pre-line;
    }
    .documentation .doc-notes {
        display: grid;
        gap: var(--space-4);
        margin: 0;
        padding: 0;
        list-style: none;
    }
    .entry-row.plain {
        /* A list item, not a button: sized as a button is, so its padding does not overflow. */
        box-sizing: border-box;
        cursor: default;
    }
    .entry-row.plain:hover {
        background: none;
    }
    .entry-row.plain .entry-name {
        color: var(--colour-ink);
    }
    /* An enum value's value beside its name, where the engine uses another. */
    .entry-row.plain .entry-value {
        margin-left: var(--space-8);
        color: var(--colour-muted);
        font-family: var(--vscode-editor-font-family, monospace);
    }

    @media (max-width: 700px) {
        grid-template-columns: minmax(0, 1fr);
        grid-template-rows: 40vh minmax(0, 1fr);
        .rail-frame {
            border-right: 0;
            border-bottom: var(--space-1) solid var(--colour-line);
        }
        .resize-handle-e {
            display: none;
        }
    }
`;

createRoot(document.getElementById("root")!).render(<App />);
