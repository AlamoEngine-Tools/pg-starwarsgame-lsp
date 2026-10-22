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
import { DataTable } from "./shared/DataTable";
import { menuAction, RowMenuFrame } from "./shared/RowMenu";
import { TabStrip } from "./shared/TabStrip";
import {
    ActionChoice,
    actionChoices,
    KindChoice,
    kindChoices,
    menuChoices,
    originChoices,
    originLabel,
    pageLabel,
    reasonLabel,
    relationLabel,
    PREVIEW_ACTIONS,
} from "./modBrowserModel";
import {
    ACTION_ICONS,
    Actions,
    EntryHead,
    PreviewPage,
} from "./modBrowserPreview";
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
import { Icon } from "./shared/Icon";
import { ModeSelector } from "./shared/ModeSelector";

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

const vscode = acquireVsCodeApi();

const PAGE_SIZE = 200;
const GAME_OBJECT_TYPE = "GameObjectType";
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
    /** The list the table last showed, whose search form the panel holds. */
    const [tableKey, setTableKey] = useState<string | null>(null);
    const [kinds, setKinds] = useState<SymbolKindCount[]>([]);
    const [loading, setLoading] = useState(false);
    const [draft, setDraft] = useState<Filter>(NO_FILTER);
    const [filter, setFilter] = useState<Filter>(NO_FILTER);
    const [formOpen, setFormOpen] = useState(true);
    const [refreshed, setRefreshed] = useState(0);
    const [menu, setMenu] = useState<Menu | null>(null);
    const closeMenu = useCallback(() => setMenu(null), []);

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

    const load = useCallback(
        async (node: ListNode, applied: Filter, offset: number) => {
            setLoading(true);
            const params: GetSymbolPageParams = {
                page: node.page,
                parent: node.parent,
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
            setTableKey(node.key);
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
            void load(list, filter, 0);
        }
        // The list is followed by its key: a new object for the same list must not reload it.
    }, [list?.key, filter, refreshed, load]);

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

    const select = (node: RailNode) => {
        if (node.kind === "list") {
            // Back to the list the table holds keeps its search; another list starts unfiltered.
            if (node.key !== tableKey) {
                setDraft(NO_FILTER);
                setFilter(NO_FILTER);
            }
            setTableList(node);
            setTabs((open) => showTable(open));
            expand(node, true);
        } else {
            void fetchNode(node);
        }
        setCurrent(node);
    };

    /** Opens an entry in the foreground, in its own tab or the one unpinned preview tab. */
    const openEntry = (node: EntryNode) => {
        if (!node.row.page) {
            return;
        }
        void fetchNode(node);
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
            return;
        }
        if (action === "open") {
            openEntry(node);
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

    if (outline === null || summary === null || list === null) {
        return (
            <Shell>
                <p className="empty">{l10n.t("Loading...")}</p>
            </Shell>
        );
    }

    const renderNode = (node: RailNode, depth: number): React.JSX.Element => {
        const open = expanded.has(node.key);
        const inert = node.kind === "entry" ? !node.row.page : node.count === 0;
        const more = node.kind === "list" && open ? moreOf(node) : 0;
        return (
            <div key={node.key}>
                <div
                    className={`node${current?.key === node.key ? " selected" : ""}`}
                    style={{ "--depth": depth } as React.CSSProperties}
                    onContextMenu={(e) => {
                        if (node.kind === "entry") {
                            e.preventDefault();
                            openMenu(e.clientX, e.clientY, node, -1);
                        }
                    }}
                >
                    <button
                        type="button"
                        className="twisty"
                        disabled={inert}
                        title={
                            inert
                                ? undefined
                                : open
                                  ? l10n.t("Collapse")
                                  : l10n.t("Expand")
                        }
                        onClick={() => toggle(node)}
                    >
                        {!inert && (
                            <Icon
                                name={open ? "expanded" : "collapsed"}
                                size={12}
                            />
                        )}
                    </button>
                    <button
                        type="button"
                        className="label"
                        disabled={inert}
                        title={inert ? inertReason(node) : undefined}
                        onClick={() => select(node)}
                        onDoubleClick={() =>
                            node.kind === "entry" && openEntry(node)
                        }
                        onKeyDown={(e) => {
                            if (node.kind !== "entry") {
                                return;
                            }
                            if (e.key === "Enter") {
                                // Not a click: a click only selects.
                                e.preventDefault();
                                openEntry(node);
                            } else if (e.key === "F12") {
                                e.preventDefault();
                                void act("definition", node);
                            }
                        }}
                    >
                        {nodeName(node)}
                        {node.kind === "list" && node.count === 0 && (
                            <em className="why">
                                {" "}
                                - {reasonLabel(node.reason)}
                            </em>
                        )}
                    </button>
                    {node.kind === "list" && (
                        <span className="count">
                            {node.count?.toLocaleString() ?? ""}
                        </span>
                    )}
                </div>
                {open &&
                    !inert &&
                    childrenOf(node).map((c) => renderNode(c, depth + 1))}
                {more > 0 && node.kind === "list" && (
                    <button
                        type="button"
                        className="node more"
                        style={{ "--depth": depth + 1 } as React.CSSProperties}
                        onClick={() => select(node)}
                    >
                        <span />
                        <span className="label">
                            {l10n.t("{0} more", more.toLocaleString())}
                        </span>
                    </button>
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
        <Shell>
            <nav className="rail" aria-label={l10n.t("Lists")}>
                {tops.map((top) => renderNode(top, 0))}
            </nav>
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
                        onMore={() => void load(list, filter, rows.length)}
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
                        draft={draft}
                        kinds={kindChoices(kinds, draft.kinds)}
                        origins={originChoices(layers)}
                        layers={layers}
                        onChange={setDraft}
                        onSearch={() => setFilter(draft)}
                        onReset={() => {
                            setDraft(NO_FILTER);
                            setFilter(NO_FILTER);
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
            columns={[
                { key: "name", label: l10n.t("Name"), cell: (r) => r.name },
                {
                    key: "type",
                    label: l10n.t("Type"),
                    cell: (r) => r.type ?? "",
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
            ]}
            rows={rows}
            rowKey={(r) => r.id}
            selected={selectedId}
            inert={(r) =>
                r.page
                    ? undefined
                    : r.origin === "missing"
                      ? l10n.t("Nothing defines this name")
                      : l10n.t("Not an entry of its own")
            }
            onSelect={onSelect}
            onOpen={(r) => onAct("open", r)}
            onKey={(key, r) => {
                if (key !== "F12" || !r.page) {
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
 * An opened entry whose type has no preview adapter yet: the preview's header and actions, and for a
 * game object its encyclopedia card - the same component the encyclopedia panel draws. Its relations
 * are its children in the rail.
 */
function EntryPage({
    node,
    result,
    layers,
    onOpenBase,
    onAct,
}: {
    node: EntryNode;
    result: GetSymbolNodeResult | null;
    layers: GetSymbolOutlineResult["layers"];
    onOpenBase: (base: string) => void;
    onAct: (action: string) => void;
}): React.JSX.Element {
    const row = result?.entry ?? node.row;
    return (
        <section className="entry">
            <div className="entry-main">
                <EntryHead
                    tile={{
                        kind: "identity",
                        x: 0,
                        y: 0,
                        width: 1,
                        height: 1,
                        entry: row,
                        location: result?.location,
                        variantOf: result?.variantOf,
                    }}
                    layers={layers}
                    handlers={{
                        onOpenBase,
                        onOpenLocation: (location) =>
                            vscode.postMessage({
                                type: "openLocation",
                                uri: location.uri,
                                line: location.line,
                            }),
                    }}
                />
                {row.type === GAME_OBJECT_TYPE && (
                    <CardSection objectId={row.id} />
                )}
            </div>
            <Actions
                actions={result?.actions}
                onAct={onAct}
                width={PREVIEW_ACTIONS}
            />
        </section>
    );
}

/**
 * An open tab's content: the preview the server's adapter lists, or, for an entry whose type has
 * no adapter yet, its page as before.
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
        return (
            <EntryPage
                node={node}
                result={result}
                layers={layers}
                onOpenBase={openBase}
                onAct={onAct}
            />
        );
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
    grid-template-columns: minmax(200px, 280px) minmax(0, 1fr);
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

    /* The rail: the editor's own tree, one row per node, indented by depth. */
    .rail {
        overflow: auto;
        border-right: var(--space-1) solid var(--colour-line);
        padding: var(--space-6) 0;
        background: var(--colour-sidebar);
    }
    .node {
        display: grid;
        grid-template-columns: var(--icon-size-16) minmax(0, 1fr) auto;
        align-items: center;
        gap: var(--space-4);
        box-sizing: border-box;
        width: 100%;
        height: 22px;
        padding: 0 var(--space-8) 0
            calc(var(--space-8) + var(--depth, 0) * var(--space-16));
        border: 0;
        background: none;
        text-align: left;
        content-visibility: auto;
        contain-intrinsic-size: 22px;
    }
    .node:hover {
        background: var(--colour-hover);
    }
    .node.selected {
        background: var(--colour-selected);
        color: var(--colour-selected-ink);
    }
    .node.more {
        cursor: pointer;
        color: var(--colour-link);
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
    .node .twisty:disabled {
        cursor: default;
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
    .node .label:disabled {
        color: var(--colour-faint);
        cursor: default;
    }
    .node .count {
        color: var(--colour-muted);
        font-size: var(--font-size-11);
        font-variant-numeric: tabular-nums;
    }
    .node .why {
        color: var(--colour-faint);
        font-style: normal;
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

    .search {
        border-top: var(--space-1) solid var(--colour-line);
        background: var(--colour-sidebar);
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
    .preview {
        display: grid;
        grid-template-rows: auto minmax(0, 1fr);
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
        grid-template-rows: minmax(0, 1fr);
    }
    .preview-grid {
        display: grid;
        min-width: 0;
        min-height: 0;
    }
    .preview-actions .tile-grid {
        justify-content: start;
    }

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
    .code {
        margin: 0;
        font-family: var(--vscode-editor-font-family, monospace);
        font-size: var(--font-size-11);
        white-space: pre;
    }

    /* An entry without a preview adapter: its header and card, the action tiles beside them. */
    .entry {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        align-items: start;
        gap: var(--space-16);
        padding: var(--space-16);
        overflow: auto;
    }
    .entry-main {
        display: grid;
        gap: var(--space-16);
        min-width: 0;
    }
    .card {
        display: inline-grid;
        gap: var(--space-8);
    }

    @media (max-width: 700px) {
        grid-template-columns: minmax(0, 1fr);
        grid-template-rows: 40vh minmax(0, 1fr);
        .rail {
            border-right: 0;
            border-bottom: var(--space-1) solid var(--colour-line);
        }
    }
`;

createRoot(document.getElementById("root")!).render(<App />);
