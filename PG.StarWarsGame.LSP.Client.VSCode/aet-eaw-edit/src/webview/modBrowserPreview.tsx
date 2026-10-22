// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// An entry's preview in the Mod Browser (#270): the tiles the server's adapter lists, laid out on a
// grid after Scout's tile grid, with the entry's actions in a column of their own. Every tile is a
// shared piece the other panels draw too: the entry's header with tags and key/value lines, a Panel
// per list, problems as the problems band's rows, the definition with a ModeSelector, and the
// actions as dock tiles. The server decides which tiles an entry gets and their cells; this file
// only draws them.

import * as l10n from "@vscode/l10n";
import {
    Fragment,
    ReactNode,
    useEffect,
    useLayoutEffect,
    useMemo,
    useRef,
    useState,
} from "react";

import type {
    GetSymbolOutlineResult,
    PreviewEntry,
    PreviewTile,
    SymbolAction,
    SymbolLocation,
    SymbolRow,
} from "../protocol/modBrowser";
import type { GetModelDetailResult } from "../protocol/modelPreview";
import {
    actionChoices,
    locationLabel,
    originLabel,
    PREVIEW_ACTIONS,
    PREVIEW_CELL,
    previewLayout,
    problemMessage,
    relationLabel,
    factLabel,
    fileSize,
} from "./modBrowserModel";
import { treeItemsFromDetail } from "./preview/modelTreeItems";
import {
    buildTree,
    defaultCollapsed,
    visibleTreeRows,
} from "./preview/previewTree";
import { DockTile, DockTileGrid } from "./shared/DockTile";
import { Icon, IconName } from "./shared/Icon";
import { Panel } from "./shared/Panel";
import { problemLook, problemTag } from "./shared/problemLook";
import { TreeRow } from "./shared/TreeRow";

/** What the preview asks of the panel around it. */
export interface PreviewHandlers {
    /** Opens a related entry in a tab. */
    onOpenRow: (row: SymbolRow) => void;
    /** Opens the variant base in a tab. */
    onOpenBase: (base: string) => void;
    onOpenLocation: (location: SymbolLocation) => void;
    onAct: (action: string) => void;
    /** The encyclopedia card, drawn to fit a width. */
    card: (objectId: string, width: number) => ReactNode;
    /** A model's detail - its bones, meshes and proxies - from its project's server. */
    modelDetail: (model: string) => Promise<GetModelDetailResult | null>;
}

/** The icon each action shows. */
export const ACTION_ICONS: Record<string, IconName> = {
    definition: "definition",
    previewUnit: "preview3d",
    previewModel: "preview3d",
    encyclopedia: "encyclopediaCard",
    effectiveObject: "effectiveObject",
    copyName: "copy",
    reveal: "reveal",
};

/**
 * What a panel's own padding and a scrollbar take off its width: content sized to fit the panel,
 * as the encyclopedia card is, is sized to what is left.
 */
const PANEL_INSET = 40;

