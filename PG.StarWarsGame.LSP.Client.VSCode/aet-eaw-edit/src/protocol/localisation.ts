// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The localisation editor's wire contract. Mirrors the records under
// PG.StarWarsGame.LSP.Server/Localisation - see ../protocol/README.md for why this is declared
// exactly once.

/**
 * What kind of localisation file this is. Mirrors `LocCategory` in CreditsFileClassifier.cs.
 *
 * A string rather than a union type because the server is free to add one: an unrecognised category
 * has to stay reachable in the tree and the editor, since a file the user cannot see is a file they
 * cannot fix. Everything not explicitly credits is treated as text.
 */
export const LOC_CATEGORY = {
    /** Keyed: one entry per key, order irrelevant. The usual MasterText case. */
    text: 'text',
    /** Ordered: duplicate keys allowed, row order significant. */
    credits: 'credits',
} as const;

// ── aet/getLocalisationProjects ──────────────────────────────────────────────

/** One localisation file, as `aet/getLocalisationProjects` reports it. */
export interface LocProjectInfo {
    label: string;
    filePath: string;
    resourceType: string;
    projectName: string;
    rank: number;
    /** See {@link LOC_CATEGORY}; anything unrecognised is treated as text rather than hidden. */
    category: string;
    /**
     * The language this file holds, for the single-language formats that name one in their file
     * name (.properties, .dat). Absent for CSV and XML, which carry every language inside the file.
     */
    language?: string | null;
}

export interface GetLocalisationProjectsResult {
    projects: LocProjectInfo[];
    error?: string | null;
}

// ── aet/getLocalisationRows ──────────────────────────────────────────────────

/**
 * One language's value on a row.
 *
 * A list of pairs rather than a dictionary on purpose: OmniSharp camel-cases dictionary keys on the
 * wire, turning `ENGLISH` into `eNGLISH`. A pair survives untouched.
 */
export interface LocValueDto {
    language: string;
    value: string;
}

/**
 * One row, addressed by position rather than by key.
 *
 * Position is the identity because credits files allow duplicate keys and their order is
 * significant - a key cannot name the row to edit. Text files are read the same way so both kinds
 * share one editing model.
 */
export interface LocRowDto {
    /** 0-based position in the file. */
    index: number;
    /** Empty for a blank spacer row, which credits files rely on. */
    key: string;
    values: LocValueDto[];
}

/**
 * @param contentHash Echoed back on every write for this file; it is how the server detects the
 *     file changed on disk since the client last read it.
 * @param ordered Whether row order is significant and duplicate keys are legal. True for credits.
 * @param canAddLanguage Whether this file can gain another language *column*. False for the
 *     single-language formats (.properties, .dat).
 * @param addLanguageCreatesFile Whether adding a language means creating a sibling file rather than
 *     a column - true for exactly the formats `canAddLanguage` is false for.
 */
export interface GetLocalisationRowsResult {
    rows: LocRowDto[];
    languages: string[];
    contentHash: string;
    category: string;
    ordered: boolean;
    error?: string | null;
    canAddLanguage: boolean;
    addLanguageCreatesFile: boolean;
}

// ── aet/applyCreditsBatch, aet/applyTranslationBatch ─────────────────────────

/**
 * @param failedIndex 0-based position of the *command* that failed - not a row.
 * @param newContentHash Hash of the text just written; echo it on the next save for this file.
 */
export interface ApplyLocalisationBatchResult {
    success: boolean;
    failedIndex?: number | null;
    error?: string | null;
    newContentHash?: string | null;
}

// ── aet/validateCreditsBatch, aet/validateTranslationBatch ───────────────────

/**
 * A validation finding, in the shape both validators share.
 *
 * The two servers report the same thing differently, which is why every locator here is optional:
 * credits address a finding by row (`index`), translations by entry (`key`), and a translation
 * finding carries `index` as well so the one row a key cannot name - a row with a blank key - is
 * still pointable at. A finding about the batch as a whole carries neither.
 *
 * `index` is always a ROW index. A failure that is about a staged command rather than about the
 * file names the command in its message and leaves `index` null; putting a command number here
 * would tint and jump to an unrelated row.
 */
export interface LocProblemDto {
    index?: number | null;
    key?: string | null;
    language?: string | null;
    severity: string;
    message: string;
}

export interface ValidateLocalisationBatchResult {
    problems: LocProblemDto[];
    error?: string | null;
}

// ── aet/getBaselineEntries ───────────────────────────────────────────────────

/**
 * @param translations Optional, though the server record declares it non-null: an entry with no
 *     translations at all serialises as an omitted field, and there is exactly one consumer, which
 *     already defaults it. Declaring it required would buy nothing and cost a crash on the whole
 *     baseline read the first time the server ships such an entry.
 */
export interface BaselineEntryDto {
    key: string;
    translations?: Record<string, string>;
}

export interface GetBaselineEntriesResult {
    entries: BaselineEntryDto[];
}

// ── aet/getLanguages ─────────────────────────────────────────────────────────

export interface GetLanguagesResult {
    languages: string[];
}

// ── aet/convertLocalisationFormat ────────────────────────────────────────────

/**
 * @param writtenPath The first written path. A convenience the server derives from
 *     `writtenPaths`; prefer the list, which is what a single-language target fills with one entry
 *     per language.
 * @param otherFilesInOldFormat Files left unloaded by a repoint, worth reporting while it is one
 *     line of the .pgproj away from being put back.
 */
export interface ConvertLocalisationFormatResult {
    writtenPath?: string | null;
    writtenPaths?: string[];
    projectFormatChanged: boolean;
    otherFilesInOldFormat: number;
    error?: string | null;
}

// ── aet/exportLocalisationToDat ──────────────────────────────────────────────

export interface ExportLocalisationToDatResult {
    writtenFiles: string[];
    error?: string | null;
}

// ── aet/createLocalisationLanguageFile ───────────────────────────────────────

export interface CreateLocalisationLanguageFileResult {
    writtenPath?: string | null;
    error?: string | null;
}

// ── aet/getRootLocalisationConfig ────────────────────────────────────────────

export interface GetRootLocalisationConfigResult {
    configured: boolean;
    type: string | null;
    directory: string | null;
}
