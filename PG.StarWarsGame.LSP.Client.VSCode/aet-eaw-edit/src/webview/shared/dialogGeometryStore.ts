// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where each dialog was last put, remembered per project.
//
// Held in a module-level map rather than fetched when a dialog opens, because a dialog has to know
// where it belongs on its very first render: asking the host at that point would show it centred and
// then jerk it into place. The host sends the whole map once when the editor loads - it is a handful
// of numbers - and this hands it out synchronously from then on.
//
// The host owns the actual storage (workspaceState), so the placement follows the project rather
// than the machine, and a different mod opens with its own layout.

import { StoredGeometry } from './modalGeometry';

/**
 * Identifies a dialog across sessions.
 *
 * Hand-assigned constants, deliberately not generated: an id minted at runtime would be new on
 * every open and remember nothing. Adding a dialog means adding an id here, which is also the
 * cheapest way to notice two dialogs sharing one.
 */
export const DIALOG_IDS = {
    addLanguage: 'loc.add-language',
    fillLanguage: 'loc.fill-language',
    convertFormat: 'loc.convert-format',
    exportDat: 'loc.export-dat',
    addTranslation: 'loc.add-translation',
} as const;

export type DialogId = typeof DIALOG_IDS[keyof typeof DIALOG_IDS];

let remembered = new Map<string, StoredGeometry>();
let publish: ((id: string, geometry: StoredGeometry) => void) | null = null;

/** Seeds the store from what the host had saved. Called once, as the editor loads. */
export function loadDialogGeometry(
    stored: Record<string, StoredGeometry> | undefined,
    onChange: (id: string, geometry: StoredGeometry) => void,
): void {
    remembered = new Map(Object.entries(stored ?? {}));
    publish = onChange;
}

export function rememberedGeometry(id: string): StoredGeometry | undefined {
    return remembered.get(id);
}

/**
 * Records where a dialog ended up, and tells the host to persist it.
 *
 * Called when a drag finishes rather than as it moves: the intermediate positions are not decisions,
 * and one message per drag keeps a resize from posting a hundred of them.
 */
export function rememberGeometry(id: string, geometry: StoredGeometry): void {
    remembered.set(id, geometry);
    publish?.(id, geometry);
}
