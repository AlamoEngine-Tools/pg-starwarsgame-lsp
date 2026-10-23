// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Tabs inside a panel, as the editor's own (#270): a table keeps a tab of its own, and every entry
// opened from it gets a preview tab beside it, so going back to the table finds it as it was left.
// One preview tab is unpinned and reused, as VS Code's italic preview tab; pinning keeps a tab. The
// strip that draws them is TabStrip. Pure, so it is testable without a webview.

/** One preview tab: the entry it shows, keyed so one entry never has two tabs. */
export interface PreviewTab<T> {
    key: string;
    item: T;
    pinned: boolean;
}

export interface Tabs<T> {
    previews: PreviewTab<T>[];
    /** The active preview tab's key; null when the table's tab is active. */
    active: string | null;
}

export const NO_TABS: Tabs<never> = { previews: [], active: null };

/**
 * Opens an entry in the foreground: its tab if it already has one, else the unpinned tab, taken
 * over in its place, else a new tab at the end.
 */
export function openPreview<T>(tabs: Tabs<T>, key: string, item: T): Tabs<T> {
    if (tabs.previews.some((t) => t.key === key)) {
        return { ...tabs, active: key };
    }
    const tab: PreviewTab<T> = { key, item, pinned: false };
    const reused = tabs.previews.findIndex((t) => !t.pinned);
    const previews =
        reused === -1
            ? [...tabs.previews, tab]
            : tabs.previews.map((t, i) => (i === reused ? tab : t));
    return { previews, active: key };
}

/** Keeps a tab: the next entry opened gets a tab of its own instead of taking this one over. */
export function pinTab<T>(tabs: Tabs<T>, key: string): Tabs<T> {
    return {
        ...tabs,
        previews: tabs.previews.map((t) =>
            t.key === key ? { ...t, pinned: true } : t,
        ),
    };
}

/** Closes a tab; the active one hands over to its right neighbour, else its left, else the table. */
export function closeTab<T>(tabs: Tabs<T>, key: string): Tabs<T> {
    const at = tabs.previews.findIndex((t) => t.key === key);
    if (at === -1) {
        return tabs;
    }
    const previews = tabs.previews.filter((t) => t.key !== key);
    if (tabs.active !== key) {
        return { previews, active: tabs.active };
    }
    const next = previews[at] ?? previews[at - 1];
    return { previews, active: next?.key ?? null };
}

/** Brings the table's tab to the front. */
export function showTable<T>(tabs: Tabs<T>): Tabs<T> {
    return { ...tabs, active: null };
}
