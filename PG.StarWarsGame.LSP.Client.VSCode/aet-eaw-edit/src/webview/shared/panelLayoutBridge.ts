// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Joins the pure size store to the webview it runs in: the DOM it reads the stored layout from, and
// the host it reports changes to.
//
// Split from `panelLayout.ts` so that module stays testable under `node --test`, which has no DOM
// and no VS Code API.

import { applyStoredLayout, setPanelSizeReporter } from './panelLayout';

/** The minimum of the VS Code webview API this needs. */
interface MessagePoster {
    postMessage(message: unknown): void;
}

/**
 * Reads the layout the host seeded into the page and starts reporting changes back to it.
 *
 * The layout arrives as a `data-` attribute rather than a message because a message would land
 * AFTER the docks had already mounted at their defaults, and every panel would visibly jump a frame
 * later. It is not an inline script for the same reason the bundle is not: the webview CSP names the
 * extension origin and no `'unsafe-inline'`, which is worth keeping.
 */
export function initPanelLayout(poster: MessagePoster): void {
    const raw = document.getElementById('root')?.dataset.panelLayout;
    if (raw !== undefined && raw !== '') {
        try {
            applyStoredLayout(JSON.parse(raw));
        } catch {
            // A layout we cannot read is a layout we do not apply. Every panel then opens at its
            // default, which is exactly where they opened before any of this existed.
        }
    }

    setPanelSizeReporter(([key, value]) => {
        poster.postMessage({ type: 'setPanelLayout', key, value });
    });
}
