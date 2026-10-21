// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * The Mod Browser's protocol (#270): an outline's pages with their counts, and one page of entries,
 * filtered and paged on the server. Mirrors `ModBrowserProtocol.cs`.
 */

/** The outlines the sidebar picks between. */
export type SymbolOutline = "gameData" | "assets" | "schema";

export const GET_SYMBOL_OUTLINE = "aet/getSymbolOutline";
export const GET_SYMBOL_PAGE = "aet/getSymbolPage";

export interface GetSymbolOutlineParams {
    outline: SymbolOutline;
}

/** One page of an outline: `xml:<type>`, `models`, `enums` and the rest. */
export interface SymbolPageSummary {
    page: string;
    count: number;
}

/** A layer of the project; an origin `layer:N` names one by rank. */
export interface SymbolLayer {
    rank: number;
    name: string;
}

export interface GetSymbolOutlineResult {
    pages: SymbolPageSummary[];
    layers: SymbolLayer[];
}

export interface GetSymbolPageParams {
    page: string;
    /** A name to match; names starting with it come first. */
    query?: string;
    /** Kinds an entry must be one of. */
    kinds?: string[];
    /** Origins an entry must come from: `game`, `project`, `layer:N`. */
    origins?: string[];
    offset?: number;
    limit?: number;
}

/** One entry, as the definition the game uses. */
export interface SymbolRow {
    id: string;
    name: string;
    type?: string | null;
    kinds: string[];
    origin: string;
    originName?: string | null;
    overrides: string[];
}

export interface GetSymbolPageResult {
    rows: SymbolRow[];
    total: number;
}
