// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * The Mod Browser's protocol (#270): an outline's pages with their counts, and one page of entries,
 * filtered and paged on the server. Mirrors `ModBrowserProtocol.cs`.
 */

import { GET_ENCYCLOPEDIA_ENTRY } from "./encyclopedia";
import { GET_MODEL_DETAIL } from "./modelPreview";

/** The outlines the sidebar picks between. */
export type SymbolOutline = "gameData" | "assets" | "scripts" | "schema";

export const GET_SYMBOL_OUTLINE = "aet/getSymbolOutline";
export const GET_SYMBOL_PAGE = "aet/getSymbolPage";
export const GET_SYMBOL_NODE = "aet/getSymbolNode";
export const GET_SYMBOL_PREVIEW = "aet/getSymbolPreview";

/**
 * The requests the panel's webview may have relayed to its server, and no other: its own four,
 * and the encyclopedia card an object's preview embeds.
 */
export const MOD_BROWSER_METHODS: readonly string[] = [
    GET_SYMBOL_OUTLINE,
    GET_SYMBOL_PAGE,
    GET_SYMBOL_NODE,
    GET_SYMBOL_PREVIEW,
    GET_ENCYCLOPEDIA_ENTRY,
    // A model's tree in its preview, read as the model preview reads it.
    GET_MODEL_DETAIL,
];

/** The entry whose relation a page lists, in place of the page's own entries. */
export interface SymbolParent {
    page: string;
    id: string;
    /** `models`, `referencedBy`, `definitions` and the rest. */
    relation: string;
}

export interface GetSymbolNodeParams {
    page: string;
    id: string;
}

/** One relation of an opened entry; a reason says why it lists none. */
export interface SymbolRelation {
    relation: string;
    /** Null until counted, for a relation that needs every object's tags. */
    count?: number | null;
    /** `none`, or `notInModFiles` when the entry's tags are not known. */
    reason?: string | null;
}

/** Where a definition starts. */
export interface SymbolLocation {
    uri: string;
    /** Zero-based. */
    line: number;
}

export interface GetSymbolNodeResult {
    entry?: SymbolRow | null;
    relations: SymbolRelation[];
    /** Where the definition the game uses is; null for one no text file of the project defines. */
    location?: SymbolLocation | null;
    /** The object this one is a variant of. */
    variantOf?: string | null;
    /** What can be done with the entry - the code lens set - each with why when it cannot. */
    actions?: SymbolAction[];
    /** Opening the entry into its preview; its reason is `noPreview` where its type has none. */
    open?: SymbolAction | null;
    /** The entry's file on disk, for an asset or a script that has one. */
    file?: string | null;
}

/** One thing that can be done with an entry. */
export interface SymbolAction {
    /**
     * `definition`, `previewUnit`, `encyclopedia`, `effectiveObject`, `previewModel`, `previewClip`
     * (an animation, on its model) or `reveal`.
     */
    action: string;
    /** Why it is unavailable; null when it is available. */
    reason?: string | null;
}

/** An entry's preview: its adapter's fixed grid, and every tile placed on it. */
export interface GetSymbolPreviewResult {
    /** `gameObject`, `xmlObject`, `model`, `particleSystem` or `luaScript`; null when the entry has no preview. */
    adapter?: string | null;
    tiles: PreviewTile[];
    /** The grid's columns and rows; the webview stretches them over the tab. */
    columns?: number;
    rows?: number;
}

/** One tile, placed and sized in grid cells; which members it fills depends on its kind. */
export interface PreviewTile {
    /** `identity`, `problems`, `encyclopedia` or `entries`. */
    kind: string;
    /** Its first column and row, from 0. */
    x: number;
    y: number;
    width: number;
    height: number;
    relation?: string | null;
    count?: number | null;
    entry?: SymbolRow | null;
    location?: SymbolLocation | null;
    variantOf?: string | null;
    entries?: PreviewEntry[];
    objectId?: string | null;
    /** The identity tile's counts, in the order shown - a model's bones, textures and the rest. */
    facts?: PreviewFact[] | null;
    /** The identity tile's file on disk, for an asset or a script that has one. */
    file?: PreviewFile | null;
    /** A documentation tile's properties, in the order shown, each by a key the panel names. */
    properties?: PreviewProperty[] | null;
    /** A documentation tile's description, in the language the server documents in. */
    text?: string | null;
    /** A documentation tile's notes, ranked as the hovers rank them. */
    notes?: PreviewNote[] | null;
}

/** One property of a schema element: `valueType`, `references`, `enum` and the rest. */
export interface PreviewProperty {
    key: string;
    /** As the schema names it; empty for a property that is only there or not. */
    value: string;
}

/** One note on a schema element: its kind, what it says and what the kind carries. */
export interface PreviewNote {
    kind: string;
    text?: string | null;
    value?: string | null;
}

/** A file on disk, as its header describes it. */
export interface PreviewFile {
    /** Its size in bytes. */
    size: number;
    /** When it was created and last changed, as ISO 8601 UTC timestamps. */
    created: string;
    modified: string;
    /** A script's line count. */
    lines?: number | null;
}

/** One count in an identity tile: `bones`, `textures`, `particleSystems` or `animations`. */
export interface PreviewFact {
    key: string;
    value: number;
}

/** One line of a problems or entries tile. */
export interface PreviewEntry {
    name: string;
    detail?: string | null;
    /** An enum value's value where it is not its name. */
    value?: string | null;
    /** The entry it opens; absent for a problem. */
    row?: SymbolRow | null;
    /** Where a problem is. */
    location?: SymbolLocation | null;
    /** A problem's severity, "error" or "warning". */
    severity?: string | null;
    /** A problem the server found in an asset: "missingTexture" or "missingParticleSystem". */
    reason?: string | null;
}

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
    /**
     * The column sorted by - `name`, `type`, `kinds` or `origin` - before the page is cut; none
     * keeps the list's own order.
     */
    sort?: string;
    descending?: boolean;
    offset?: number;
    limit?: number;
    /** When set, the page lists this entry's relation. */
    parent?: SymbolParent;
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
    /** The page the entry opens on; none for a missing name, a tag or a definition's place. */
    page?: string | null;
    /** An enum value's value - what the engine uses for it - beside its name, the key XML writes. */
    value?: string | null;
}

/** A kind with how many entries of the list are of it. */
export interface SymbolKindCount {
    kind: string;
    count: number;
}

export interface GetSymbolPageResult {
    rows: SymbolRow[];
    total: number;
    /**
     * Every kind a game object can be, counted over the name and origin filters - not the kind
     * filter. Empty for a list without game objects.
     */
    kinds?: SymbolKindCount[];
}
