// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import type { Memento } from "vscode";

import {
    type ProjectSettings,
    projectSettingsFrom,
} from "./webview/preview/projectSettings";

/**
 * What the preview remembers about ONE PROJECT: the weapon bench, the faction and its colour.
 *
 * Backed by `workspaceState`, like `dialogGeometryStorage` and unlike `viewerSettingsStorage`, and
 * keyed by the project inside it. Every field here names something out of the mod's own tree - a
 * damage type, a faction - so one set against one mod means nothing in the next. The reported fault
 * was exactly that: a damage type the open tree does not declare, arriving from somewhere else
 * entirely. A window holding two projects is the same fault one level down, which is why the key
 * is the project's and not the window's.
 *
 * Held in a module rather than threaded through the panel constructor, matching the two stores
 * beside it: the preview is opened from several call sites and none of them otherwise cares about
 * storage.
 */
const KEY = "aet-eaw-edit.previewProjectSettings.byProject";

let store: Memento | undefined;

/** Called once from `activate`, with `context.workspaceState`. */
export function initProjectSettingsStorage(memento: Memento): void {
    store = memento;
}

/**
 * What this project stored, defaulted field by field.
 *
 * There is deliberately NO migration from the earlier shapes - the global blob, then one blob per
 * window. Copying either in would carry one mod's damage types and factions into another project,
 * which is the fault this store exists to end.
 */
export function readProjectSettings(projectKey: string): ProjectSettings {
    return projectSettingsFrom(byProject()[projectKey]);
}

/**
 * Records them for one project.
 *
 * Normalised before they are written, so nothing that failed validation on the way in can be handed
 * back out again next session. Failures are swallowed - a weapon that forgets itself is a small
 * annoyance and an unhandled rejection from a storage write is a worse one.
 */
export function saveProjectSettings(
    projectKey: string,
    settings: unknown,
): void {
    if (store === undefined) {
        return;
    }

    const next = {
        ...byProject(),
        [projectKey]: projectSettingsFrom(settings),
    };
    void Promise.resolve(store.update(KEY, next)).then(undefined, () => {
        /* a saved weapon is not worth reporting a failure over */
    });
}

/** The stored map, or an empty one when nothing or something else is there. */
function byProject(): Record<string, unknown> {
    const raw = store?.get<unknown>(KEY);
    return typeof raw === "object" && raw !== null && !Array.isArray(raw)
        ? (raw as Record<string, unknown>)
        : {};
}
