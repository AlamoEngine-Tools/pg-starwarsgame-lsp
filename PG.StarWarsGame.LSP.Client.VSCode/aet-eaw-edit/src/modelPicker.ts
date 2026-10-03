// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The "Preview Model" picker's items and its choice. Kept free of `vscode` so the unit harness can
// run it; extension.ts owns the QuickPick itself.

import {type PreviewModelEntry} from './protocol/modelPreview';

/** One row of the picker. Structurally a `vscode.QuickPickItem`. */
export interface ModelPickItem {
    label: string;
    description: string;
}

/** The picker's rows, in the server's order. */
export function modelPickItems(models: readonly PreviewModelEntry[]): ModelPickItem[] {
    return models.map(m => ({label: m.name, description: m.baseGame ? 'Base game' : 'Project'}));
}

/**
 * The model to open: the highlighted row, else what was typed, else nothing.
 *
 * Typed text is a fallback rather than an error because the list is the asset catalog, and the
 * catalog can lag the disk - a model saved a moment ago must still open by name, as it did before
 * the list existed.
 */
export function pickedModelName(active: { label: string } | undefined, typed: string): string | null {
    if (active !== undefined) {
        return active.label;
    }

    const name = typed.trim();
    return name === '' ? null : name;
}
