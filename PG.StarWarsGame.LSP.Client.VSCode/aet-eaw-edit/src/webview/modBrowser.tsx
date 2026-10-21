// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Mod Browser (#270): everything a project indexes, by what it is. Modelled on Eclipse Scout's
// outline - a rail on the left that is the recursive tree of lists and entries, and the open list or
// entry on the right. A list's children are its loaded rows, an entry's are its relations, and a
// relation is a list again; the path to the open node is the breadcrumb. A list carries its search
// form at the bottom; an entry shows its header and, for a game object, its encyclopedia card.
//
// A click on a row opens it, a double click goes to its definition; Enter and F12 do the same from
// the keyboard, and the twisty only expands. What else an entry offers - the code lens set - is on
// its context menu, on the row's hover icons and on its page.
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
    GetSymbolNodeResult,
    GetSymbolOutlineResult,
    GetSymbolPageParams,
    GetSymbolPageResult,
    SymbolKindCount,
    SymbolOutline,
    SymbolParent,
    SymbolRelation,
    SymbolRow,
} from "../protocol/modBrowser";
import { pickShipName } from "../shipNamePick";
import { EncyclopediaCard } from "./encyclopediaCard";
import { factionModes, factionSlotOf } from "./encyclopediaModes";
import { menuAction, RowMenuFrame } from "./loc/LocCells";
import {
    ActionChoice,
    actionChoices,
    KindChoice,
    kindChoices,
    locationLabel,
    originChoices,
    originLabel,
    pageLabel,
    reasonLabel,
    relationLabel,
} from "./modBrowserModel";
import { Button } from "./shared/Button";
import { IconButton } from "./shared/Button";
import { dockChromeCss, dockHeaderCss } from "./shared/dockChrome";
import { Field } from "./shared/Field";
import { Icon, IconName } from "./shared/Icon";
import { ModeSelector } from "./shared/ModeSelector";

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

const vscode = acquireVsCodeApi();

const PAGE_SIZE = 200;
const GAME_OBJECT_TYPE = "GameObjectType";
/** The card at twice the game's size: legible, and still small enough to sit beside the header. */
const CARD_ZOOM = 2;
/** How long the pointer rests on a row before its actions are asked for. */
const HOVER_DELAY_MS = 250;

