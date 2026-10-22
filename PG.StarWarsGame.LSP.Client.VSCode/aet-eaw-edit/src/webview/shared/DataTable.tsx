// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every table of rows, read as the editor's own Keyboard Shortcuts table. Its look is .data-table in
// dockChromeCss; the keys follow rovingFocus, a header click cycles tableSort. What a row IS, and
// what it can do, is the owner's: the table draws, selects and moves focus.

import { ReactNode, useRef } from "react";

import { Icon } from "./Icon";
import { rowStep } from "./rovingFocus";
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
}

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
}: DataTableProps<R>): React.JSX.Element {
    const body = useRef<HTMLTableSectionElement>(null);
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
                <table className="data-table" aria-label={label}>
                    <thead>
                        <tr>
                            {columns.map((c) => (
                                <HeadCell
                                    key={c.key}
                                    column={c}
                                    sortable={
                                        onSort !== undefined &&
                                        (sortable?.includes(c.key) ?? false)
                                    }
                                    sort={sort ?? null}
                                    onSort={onSort}
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
                                    {columns.map((c) => (
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
            {foot !== undefined && (
                <div className="data-table-foot">{foot}</div>
            )}
        </div>
    );
}

/** A column's header: a click, Enter or Space cycles its sort when the table sorts by it. */
function HeadCell<R>({
    column,
    sortable,
    sort,
    onSort,
}: {
    column: DataColumn<R>;
    sortable: boolean;
    sort: SortState | null;
    onSort?: (next: SortState | null) => void;
}): React.JSX.Element {
    if (!sortable || onSort === undefined) {
        return <th scope="col">{column.label}</th>;
    }
    const active = sort?.column === column.key ? sort : null;
    const cycle = (): void => onSort(nextSort(sort, column.key));
    return (
        <th
            className="sortable"
            scope="col"
            tabIndex={0}
            aria-sort={
                active === null
                    ? "none"
                    : active.direction === "asc"
                      ? "ascending"
                      : "descending"
            }
            onClick={cycle}
            onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    cycle();
                }
            }}
        >
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
        </th>
    );
}
