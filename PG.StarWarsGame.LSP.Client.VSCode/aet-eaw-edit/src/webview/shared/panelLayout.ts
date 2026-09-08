// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How wide the reader likes a dock, and how tall they like a drawer.
//
// One store for every editor, keyed `<surface>.<control>` so each keeps its own value - the story
// graph's dock and the model preview's dock are different questions and always were. Before this,
// each webview held a module-level `let dockWidthMemo`, which survived a re-render and nothing else:
// closing the tab, reloading the window or restarting VS Code put every one of them back to a
// hardcoded default.
//
// The values are a USER setting, not a project one. Which width you like reading at follows the
// person the way the preview's light rig does (`viewerSettingsStorage`), unlike where a dialog was
// dragged, which is deliberately per project (`dialogGeometryStorage`). The host backs this with
// `globalState` for that reason.

/** Sizes by `<surface>.<control>` key, in pixels. */
export type PanelLayout = Record<string, number>;

/** What the webview posts when a size changes. */
export type PanelSizeReporter = (entry: [key: string, value: number]) => void;

/**
 * The largest size worth storing. Well past any sane dock or drawer, and far enough from
 * `Number.MAX_SAFE_INTEGER` that a corrupt entry cannot open a panel wider than the screen.
 */
const MAX_SIZE = 4000;

let sizes: PanelLayout = {};

/** Keys the reader has changed this session; the host's stored layout must not overwrite these. */
let touched = new Set<string>();

let report: PanelSizeReporter = () => { /* replaced by the webview's own poster */ };

/**
 * Validates a stored layout entry by entry, and never throws.
 *
 * The same contract as `viewerSettingsFrom`, for the same reason: this blob outlives the build that
 * wrote it, so one unusable entry must cost that entry and nothing else. A panel that opens at NaN
 * pixels leaves the reader no way back.
 */
export function panelLayoutFrom(value: unknown): PanelLayout {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) { return {}; }

    const layout: PanelLayout = {};
    for (const [key, size] of Object.entries(value as Record<string, unknown>)) {
        if (typeof size === 'number' && Number.isFinite(size) && size > 0 && size <= MAX_SIZE) {
            layout[key] = size;
        }
    }
    return layout;
}

/** Installs the poster that carries changes to the host. Called once when the webview starts. */
export function setPanelSizeReporter(reporter: PanelSizeReporter): void {
    report = reporter;
}

/** The stored size for `key`, or `fallback` when the reader has never set one. */
export function readPanelSize(key: string, fallback: number): number {
    return sizes[key] ?? fallback;
}

/** Records a size the reader just dragged to, and tells the host about it. */
export function writePanelSize(key: string, value: number): void {
    sizes[key] = value;
    touched.add(key);
    report([key, value]);
}

/**
 * Takes the layout the host has stored.
 *
 * Deliberately does not overwrite a key the reader has already dragged this session: the stored
 * layout arrives asynchronously, after the panels have mounted at their defaults, and a reader quick
 * enough to grab a handle first should not have it yanked out from under them.
 */
export function applyStoredLayout(value: unknown): void {
    for (const [key, size] of Object.entries(panelLayoutFrom(value))) {
        if (!touched.has(key)) { sizes[key] = size; }
    }
}

/** Test seam: forgets everything, and optionally watches what would be sent to the host. */
export function resetPanelLayoutForTests(reporter?: PanelSizeReporter): void {
    sizes = {};
    touched = new Set<string>();
    report = reporter ?? (() => { /* nothing is listening */ });
}