/** The icon an action shows on a row. */
const ACTION_ICONS: Record<string, IconName> = {
    definition: "definition",
    previewUnit: "preview3d",
    previewModel: "preview3d",
    encyclopedia: "encyclopediaCard",
    effectiveObject: "effectiveObject",
};

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
    const [current, setCurrent] = useState<RailNode | null>(null);
    const [expanded, setExpanded] = useState<Set<string>>(new Set());
    /**
     * Every list's loaded rows, the table's among them. The rail draws a list's children from here,
     * so a list keeps its rows - searched and paged as the table left them - after the table moves
     * on to one of its entries or their relations.
     */
    const [loaded, setLoaded] = useState<Record<string, Loaded>>({});
    const [nodes, setNodes] = useState<Record<string, GetSymbolNodeResult>>({});
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
                setExpanded(new Set());
                setLoaded({});
                setNodes({});
            }
            // The index changed: what was loaded may be stale, so it is asked again.
            if (msg.type === "refresh") {
                setLoaded({});
                setNodes({});
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
                setCurrent(
                    (open) =>
                        open ?? {
                            kind: "list",
                            key: first.page,
                            page: first.page,
                            label: pageLabel(first.page),
                            count: first.count,
                            trail: [],
                        },
                );
                setExpanded((open) => new Set(open).add(first.page));
            }
        });
    }, [outline, refreshed]);

    const list = current?.kind === "list" ? current : null;

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
        // Back to the list the table holds keeps its search; another list starts unfiltered.
        if (node.kind === "list" && node.key !== tableKey) {
            setDraft(NO_FILTER);
            setFilter(NO_FILTER);
        } else if (node.kind === "entry") {
            void fetchNode(node);
        }
        expand(node, true);
        setCurrent(node);
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
            select(node);
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

    if (outline === null || summary === null || current === null) {
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
                    className={`node${current.key === node.key ? " selected" : ""}`}
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
                            node.kind === "entry" &&
                            void act("definition", node)
                        }
                        onKeyDown={(e) => {
                            if (node.kind === "entry" && e.key === "F12") {
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

    const crumbs = [...current.trail, current];

    return (
        <Shell>
            <nav className="rail" aria-label={l10n.t("Lists")}>
                {tops.map((top) => renderNode(top, 0))}
            </nav>
            <main className="page">
                <header className="crumbs">
                    {crumbs.map((node, i) => (
                        <span key={node.key} className="crumb-step">
                            {i > 0 && <Separator />}
                            {i < crumbs.length - 1 ? (
                                <button
                                    type="button"
                                    className="crumb"
                                    onClick={() => select(node)}
                                >
                                    {nodeName(node)}
                                </button>
                            ) : (
                                <b>{nodeName(node)}</b>
                            )}
                        </span>
                    ))}
                    {list && (
                        <span className="end">
                            {l10n.t(
                                "{0} of {1}",
                                rows.length.toLocaleString(),
                                total.toLocaleString(),
                            )}
                        </span>
                    )}
                </header>
                {current.kind === "entry" ? (
                    <EntryPage
                        node={current}
                        result={nodes[current.key] ?? null}
                        layers={layers}
                        onOpenBase={(base) =>
                            select({
                                kind: "entry",
                                key: `${current.key}\n${base}`,
                                page: current.page,
                                row: {
                                    id: base,
                                    name: base,
                                    kinds: [],
                                    origin: "",
                                    overrides: [],
                                    page: current.page,
                                },
                                // Reached from the variant, so the way back is the variant.
                                trail: [...current.trail, current],
                            })
                        }
                        onAct={(action) => void act(action, current)}
                    />
                ) : (
                    <ListPage
                        node={current}
                        rows={rows}
                        total={total}
                        loading={loading}
                        layers={layers}
                        nodeOf={(row) => nodes[entryOf(current, row).key]}
                        onHover={(row) => void fetchNode(entryOf(current, row))}
                        onAct={(action, row) =>
                            void act(action, entryOf(current, row))
                        }
                        onMenu={(x, y, row, index) =>
                            openMenu(x, y, entryOf(current, row), index)
                        }
                        onMore={() => void load(current, filter, rows.length)}
                    />
                )}
                {current.kind === "list" && (
                    <SearchForm
                        open={formOpen}
                        onToggle={() => setFormOpen((o) => !o)}
                        title={
                            current.parent
                                ? l10n.t(
                                      "{0} of {1}",
                                      current.label,
                                      nodeName(
                                          current.trail[
                                              current.trail.length - 1
                                          ],
                                      ),
                                  )
                                : current.label
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
                    choices={actionChoices(
                        !!menu.node.row.page,
                        nodes[menu.node.key]?.actions,
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
    const first = useRef<HTMLButtonElement>(null);
    useEffect(() => first.current?.focus(), []);
    // Up and down walk the enabled items, as in the editor's own menus.
    const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
        if (e.key !== "ArrowDown" && e.key !== "ArrowUp") {
            return;
        }
        e.preventDefault();
        const items = [
            ...e.currentTarget.querySelectorAll<HTMLButtonElement>(
                "button:not(:disabled)",
            ),
        ];
        const at = items.indexOf(document.activeElement as HTMLButtonElement);
        const step = e.key === "ArrowDown" ? 1 : -1;
        items[(at + step + items.length) % items.length]?.focus();
    };
    let firstEnabled = true;
    return (
        <RowMenuFrame
            x={menu.x}
            y={menu.y}
            anchorIndex={menu.anchor}
            onClose={onClose}
        >
            <div className="menu-items" onKeyDown={onKeyDown}>
                {choices.map((group, g) => (
                    <div key={g} className="menu-group">
                        {g > 0 && <div className="sep" />}
                        {group.map((c) => {
                            const focus = !c.disabled && firstEnabled;
                            if (focus) {
                                firstEnabled = false;
                            }
                            return (
                                <button
                                    key={c.action}
                                    ref={focus ? first : undefined}
                                    type="button"
                                    role="menuitem"
                                    disabled={c.disabled}
                                    onClick={menuAction(
                                        () => onAct(c.action),
                                        onClose,
                                    )}
                                >
                                    {c.label}
                                    <span className="keys">
                                        {c.disabled ? c.reason : c.keys}
                                    </span>
                                </button>
                            );
                        })}
                    </div>
                ))}
            </div>
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

function ListPage({
    rows,
    total,
    loading,
    layers,
    nodeOf,
    onHover,
    onAct,
    onMenu,
    onMore,
}: {
    node: ListNode;
    rows: SymbolRow[];
    total: number;
    loading: boolean;
    layers: GetSymbolOutlineResult["layers"];
    /** The row's entry as the server opened it, once it has. */
    nodeOf: (row: SymbolRow) => GetSymbolNodeResult | undefined;
    onHover: (row: SymbolRow) => void;
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

    const onRowKey = (
        e: React.KeyboardEvent<HTMLTableRowElement>,
        row: SymbolRow,
        index: number,
    ) => {
        const step =
            e.key === "ArrowDown"
                ? e.currentTarget.nextElementSibling
                : e.key === "ArrowUp"
                  ? e.currentTarget.previousElementSibling
                  : undefined;
        if (step !== undefined) {
            e.preventDefault();
            (step as HTMLElement | null)?.focus();
        } else if (e.key === "Enter" || e.key === "F12") {
            e.preventDefault();
            onAct(e.key === "Enter" ? "open" : "definition", row);
        } else if (e.key === "ContextMenu" || (e.shiftKey && e.key === "F10")) {
            e.preventDefault();
            const box = e.currentTarget.getBoundingClientRect();
            onMenu(box.left + 16, box.bottom, row, index);
        }
    };

    return (
        <>
            <div className="grid">
                <table>
                    <thead>
                        <tr>
                            <th>{l10n.t("Name")}</th>
                            <th>{l10n.t("Type")}</th>
                            <th>{l10n.t("Kinds")}</th>
                            <th>{l10n.t("Origin")}</th>
                            <th aria-label={l10n.t("Actions")} />
                        </tr>
                    </thead>
                    <tbody>
                        {rows.map((row, index) => (
                            <tr
                                key={row.id}
                                data-row-index={index}
                                tabIndex={0}
                                className={row.page ? undefined : "inert"}
                                title={
                                    row.page
                                        ? undefined
                                        : row.origin === "missing"
                                          ? l10n.t("Nothing defines this name")
                                          : l10n.t("Not an entry of its own")
                                }
                                onClick={() => onAct("open", row)}
                                onDoubleClick={() => onAct("definition", row)}
                                onKeyDown={(e) => onRowKey(e, row, index)}
                                onMouseEnter={() => rest(row)}
                                onFocus={() => rest(row)}
                                onContextMenu={(e) => {
                                    e.preventDefault();
                                    onMenu(e.clientX, e.clientY, row, index);
                                }}
                            >
                                <td>{row.name}</td>
                                <td className="dim">{row.type ?? ""}</td>
                                <td className="dim">{row.kinds.join(", ")}</td>
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
                                                    row.overrides[0],
                                                    null,
                                                    layers,
                                                ),
                                            )}
                                        </span>
                                    )}
                                </td>
                                <td className="acts">
                                    <RowActions
                                        row={row}
                                        result={nodeOf(row)}
                                        onAct={(action) => onAct(action, row)}
                                        onMore={(x, y) =>
                                            onMenu(x, y, row, index)
                                        }
                                    />
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
                {rows.length === 0 && !loading && (
                    <p className="empty">{l10n.t("Nothing matches")}</p>
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
                    onClick={onMore}
                >
                    {l10n.t("Load more")}
                </Button>
            </footer>
        </>
    );
}

/**
 * A row's hover icons: go to definition and what the entry offers, disabled ones saying why, then
 * the whole menu. Drawn once the server has opened the entry, which resting on the row asks for.
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
        <span className="row-actions">
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
        </span>
    );
}

/**
 * An opened entry: what it is and where the game takes it from, and for a game object its
 * encyclopedia card - the same component the encyclopedia panel draws. Its relations are its
 * children in the rail, not a column here.
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
    const location = result?.location;
    const definedIn = originLabel(row.origin, row.originName, layers);
    const [[, definition], offered = []] = actionChoices(true, result?.actions);
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
            {row.origin && (
                <p className="dim">
                    {location
                        ? l10n.t(
                              "Defined in - {0} - {1}",
                              definedIn,
                              locationLabel(location),
                          )
                        : l10n.t("Defined in - {0}", definedIn)}
                </p>
            )}
            {row.overrides.map((o) => (
                <p key={o} className="dim">
                    {l10n.t("Overrides - {0}", originLabel(o, null, layers))}
                </p>
            ))}
            {result?.variantOf && (
                <p className="dim">
                    {l10n.t("Variant of - ")}
                    <button
                        type="button"
                        className="link"
                        onClick={() => onOpenBase(result.variantOf!)}
                    >
                        {result.variantOf}
                    </button>
                </p>
            )}
            <p className="actions">
                {[
                    definition,
                    ...offered.filter((c) => c.action !== "copyName"),
                ].map((c) => (
                    <Button
                        key={c.action}
                        compact
                        disabled={c.disabled}
                        disabledReason={c.reason ?? ""}
                        onClick={() => onAct(c.action)}
                    >
                        <Icon
                            name={ACTION_ICONS[c.action] ?? "open"}
                            size={14}
                        />
                        {c.label}
                    </Button>
                ))}
            </p>
            {row.type === GAME_OBJECT_TYPE && <CardSection objectId={row.id} />}
        </section>
    );
}

/** The object's encyclopedia card with its faction switch, asked for when the entry opens. */
function CardSection({ objectId }: { objectId: string }): React.JSX.Element {
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

    return (
        <div className="card">
            <EncyclopediaCard
                entry={
                    shipName === null ? card : { ...card, unitClass: shipName }
                }
                zoom={CARD_ZOOM}
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
                                <span>{l10n.t("Kind")}</span>
                                <span className="chips">
                                    {kinds.map((k) => (
                                        <button
                                            key={k.kind}
                                            type="button"
                                            className={`chip${draft.kinds.includes(k.kind) ? " on" : ""}`}
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

// VS Code's own colours, so the panel follows the theme, on the chrome the other panels share. The
// rail and the page are a fixed grid; the table and the rail scroll in place.
const Shell = styled.div`
    ${dockChromeCss}
    ${dockHeaderCss}

    display: grid;
    grid-template-columns: minmax(200px, 280px) minmax(0, 1fr);
    height: 100vh;
    color: var(--vscode-foreground);
    background: var(--vscode-editor-background);
    font: var(--vscode-font-size) / 1.4 var(--vscode-font-family);

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
    .chip:disabled {
        background: transparent;
        color: var(--vscode-disabledForeground);
        cursor: default;
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
        background: var(--vscode-list-hoverBackground);
    }
    .node.selected {
        background: var(--vscode-list-activeSelectionBackground);
        color: var(--vscode-list-activeSelectionForeground);
    }
    .node.more {
        cursor: pointer;
        color: var(--vscode-textLink-foreground);
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
        color: var(--vscode-disabledForeground);
        cursor: default;
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
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-6);
        padding: var(--space-8) var(--space-16);
        border-bottom: 1px solid var(--vscode-panel-border);
    }
    .crumb-step {
        display: inline-flex;
        align-items: center;
        gap: var(--space-6);
    }
    .crumbs .crumb,
    .link {
        border: 0;
        background: none;
        padding: 0;
        cursor: pointer;
        color: var(--vscode-textLink-foreground);
    }
    .crumbs .sep {
        display: inline-flex;
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
    tbody tr:focus-visible {
        outline: 1px solid var(--vscode-focusBorder);
        outline-offset: -1px;
    }
    /* The hover icons overlay the row's end, as the editor's own lists do: the cell takes no width,
       so the table is as wide with them as without. */
    td.acts {
        position: sticky;
        right: 0;
        width: 0;
        padding: 0;
    }
    .row-actions {
        position: absolute;
        right: var(--space-8);
        top: 50%;
        transform: translateY(-50%);
        display: inline-flex;
        visibility: hidden;
        background: var(--vscode-editor-background);
        border-radius: var(--radius-6);
    }
    tbody tr:hover .row-actions,
    tbody tr:focus-within .row-actions {
        visibility: visible;
    }
    .entry .actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-6);
        margin-top: var(--space-12);
    }
    .entry .actions .btn {
        display: inline-flex;
        align-items: center;
        gap: var(--space-4);
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
    .entry p {
        margin: var(--space-4) 0;
    }
    .entry .line .chip:first-child {
        margin-left: 0;
    }
    .entry .card {
        display: inline-grid;
        gap: var(--space-8);
        margin-top: var(--space-16);
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