export function PreviewPage({
    tiles,
    columns,
    rows,
    actions,
    layers,
    handlers,
}: {
    tiles: PreviewTile[];
    /** The adapter's grid. */
    columns: number;
    rows: number;
    /** The entry's actions, once the server has opened it. */
    actions: SymbolAction[] | undefined;
    layers: GetSymbolOutlineResult["layers"];
    handlers: PreviewHandlers;
}): React.JSX.Element {
    const page = useRef<HTMLElement>(null);
    const [width, setWidth] = useState(0);
    useLayoutEffect(() => {
        const element = page.current;
        if (!element) {
            return;
        }
        setWidth(element.clientWidth);
        const watch = new ResizeObserver(() => setWidth(element.clientWidth));
        watch.observe(element);
        return () => watch.disconnect();
    }, []);
    const layout = previewLayout(width, columns);
    const across = (cells: number) =>
        cells * layout.cellWidth + (cells - 1) * PREVIEW_CELL.gap;

    return (
        <section
            ref={page}
            className={`preview${layout.actionsBeside ? " beside" : ""}`}
        >
            <div
                className="preview-grid"
                style={{
                    // The adapter's fixed grid, stretched over the tab from each cell's least size.
                    gridTemplateColumns: `repeat(${columns}, minmax(${PREVIEW_CELL.width}px, 1fr))`,
                    gridTemplateRows: `repeat(${rows}, minmax(${PREVIEW_CELL.height}px, 1fr))`,
                    gap: `${PREVIEW_CELL.gap}px`,
                }}
            >
                {tiles.map((tile, i) => (
                    <Tile
                        key={`${tile.kind}-${tile.relation ?? ""}-${i}`}
                        tile={tile}
                        layers={layers}
                        handlers={handlers}
                        width={across(tile.width)}
                        // Its own cells and no others: an absent neighbour leaves its cells empty.
                        place={{
                            gridColumn: `${tile.x + 1} / span ${tile.width}`,
                            gridRow: `${tile.y + 1} / span ${tile.height}`,
                        }}
                    />
                ))}
            </div>
            <Actions
                actions={actions}
                onAct={handlers.onAct}
                width={layout.actionsBeside ? PREVIEW_ACTIONS : undefined}
            />
        </section>
    );
}

function Tile({
    tile,
    layers,
    handlers,
    width,
    place,
}: {
    tile: PreviewTile;
    layers: GetSymbolOutlineResult["layers"];
    handlers: PreviewHandlers;
    /** The tile's width in pixels, for what has to fit it. */
    width: number;
    place: React.CSSProperties;
}): React.JSX.Element | null {
    switch (tile.kind) {
        case "identity":
            return (
                <EntryHead
                    tile={tile}
                    layers={layers}
                    handlers={handlers}
                    place={place}
                />
            );
        case "problems":
            return (
                <Panel
                    title={l10n.t("Problems")}
                    count={tile.count ?? undefined}
                    flush
                    className="prose-problems"
                    style={place}
                >
                    <Problems
                        entries={tile.entries ?? []}
                        onOpen={handlers.onOpenLocation}
                    />
                </Panel>
            );
        case "encyclopedia":
            return (
                <Panel title={l10n.t("Encyclopedia")} style={place}>
                    {tile.objectId &&
                        handlers.card(tile.objectId, width - PANEL_INSET)}
                </Panel>
            );
        case "hierarchy":
            return tile.objectId ? (
                <ModelTreePanel
                    model={tile.objectId}
                    place={place}
                    load={handlers.modelDetail}
                />
            ) : null;
        case "entries":
            // Every entry of the relation: the tile scrolls, so none is held back.
            return (
                <Panel
                    title={relationLabel(tile.relation ?? "")}
                    count={tile.count?.toLocaleString()}
                    flush
                    style={place}
                >
                    <EntryList
                        entries={tile.entries ?? []}
                        onOpen={(row) => handlers.onOpenRow(row)}
                    />
                </Panel>
            );
        default:
            return null;
    }
}

/**
 * The entry's header, the one bold thing on the page: its name, tags for what classifies it, and
 * where the definition the game uses comes from.
 */
