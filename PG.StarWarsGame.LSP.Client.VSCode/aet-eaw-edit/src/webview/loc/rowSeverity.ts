// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which rows a validation run has something to say about, and how badly.

import { LocProblem } from './useLocPanel';
import { LocRow } from './locRow';

/** Highest-first, so the worst finding on a row is the one that colours it. */
const RANK: Record<string, number> = { error: 3, warning: 2, info: 1 };

/**
 * The worst severity reported against each row, keyed by row index.
 *
 * Built once per validation result rather than scanned per row: a master text file runs to tens of
 * thousands of rows, and the grid re-renders on every scroll.
 *
 * A row with nothing against it is absent from the map, which is what lets the caller leave valid
 * rows unmarked - an "ok" class on every row would tint the whole table.
 */
export function severityByRow(
    problems: readonly LocProblem[], rows: readonly LocRow[],
): Map<number, string> {
    if (problems.length === 0) { return new Map(); }

    // Keys are not unique - a duplicate is the whole point of one of these findings - so a key-
    // addressed problem marks every row carrying it, not just the first.
    const rowsByKey = new Map<string, number[]>();
    for (const row of rows) {
        const existing = rowsByKey.get(row.key);
        if (existing) { existing.push(row.index); } else { rowsByKey.set(row.key, [row.index]); }
    }

    const worst = new Map<number, string>();
    const mark = (index: number, severity: string): void => {
        const current = worst.get(index);
        if (current === undefined || rankOf(severity) > rankOf(current)) {
            worst.set(index, severity);
        }
    };

    for (const problem of problems) {
        // Index first: it is the only thing that can point at a row with no key, and a blank key is
        // exactly the row worth pointing at.
        if (problem.index !== null && problem.index !== undefined) {
            mark(problem.index, problem.severity);
            continue;
        }

        if (problem.key === null || problem.key === undefined) { continue; }
        for (const index of rowsByKey.get(problem.key) ?? []) { mark(index, problem.severity); }
    }

    return worst;
}

/**
 * The class that tints a row, or undefined for a row with nothing against it.
 *
 * An unrecognised severity is treated as a warning, matching how the Validate tag grades one: a
 * level this does not know about must still show, and picking the mildest tint would hide it.
 */
export function rowSeverityClass(severity: string | undefined): string | undefined {
    if (severity === undefined) { return undefined; }
    return `row-sev-${severity in RANK ? severity : 'warning'}`;
}

function rankOf(severity: string): number {
    return RANK[severity] ?? RANK.warning;
}
