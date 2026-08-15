// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { viewerSettingsFrom, type ViewerSettings } from './webview/preview/viewerSettings';

/**
 * How the reader likes the preview's ROOM: the grid, the floor, the lights, the background.
 *
 * Backed by `globalState`, unlike the dialog geometry beside it. That one is deliberately per
 * project - a mod whose credits file needs a tall dialog keeps one - but which way you like the
 * light and whether you want a grid follows the PERSON, and having to set it again in every mod
 * would be the annoyance this exists to remove.
 *
 * Held in a module rather than threaded through the panel constructor, matching
 * `dialogGeometryStorage`: the preview is opened from several call sites and none of them otherwise
 * cares about storage.
 */
const KEY = 'aet-eaw-edit.previewViewerSettings';

let store: vscode.Memento | undefined;

/** Called once from `activate`, with `context.globalState`. */
export function initViewerSettingsStorage(memento: vscode.Memento): void {
    store = memento;
}

/**
 * What was stored, defaulted field by field.
 *
 * The read never trusts what it finds: the blob outlives the build that wrote it, and a preview
 * that opens blank over one bad value in a settings file gives the reader no way back.
 */
export function readViewerSettings(): ViewerSettings {
    return viewerSettingsFrom(store?.get<unknown>(KEY));
}

/**
 * Records the room.
 *
 * Normalised before it is written, so nothing that failed validation on the way in can be handed
 * back out again next session. Failures are swallowed - a grid that forgets itself is a small
 * annoyance and an unhandled rejection from a storage write is a worse one.
 */
export function saveViewerSettings(settings: unknown): void {
    if (store === undefined) {
        return;
    }

    void Promise.resolve(store.update(KEY, viewerSettingsFrom(settings)))
        .then(undefined, () => { /* a preference is not worth reporting a failure over */ });
}