function EntryHead({
    tile,
    layers,
    handlers,
    place,
}: {
    tile: PreviewTile;
    layers: GetSymbolOutlineResult["layers"];
    handlers: Pick<PreviewHandlers, "onOpenBase" | "onOpenLocation">;
    /** Its cells, on the preview grid. */
    place: React.CSSProperties;
}): React.JSX.Element | null {
    const entry = tile.entry;
    if (!entry) {
        return null;
    }
    return (
        <section className="panel entry-head" style={place}>
            <div className="panel-body">
                <h2 className="entry-title">{entry.name}</h2>
                <div className="tag-row">
                    {entry.type && <span className="tag">{entry.type}</span>}
                    {entry.kinds.map((k) => (
                        <span key={k} className="tag">
                            {k}
                        </span>
                    ))}
                    <span className="tag outline">
                        {originLabel(entry.origin, entry.originName, layers)}
                    </span>
                </div>
                <dl className="kv-rows">
                    {tile.location && (
                        <>
                            <dt>{l10n.t("Defined in")}</dt>
                            <dd>
                                <button
                                    type="button"
                                    className="link"
                                    onClick={() =>
                                        handlers.onOpenLocation(tile.location!)
                                    }
                                >
                                    {locationLabel(tile.location)}
                                </button>
                            </dd>
                        </>
                    )}
                    {entry.overrides.length > 0 && (
                        <>
                            <dt>{l10n.t("Overrides")}</dt>
                            <dd>
                                {entry.overrides
                                    .map((o) => originLabel(o, null, layers))
                                    .join(", ")}
                            </dd>
                        </>
                    )}
                    {tile.variantOf && (
                        <>
                            <dt>{l10n.t("Variant of")}</dt>
                            <dd>
                                <button
                                    type="button"
                                    className="link"
                                    onClick={() =>
                                        handlers.onOpenBase(tile.variantOf!)
                                    }
                                >
                                    {tile.variantOf}
                                </button>
                            </dd>
                        </>
                    )}
                    {/* A model's counts: what it holds, at a glance beside where it comes from. */}
                    {(tile.facts ?? []).map((f) => (
                        <Fragment key={f.key}>
                            <dt>{factLabel(f.key)}</dt>
                            <dd className="number">
                                {f.value.toLocaleString()}
                            </dd>
                        </Fragment>
                    ))}
                    {/* The file on disk, for an asset or a script that has one. */}
                    {tile.file && (
                        <>
                            <dt>{l10n.t("Size")}</dt>
                            <dd className="number">
                                {fileSize(tile.file.size)}
                            </dd>
                            {typeof tile.file.lines === "number" && (
                                <>
                                    <dt>{l10n.t("Lines")}</dt>
                                    <dd className="number">
                                        {tile.file.lines.toLocaleString()}
                                    </dd>
                                </>
                            )}
                            {/* The day, which fits the header's column; the time is on its tooltip. */}
                            <dt>{l10n.t("Modified")}</dt>
                            <dd
                                title={new Date(
                                    tile.file.modified,
                                ).toLocaleString()}
                            >
                                {new Date(
                                    tile.file.modified,
                                ).toLocaleDateString()}
                            </dd>
                            <dt>{l10n.t("Created")}</dt>
                            <dd
                                title={new Date(
                                    tile.file.created,
                                ).toLocaleString()}
                            >
                                {new Date(
                                    tile.file.created,
                                ).toLocaleDateString()}
                            </dd>
                        </>
                    )}
                </dl>
            </div>
        </section>
    );
}

/**
 * The problems inside the definition, drawn as the problems band draws them: the severity's mark,
 * the message as prose, the diagnostic id last and dim. A click goes to the line.
 */
function Problems({
    entries,
    onOpen,
}: {
    entries: PreviewEntry[];
    onOpen: (location: SymbolLocation) => void;
}): React.JSX.Element {
    return (
        <div className="problem-list">
            {entries.map((e, i) => {
                const look = problemLook(e.severity ?? "warning");
                // A problem the server found in an asset has no diagnostic id to show.
                const tag = e.reason ? null : problemTag(e.detail);
                return (
                    <button
                        key={`${e.name}-${i}`}
                        type="button"
                        className="problem-row"
                        disabled={!e.location}
                        onClick={() => e.location && onOpen(e.location)}
                    >
                        <span
                            className={`codicon codicon-${look.icon} sev-${look.tone}`}
                            aria-hidden="true"
                        />
                        <span className="problem-msg">{problemMessage(e)}</span>
                        {tag && (
                            <span className="problem-id" title={tag.title}>
                                {tag.text}
                            </span>
                        )}
                    </button>
                );
            })}
        </div>
    );
}

/**
 * A model's bones, meshes and effects as the model preview's tree draws them - the same rows, the
 * same nesting and the same subtrees folded at first - read from the model by its project's server.
 * To look, not to act: the eyes and the inspector are the model preview's, a click away.
 */
