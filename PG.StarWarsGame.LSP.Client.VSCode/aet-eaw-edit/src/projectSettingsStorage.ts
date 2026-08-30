// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { projectSettingsFrom, type ProjectSettings } from './webview/preview/projectSettings';

/**
 * What the preview remembers about THIS PROJECT: the weapon bench, the faction and its colour.
 *
 * Backed by `workspaceState`, like `dialogGeometryStorage` and unlike `viewerSettingsStorage`. Every
 * field here names something out of the mod's own tree - a damage type, a faction - so one set
 * against one mod means nothing in the next. The reported fault was exactly that: a damage type the
 * open tree does not declare, arriving from somewhere else entirely.
 *
 * Held in a module rather than threaded through the panel constructor, matching the two stores
 * beside it: the preview is opened from several call sites and none of them otherwise cares about
 * storage.
 */
const KEY = 'aet-eaw-edit.previewProjectSettings';

let store: vscode.Memento | undefined;

/** Called once from `activate`, with `context.workspaceState`. */
export function initProjectSettingsStorage(memento: vscode.Memento): void {
    store = memento;
}

/**
 * What this project stored, defaulted field by field.
 *
 * There is deliberately NO migration from the old global blob. Copying it in would carry one mod's
 * damage types and factions into every project on the machine, which is the fault this move exists
 * to end.
 */
export function readProjectSettings(): ProjectSettings {
    return projectSettingsFrom(store?.get<unknown>(KEY));
}

/**
 * Records them.
 *
 * Normalised before they are written, so nothing that failed validation on the way in can be handed
 * back out again next session. Failures are swallowed - a weapon that forgets itself is a small
 * annoyance and an unhandled rejection from a storage write is a worse one.
 */
export function saveProjectSettings(settings: unknown): void {
    if (store === undefined) {
        return;
    }

    void Promise.resolve(store.update(KEY, projectSettingsFrom(settings)))
        .then(undefined, () => { /* a saved weapon is not worth reporting a failure over */ });
}
