// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';

import { StoredGeometry } from './webview/shared/modalGeometry';

/**
 * Where each dialog was last put, kept per project.
 *
 * Backed by `workspaceState`, so the placement follows the mod rather than the machine: a project
 * whose credits file needs a tall dialog keeps one, and opening a different mod does not inherit it.
 *
 * Held in a module rather than threaded through every panel constructor - the panels are created
 * from four different call sites and none of them otherwise cares about storage.
 */
const KEY = 'aet-eaw-edit.dialogGeometry';

let store: vscode.Memento | undefined;

/** Called once from `activate`, with `context.workspaceState`. */
export function initDialogGeometryStorage(memento: vscode.Memento): void {
    store = memento;
}

export function readDialogGeometry(): Record<string, StoredGeometry> {
    return store?.get<Record<string, StoredGeometry>>(KEY) ?? {};
}

/**
 * Records one dialog's placement.
 *
 * Failures are swallowed: a dialog that reopens centred is a small annoyance, and an unhandled
 * rejection from a storage write would be a worse one.
 */
export function saveDialogGeometry(id: string, geometry: StoredGeometry): void {
    if (store === undefined || !isGeometry(geometry)) { return; }

    void Promise.resolve(store.update(KEY, { ...readDialogGeometry(), [id]: geometry }))
        .then(undefined, () => { /* placement is not worth reporting a failure over */ });
}

/**
 * Guards what reaches storage. The payload crosses a webview boundary, and a NaN written once would
 * come back every session and put the dialog nowhere.
 */
function isGeometry(value: unknown): value is StoredGeometry {
    if (value === null || typeof value !== 'object') { return false; }

    const candidate = value as Record<string, unknown>;
    return (['xRatio', 'yRatio', 'width', 'height'] as const).every(
        field => typeof candidate[field] === 'number' && Number.isFinite(candidate[field]));
}
