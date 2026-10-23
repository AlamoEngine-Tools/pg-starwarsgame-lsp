// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every table of rows, read as the editor's own Keyboard Shortcuts table. Its look is .data-table in
// dockChromeCss; the keys follow rovingFocus, a header click cycles tableSort. What a row IS, and
// what it can do, is the owner's: the table draws, selects and moves focus.

import * as l10n from "@vscode/l10n";
import { ReactNode, useEffect, useRef, useState } from "react";

import { Icon } from "./Icon";
import { rowStep } from "./rovingFocus";
import type { ColumnLayout } from "./tableColumns";
import { nextSort, SortState } from "./tableSort";

export interface DataColumn<R> {
    key: string;
    label: string;
    cell: (row: R) => ReactNode;
    /** A quieter column - what a row is, beside the name that says which. */
    dim?: boolean;
}

export type RowSeverity = "error" | "warning" | "info";

export interface DataTableProps<R> {
    /** What the table holds, for a screen reader. */
    label: string;
    columns: readonly DataColumn<R>[];
    rows: readonly R[];
    rowKey: (row: R) => string;
    /** The selected row's key; it is also the row the Tab key lands on. */
    selected?: string | null;
    /** Why a row does nothing, for one that does nothing. It stays, greyed, with the reason on it. */
    inert?: (row: R) => string | undefined;
    /** A row's worst finding, drawn as a bar down its leading edge. */
    severity?: (row: R) => RowSeverity | undefined;
    /** The columns a header click sorts by. The owner sorts; the table only says how. */
    sortable?: readonly string[];
    sort?: SortState | null;
    onSort?: (next: SortState | null) => void;
    /** A click, or the keys arriving on a row. Never called for an inert row. */
    onSelect?: (row: R) => void;
    /** A double click or Enter. */
    onOpen?: (row: R) => void;
    /** Any other key on a row; true when it was handled. */
    onKey?: (key: string, row: R) => boolean;
    /** The context menu: a right click, the menu key or Shift+F10, at the given point. */
    onMenu?: (x: number, y: number, row: R, index: number) => void;
    /** The pointer or focus resting on a row, before anything is pressed. */
    onRest?: (row: R) => void;
    /** A row's actions, at its end, on the hovered, focused and selected rows - as the editor's lists. */
    actions?: (row: R, index: number) => ReactNode;
    /** Said in place of the rows when there are none. */
    empty: string;
    foot?: ReactNode;
    /**
     * The reader's arrangement of the columns. Without it the columns keep the table's order and
     * size to what they hold; with widths for every column, they keep those.
     */
    layout?: ColumnLayout;
    /** Every column's width, once one is dragged by its header's edge: the rest keep the width drawn. */
    onResize?: (widths: Record<string, number>) => void;
    /** A column dragged by its header onto another, which it takes the place of. */
    onMove?: (from: string, to: string) => void;
    /** A column's filter, opened from its header; none for a column without one. */
    filter?: (column: string) => ColumnFilter | undefined;
}

/** A column's filter in its header: what it shows when opened, and whether it narrows the rows. */
export interface ColumnFilter {
    /** It holds rows back. */
    active: boolean;
    /** Why the column cannot be filtered, for one that cannot. */
    reason?: string;
    content: ReactNode;
}

/** The narrowest a column is dragged to. */
const MIN_COLUMN_WIDTH = 48;

