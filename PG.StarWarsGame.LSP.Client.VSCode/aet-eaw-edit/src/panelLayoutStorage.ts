// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { panelLayoutFrom, type PanelLayout } from './webview/shared/panelLayout';

/**
 * How wide the reader likes each editor's dock, and how tall its drawer.
 *
 * Backed by `globalState`, like `viewerSettingsStorage` and unlike `dialogGeometryStorage`. Which
 * width you read comfortably at follows the PERSON: it is a property of the screen you are sitting
 * at and the way you work, not of the mod you happen to have open, and having to drag every dock
 * back into shape in each project would be the annoyance this exists to remove.
 *
 * Keyed `<surface>.<control>` so every editor keeps its own value - the story graph's dock and the
 * model preview's dock are genuinely different questions.
 *
 * Held in a module rather than threaded through the panel constructors, matching the two storage
 * modules beside it: panels are created from several call sites and none of them otherwise cares.
 */
const KEY = 'aet-eaw-edit.panelLayout';

let store: vscode.Memento | undefined;

/** Called once from `activate`, with `context.globalState`. */
export function initPanelLayoutStorage(memento: vscode.Memento): void {
    store = memento;
}

/**
 * Every stored size, validated entry by entry.
 *
 * Never throws and never returns junk: the blob outlives the build that wrote it, and a dock that
 * opens at NaN pixels wide would leave the reader no handle to drag back.
 */
export function readPanelLayout(): PanelLayout {
    return panelLayoutFrom(store?.get<unknown>(KEY));
}

/**
 * Records one panel's size.
 *
 * Failures are swallowed for the same reason the dialog geometry's are: a dock that reopens at its
 * default is a small annoyance, and an unhandled rejection from a storage write would be a worse one.
 */
export function savePanelSize(key: string, value: number): void {
    if (store === undefined) { return; }

    const next = panelLayoutFrom({ ...readPanelLayout(), [key]: value });
    void Promise.resolve(store.update(KEY, next)).then(undefined, () => { /* not worth reporting */ });
}
