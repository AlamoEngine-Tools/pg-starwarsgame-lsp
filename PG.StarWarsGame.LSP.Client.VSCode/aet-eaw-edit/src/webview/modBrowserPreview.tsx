// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// An entry's preview in the Mod Browser (#270): the tiles the server's adapter lists, laid out on a
// grid after Scout's tile grid, with the entry's actions in a column of their own. One form per
// concept, whatever the entry: tags for what classifies it, key/value lines for where it comes
// from, one entry list for every relation, problems in the error colour, code for the definition.
// The server decides which tiles an entry gets and their size in cells; this file only draws them.

import * as l10n from "@vscode/l10n";
import { ReactNode, useLayoutEffect, useRef, useState } from "react";

import type {
    GetSymbolOutlineResult,
    PreviewEntry,
    PreviewTile,
    SymbolAction,
    SymbolLocation,
    SymbolRow,
} from "../protocol/modBrowser";
import {
    actionChoices,
    locationLabel,
    originLabel,
    PREVIEW_ACTIONS,
    PREVIEW_CELL,
    previewLayout,
    relationLabel,
} from "./modBrowserModel";
import { Icon, IconName } from "./shared/Icon";

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
}

/** The icon each action shows. */
export const ACTION_ICONS: Record<string, IconName> = {
    definition: "definition",
    previewUnit: "preview3d",
    previewModel: "preview3d",
    encyclopedia: "encyclopediaCard",
    effectiveObject: "effectiveObject",
    copyName: "copy",
};

/**
 * What a tile's own padding and a scrollbar take off its width: content sized to fit the tile, as
 * the encyclopedia card is, is sized to what is left.
 */
const TILE_INSET = 48;