export function DataTable<R>({
    label,
    columns,
    rows,
    rowKey,
    selected,
    inert,
    severity,
    sortable,
    sort,
    onSort,
    onSelect,
    onOpen,
    onKey,
    onMenu,
    onRest,
    actions,
    empty,
    foot,
    layout,
    onResize,
    onMove,
    filter,
}: DataTableProps<R>): React.JSX.Element {
    const body = useRef<HTMLTableSectionElement>(null);
    const head = useRef<HTMLTableRowElement>(null);
    const [openFilter, setOpenFilter] = useState<{
        column: string;
        x: number;
        y: number;
    } | null>(null);
    const shown = layout
        ? layout.order.flatMap((key) => columns.filter((c) => c.key === key))
        : [...columns];
    const widths = layout?.widths ?? {};
    const fixed = shown.every((c) => widths[c.key] !== undefined);

    // A drag by a header's edge: every column's width as drawn is taken when it starts, so the
    // others keep theirs once the table lays out at fixed widths.
    const resizeFrom = (
        column: string,
        e: React.PointerEvent<HTMLSpanElement>,
    ): void => {
        if (!onResize) {
            return;
        }
        e.preventDefault();
        e.stopPropagation();
        const cells = [
            ...(head.current?.querySelectorAll<HTMLElement>(
                "th[data-column]",
            ) ?? []),
        ];
        const start: Record<string, number> = {};
        for (const cell of cells) {
            start[cell.dataset.column ?? ""] = Math.round(cell.offsetWidth);
        }
        const x = e.clientX;
        const grip = e.currentTarget;
        grip.setPointerCapture(e.pointerId);
        const move = (ev: PointerEvent): void =>
            onResize({
                ...start,
                [column]: Math.max(
                    MIN_COLUMN_WIDTH,
                    start[column] + ev.clientX - x,
                ),
            });
        const stop = (): void => {
            grip.removeEventListener("pointermove", move);
            grip.removeEventListener("pointerup", stop);
        };
        grip.addEventListener("pointermove", move);
        grip.addEventListener("pointerup", stop);
    };
    const selectedAt = rows.findIndex((r) => rowKey(r) === selected);
    const tabStop = selectedAt === -1 ? 0 : selectedAt;

    const rowAt = (i: number): HTMLTableRowElement | null =>
        body.current?.querySelector<HTMLTableRowElement>(
            `tr[data-row-index="${i}"]`,
        ) ?? null;

    // A page is the rows the scroller shows, less one so the row moved from stays in sight.
    const page = (from: HTMLTableRowElement): number => {
        const scroller = from.closest(".data-table-scroll");
        const height = from.offsetHeight || 1;
        return Math.max(
            1,
            Math.floor((scroller?.clientHeight ?? 0) / height) - 1,
        );
    };

    const onRowKey = (
        e: React.KeyboardEvent<HTMLTableRowElement>,
        row: R,
        index: number,
    ): void => {
        const to = rowStep(rows.length, index, e.key, page(e.currentTarget));
        if (to !== undefined) {
            e.preventDefault();
            const target = rowAt(to);
            target?.focus();
            target?.scrollIntoView({ block: "nearest" });
            if (inert?.(rows[to]) === undefined) {
                onSelect?.(rows[to]);
            }
        } else if (e.key === "Enter" && onOpen) {
            e.preventDefault();
            onOpen(row);
        } else if (
            onMenu &&
            (e.key === "ContextMenu" || (e.shiftKey && e.key === "F10"))
        ) {
            e.preventDefault();
            const box = e.currentTarget.getBoundingClientRect();
            onMenu(box.left + 16, box.bottom, row, index);
        } else if (onKey?.(e.key, row)) {
            e.preventDefault();
        }
    };

    return (
        <div className="data-table-frame">
            <div className="data-table-scroll">
                <table
                    className={fixed ? "data-table fixed" : "data-table"}
                    aria-label={label}
                    style={
                        fixed
                            ? {
                                  width: shown.reduce(
                                      (sum, c) => sum + widths[c.key],
                                      0,
                                  ),
                              }
                            : undefined
                    }
                >
                    {fixed && (
                        <colgroup>
                            {shown.map((c) => (
                                <col
                                    key={c.key}
                                    style={{ width: widths[c.key] }}
                                />
                            ))}
                            {actions && <col style={{ width: 0 }} />}
                        </colgroup>
                    )}
                    <thead>
                        <tr ref={head}>
                            {shown.map((c) => (
                                <HeadCell
                                    key={c.key}
                                    column={c}
                                    sortable={
                                        onSort !== undefined &&
                                        (sortable?.includes(c.key) ?? false)
                                    }
                                    sort={sort ?? null}
                                    onSort={onSort}
                                    filter={filter?.(c.key)}
                                    onFilter={(x, y) =>
                                        setOpenFilter((open) =>
                                            open?.column === c.key
                                                ? null
                                                : { column: c.key, x, y },
                                        )
                                    }
                                    onResizeStart={
                                        onResize
                                            ? (e) => resizeFrom(c.key, e)
                                            : undefined
                                    }
                                    onMove={onMove}
                                />
                            ))}
                            {actions && <th aria-hidden="true" />}
                        </tr>
                    </thead>
                    <tbody ref={body}>
                        {rows.map((row, index) => {
                            const key = rowKey(row);
                            const reason = inert?.(row);
                            const sev = severity?.(row);
                            const className =
                                [
                                    reason === undefined ? null : "inert",
                                    key === selected ? "selected" : null,
                                    sev === undefined ? null : `row-sev-${sev}`,
                                ]
                                    .filter(Boolean)
                                    .join(" ") || undefined;
                            return (
                                <tr
                                    key={key}
                                    data-row-index={index}
                                    tabIndex={index === tabStop ? 0 : -1}
                                    className={className}
                                    title={reason}
                                    aria-selected={key === selected}
                                    onClick={() =>
                                        reason === undefined && onSelect?.(row)
                                    }
                                    onDoubleClick={() =>
                                        reason === undefined && onOpen?.(row)
                                    }
                                    onKeyDown={(e) => onRowKey(e, row, index)}
                                    onMouseEnter={() => onRest?.(row)}
                                    onFocus={() => onRest?.(row)}
                                    onContextMenu={(e) => {
                                        if (onMenu) {
                                            e.preventDefault();
                                            onMenu(
                                                e.clientX,
                                                e.clientY,
                                                row,
                                                index,
                                            );
                                        }
                                    }}
                                >
                                    {shown.map((c) => (
                                        <td
                                            key={c.key}
                                            className={
                                                c.dim ? "dim" : undefined
                                            }
                                        >
                                            {c.cell(row)}
                                        </td>
                                    ))}
                                    {actions && (
                                        <td className="row-actions-cell">
                                            <span className="row-actions">
                                                {actions(row, index)}
                                            </span>
                                        </td>
                                    )}
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
                {rows.length === 0 && (
                    <p className="data-table-empty">{empty}</p>
                )}
            </div>
            {openFilter && filter?.(openFilter.column) && (
                <FilterFlyout
                    x={openFilter.x}
                    y={openFilter.y}
                    label={
                        columns.find((c) => c.key === openFilter.column)
                            ?.label ?? ""
                    }
                    onClose={() => setOpenFilter(null)}
                >
                    {filter(openFilter.column)?.content}
                </FilterFlyout>
            )}
            {foot !== undefined && (
                <div className="data-table-foot">{foot}</div>
            )}
        </div>
    );
}

/**
 * A column's header. A click, Enter or Space cycles its sort when the table sorts by it; its filter
 * button opens the column's filter; its right edge is dragged to size it, and the header itself onto
 * another to move it there.
 */
function HeadCell<R>({
    column,
    sortable,
    sort,
    onSort,
    filter,
    onFilter,
    onResizeStart,
    onMove,
}: {
    column: DataColumn<R>;
    sortable: boolean;
    sort: SortState | null;
    onSort?: (next: SortState | null) => void;
    filter?: ColumnFilter;
    onFilter: (x: number, y: number) => void;
    onResizeStart?: (e: React.PointerEvent<HTMLSpanElement>) => void;
    onMove?: (from: string, to: string) => void;
}): React.JSX.Element {
    const [dropping, setDropping] = useState(false);
    const sorts = sortable && onSort !== undefined;
    const active = sorts && sort?.column === column.key ? sort : null;
    const cycle = (): void => {
        if (sorts) {
            onSort(nextSort(sort, column.key));
        }
    };
    return (
        <th
            className={
                [sorts ? "sortable" : null, dropping ? "drop-target" : null]
                    .filter(Boolean)
                    .join(" ") || undefined
            }
            scope="col"
            data-column={column.key}
            tabIndex={sorts ? 0 : undefined}
            aria-sort={
                !sorts
                    ? undefined
                    : active === null
                      ? "none"
                      : active.direction === "asc"
                        ? "ascending"
                        : "descending"
            }
            draggable={onMove !== undefined}
            onDragStart={(e) => {
                e.dataTransfer.setData("text/x-column", column.key);
                e.dataTransfer.effectAllowed = "move";
            }}
            onDragOver={(e) => {
                if (onMove && e.dataTransfer.types.includes("text/x-column")) {
                    e.preventDefault();
                    setDropping(true);
                }
            }}
            onDragLeave={() => setDropping(false)}
            onDrop={(e) => {
                setDropping(false);
                const from = e.dataTransfer.getData("text/x-column");
                if (onMove && from) {
                    e.preventDefault();
                    onMove(from, column.key);
                }
            }}
            onClick={cycle}
            onKeyDown={(e) => {
                if (sorts && (e.key === "Enter" || e.key === " ")) {
                    e.preventDefault();
                    cycle();
                }
            }}
        >
            <span className="th-label">
                {column.label}
                {active && (
                    <Icon
                        name={
                            active.direction === "asc"
                                ? "sortAscending"
                                : "sortDescending"
                        }
                        size={12}
                    />
                )}
            </span>
            {filter && (
                <button
                    type="button"
                    className={filter.active ? "th-filter active" : "th-filter"}
                    disabled={filter.reason !== undefined}
                    aria-pressed={filter.active}
                    title={filter.reason ?? l10n.t("Filter {0}", column.label)}
                    onClick={(e) => {
                        e.stopPropagation();
                        const box = e.currentTarget.getBoundingClientRect();
                        onFilter(box.left, box.bottom + 2);
                    }}
                >
                    <Icon name="filter" size={12} />
                </button>
            )}
            {onResizeStart && (
                <span
                    className="th-grip"
                    title={l10n.t("Drag to resize")}
                    draggable={false}
                    onPointerDown={onResizeStart}
                    onClick={(e) => e.stopPropagation()}
                    onDragStart={(e) => e.preventDefault()}
                />
            )}
        </th>
    );
}

/**
 * A column's filter, opened below its header and over the rows, as a flyout rather than a
 * collapsible: the table does not move. Escape or a click outside closes it.
 */
function FilterFlyout({
    x,
    y,
    label,
    onClose,
    children,
}: {
    x: number;
    y: number;
    label: string;
    onClose: () => void;
    children: ReactNode;
}): React.JSX.Element {
    const frame = useRef<HTMLDivElement>(null);
    useEffect(() => {
        const away = (e: PointerEvent): void => {
            // A header's filter button toggles it itself.
            const target = e.target as Element;
            if (
                !frame.current?.contains(target) &&
                target.closest(".th-filter") === null
            ) {
                onClose();
            }
        };
        const escape = (e: KeyboardEvent): void => {
            if (e.key === "Escape") {
                onClose();
            }
        };
        window.addEventListener("pointerdown", away);
        window.addEventListener("keydown", escape);
        return () => {
            window.removeEventListener("pointerdown", away);
            window.removeEventListener("keydown", escape);
        };
    }, [onClose]);
    return (
        <div
            ref={frame}
            className="column-filter"
            role="dialog"
            aria-label={l10n.t("Filter {0}", label)}
            style={{ left: x, top: y }}
        >
            {children}
        </div>
    );
}
