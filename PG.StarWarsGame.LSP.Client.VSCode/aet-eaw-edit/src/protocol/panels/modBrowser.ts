// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The Mod Browser's messages (#208): ModBrowserPanel and modBrowser.tsx. Its lists, pages and previews
// come through the shared request relay (common.ts) on MOD_BROWSER_METHODS; these are the rest.

import type { SymbolOutline } from "../modBrowser";

/** The bundle is listening; the outline can be sent. */
export interface ModBrowserReady {
    type: "ready";
}

/** One of an entry's actions, run by its command with the entry and this panel's project. */
export interface ModBrowserRun {
    type: "run";
    action: string;
    id: string;
    name: string;
    /** The entry's type: one name may be objects of several types. */
    objectType?: string | null;
}

/** Opens a file at a line: a row's definition, or a place a preview names. */
export interface ModBrowserOpenLocation {
    type: "openLocation";
    uri: string;
    line: number;
}

/** Copies an entry's name. */
export interface ModBrowserCopy {
    type: "copy";
    text: string;
}

/** Shows an entry's file in the file manager. */
export interface ModBrowserReveal {
    type: "reveal";
    path: string;
}

export type ModBrowserToHost =
    | ModBrowserReady
    | ModBrowserRun
    | ModBrowserOpenLocation
    | ModBrowserCopy
    | ModBrowserReveal;

/** The outline to show: the one the reader picked in the view. */
export interface ModBrowserOutline {
    type: "outline";
    outline: SymbolOutline;
}

/** The server's index changed; what was loaded is asked again. */
export interface ModBrowserRefresh {
    type: "refresh";
}

export type ModBrowserToWebview = ModBrowserOutline | ModBrowserRefresh;