function ModelTreePanel({
    model,
    place,
    load,
}: {
    model: string;
    place: React.CSSProperties;
    load: (model: string) => Promise<GetModelDetailResult | null>;
}): React.JSX.Element {
    const [answer, setAnswer] = useState<GetModelDetailResult | null>(null);
    const [collapsed, setCollapsed] = useState<Set<string> | null>(null);
    useEffect(() => {
        let live = true;
        setAnswer(null);
        setCollapsed(null);
        void load(model).then((result) => {
            if (live) {
                setAnswer(result ?? { detail: null, error: null });
            }
        });
        return () => {
            live = false;
        };
    }, [model, load]);

    const roots = useMemo(
        () =>
            answer?.detail ? buildTree(treeItemsFromDetail(answer.detail)) : [],
        [answer],
    );
    const folded = collapsed ?? defaultCollapsed(roots);
    const rows = visibleTreeRows(roots, folded);
    const toggle = (id: string) => {
        const next = new Set(folded);
        if (!next.delete(id)) {
            next.add(id);
        }
        setCollapsed(next);
    };

    return (
        <Panel
            title={l10n.t("Model tree")}
            count={
                answer?.detail
                    ? answer.detail.bones.length.toLocaleString()
                    : undefined
            }
            flush
            style={place}
        >
            {answer === null ? (
                <p className="empty">{l10n.t("Loading...")}</p>
            ) : !answer.detail ? (
                <p className="empty">
                    {answer.error ?? l10n.t("The model could not be read")}
                </p>
            ) : (
                <ul className="tree-list">
                    {rows.map(({ node, expandable, expanded }) => (
                        <TreeRow
                            key={node.id}
                            id={node.id}
                            kind={node.kind}
                            name={node.name}
                            depth={node.depth}
                            expandable={expandable}
                            expanded={expanded}
                            onToggle={() => toggle(node.id)}
                        />
                    ))}
                </ul>
            )}
        </Panel>
    );
}

/**
 * Every relation in a preview: a name that opens what it names, and a quieter line saying what it
 * is or where. An entry with nothing to open is greyed, as in the table.
 */
function EntryList({
    entries,
    onOpen,
}: {
    entries: PreviewEntry[];
    onOpen: (row: SymbolRow) => void;
}): React.JSX.Element {
    if (entries.length === 0) {
        return <p className="empty">{l10n.t("None")}</p>;
    }
    return (
        <ul className="entry-list">
            {entries.map((e, i) => (
                <li key={`${e.name}-${i}`}>
                    <button
                        type="button"
                        className="entry-row"
                        disabled={!e.row?.page}
                        title={
                            e.row?.page
                                ? undefined
                                : l10n.t("Nothing defines this name")
                        }
                        onClick={() => e.row && onOpen(e.row)}
                    >
                        <span className="entry-name">{e.name}</span>
                        {e.detail && (
                            <span className="entry-detail">{e.detail}</span>
                        )}
                    </button>
                </li>
            ))}
        </ul>
    );
}

/**
 * The entry's actions as dock tiles, in one order everywhere: go to definition first, the type's
 * own next, copying the name last. One that does not apply stays, disabled, with why. A column
 * beside the grid when there is room for one, a row above it when there is not.
 */
function Actions({
    actions,
    onAct,
    width,
}: {
    actions: SymbolAction[] | undefined;
    onAct: (action: string) => void;
    /** The column's width, beside the grid; absent when the actions run across above it. */
    width?: number;
}): React.JSX.Element {
    const [[, definition], ...groups] = actionChoices(true, actions);
    const items = [definition, ...groups.flat()];
    return (
        <nav
            className="preview-actions"
            aria-label={l10n.t("Actions")}
            style={width ? { width: `${width}px` } : undefined}
        >
            <DockTileGrid>
                {items.map((c) => (
                    <DockTile
                        key={c.action}
                        glyph={<Icon name={ACTION_ICONS[c.action] ?? "open"} />}
                        label={c.label}
                        title={c.label}
                        disabled={c.disabled ?? false}
                        disabledReason={c.reason ?? ""}
                        onClick={() => onAct(c.action)}
                    />
                ))}
            </DockTileGrid>
        </nav>
    );
}