export function PreviewPage({
    tiles,
    actions,
    layers,
    handlers,
}: {
    tiles: PreviewTile[];
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
    const layout = previewLayout(width);
    const span = (cells: number) => Math.min(cells, layout.columns);
    const across = (cells: number) =>
        span(cells) * layout.columnWidth + (span(cells) - 1) * PREVIEW_CELL.gap;

    return (
        <section
            ref={page}
            className={`preview${layout.actionsBeside ? " beside" : ""}`}
        >
            {!layout.actionsBeside && (
                <Actions actions={actions} onAct={handlers.onAct} inline />
            )}
            <div
                className="preview-grid"
                style={{
                    // At least the cell's width, then stretched across the full width.
                    gridTemplateColumns: `repeat(${layout.columns}, minmax(${PREVIEW_CELL.width}px, 1fr))`,
                    gridAutoRows: `${PREVIEW_CELL.height}px`,
                    gap: `${PREVIEW_CELL.gap}px`,
                    // A later tile may fill a hole an earlier, wider one left, so the grid flows.
                    gridAutoFlow: "row dense",
                }}
            >
                {tiles.map((tile, i) => (
                    <div
                        key={`${tile.kind}-${tile.relation ?? ""}-${i}`}
                        className={`tile tile-${tile.kind}`}
                        style={{
                            gridColumn: `span ${span(tile.width)}`,
                            gridRow: `span ${tile.height}`,
                        }}
                    >
                        <Tile
                            tile={tile}
                            layers={layers}
                            handlers={handlers}
                            width={across(tile.width)}
                        />
                    </div>
                ))}
            </div>
            {layout.actionsBeside && (
                <Actions
                    actions={actions}
                    onAct={handlers.onAct}
                    width={PREVIEW_ACTIONS}
                />
            )}
        </section>
    );
}

function Tile({
    tile,
    layers,
    handlers,
    width,
}: {
    tile: PreviewTile;
    layers: GetSymbolOutlineResult["layers"];
    handlers: PreviewHandlers;
    /** The tile's width in pixels, for what has to fit it. */
    width: number;
}): React.JSX.Element | null {
    switch (tile.kind) {
        case "identity":
            return (
                <IdentityTile tile={tile} layers={layers} handlers={handlers} />
            );
        case "problems":
            return (
                <>
                    <TileHead title={l10n.t("Problems")} count={tile.count} />
                    <div className="tile-body">
                        <EntryList
                            entries={tile.entries ?? []}
                            error
                            onOpen={(e) =>
                                e.location &&
                                handlers.onOpenLocation(e.location)
                            }
                        />
                    </div>
                </>
            );
        case "encyclopedia":
            return (
                <>
                    <TileHead title={l10n.t("Encyclopedia")} />
                    <div className="tile-body">
                        {tile.objectId &&
                            handlers.card(tile.objectId, width - TILE_INSET)}
                    </div>
                </>
            );
        case "definition":
            return <DefinitionTile tile={tile} />;
        case "entries": {
            const entries = tile.entries ?? [];
            const more = (tile.count ?? entries.length) - entries.length;
            return (
                <>
                    <TileHead
                        title={relationLabel(tile.relation ?? "")}
                        count={tile.count}
                    />
                    <div className="tile-body">
                        <EntryList
                            entries={entries}
                            onOpen={(e) => e.row && handlers.onOpenRow(e.row)}
                        />
                    </div>
                    {more > 0 && (
                        <span className="tile-foot">
                            {l10n.t("{0} more", more.toLocaleString())}
                        </span>
                    )}
                </>
            );
        }
        default:
            return null;
    }
}

function TileHead({
    title,
    count,
    children,
}: {
    title: string;
    count?: number | null;
    children?: ReactNode;
}): React.JSX.Element {
    return (
        <h3 className="tile-head">
            <span>{title}</span>
            {children}
            {count !== undefined && count !== null && (
                <span className="tile-count">{count.toLocaleString()}</span>
            )}
        </h3>
    );
}

/** Name, tags for what classifies it, and where the definition the game uses comes from. */
function IdentityTile({
    tile,
    layers,
    handlers,
}: {
    tile: PreviewTile;
    layers: GetSymbolOutlineResult["layers"];
    handlers: PreviewHandlers;
}): React.JSX.Element | null {
    const entry = tile.entry;
    if (!entry) {
        return null;
    }
    return (
        <div className="tile-body">
            <h2 className="identity-name">{entry.name}</h2>
            <div className="tags">
                {entry.type && <span className="tg">{entry.type}</span>}
                {entry.kinds.map((k) => (
                    <span key={k} className="tg">
                        {k}
                    </span>
                ))}
                <span className="tg origin">
                    {originLabel(entry.origin, entry.originName, layers)}
                </span>
            </div>
            <dl className="kv">
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
            </dl>
        </div>
    );
}

/** The definition as written in its file, with the merged effective object one switch away. */
function DefinitionTile({ tile }: { tile: PreviewTile }): React.JSX.Element {
    const [effective, setEffective] = useState(false);
    const text = (effective ? tile.effective : tile.text) ?? "";
    const lines = text.split("\n").length;
    return (
        <>
            <TileHead title={l10n.t("Definition")}>
                {tile.effective && (
                    <span className="seg" role="group">
                        <button
                            type="button"
                            className={effective ? undefined : "on"}
                            aria-pressed={!effective}
                            onClick={() => setEffective(false)}
                        >
                            {l10n.t("As written")}
                        </button>
                        <button
                            type="button"
                            className={effective ? "on" : undefined}
                            aria-pressed={effective}
                            onClick={() => setEffective(true)}
                        >
                            {l10n.t("Effective")}
                        </button>
                    </span>
                )}
            </TileHead>
            <div className="tile-body">
                <pre className="code">{text}</pre>
            </div>
            <span className="tile-foot">
                {l10n.t("{0} lines", lines.toLocaleString())}
            </span>
        </>
    );
}

/**
 * Every list in a preview: a name that opens what it names, and a quieter line saying what it is
 * or where. An entry with nothing to open is greyed, as in the table.
 */
function EntryList({
    entries,
    error,
    onOpen,
}: {
    entries: PreviewEntry[];
    error?: boolean;
    onOpen: (entry: PreviewEntry) => void;
}): React.JSX.Element {
    return (
        <ul className="lst">
            {entries.map((e, i) => {
                const opens = error ? !!e.location : !!e.row?.page;
                return (
                    <li
                        key={`${e.name}-${i}`}
                        className={`li${error ? " err" : ""}${opens ? "" : " off"}`}
                    >
                        <button
                            type="button"
                            disabled={!opens}
                            onClick={() => onOpen(e)}
                        >
                            <b>{e.name}</b>
                            {e.detail && <span>{e.detail}</span>}
                        </button>
                    </li>
                );
            })}
        </ul>
    );
}

/**
 * The entry's actions in a column of their own, in one order everywhere: go to definition first,
 * the type's own next, copying the name last. One that does not apply stays, disabled, with why.
 */
function Actions({
    actions,
    onAct,
    inline,
    width,
}: {
    actions: SymbolAction[] | undefined;
    onAct: (action: string) => void;
    /** Above the grid as one row, when the panel is too narrow for a column beside it. */
    inline?: boolean;
    /** The column's width, beside the grid. */
    width?: number;
}): React.JSX.Element {
    const [, ...groups] = actionChoices(true, actions);
    const [[, definition]] = actionChoices(true, actions);
    const items = [definition, ...groups.flat()];
    return (
        <nav
            className={`preview-actions${inline ? " inline" : ""}`}
            aria-label={l10n.t("Actions")}
            style={width ? { width: `${width}px` } : undefined}
        >
            {!inline && <h3 className="tile-head">{l10n.t("Actions")}</h3>}
            {items.map((c) => (
                <button
                    key={c.action}
                    type="button"
                    className="act"
                    disabled={c.disabled}
                    onClick={() => onAct(c.action)}
                >
                    <Icon name={ACTION_ICONS[c.action] ?? "open"} size={16} />
                    <span>
                        {c.label}
                        {c.disabled && c.reason && <small>{c.reason}</small>}
                    </span>
                </button>
            ))}
        </nav>
    );
}
