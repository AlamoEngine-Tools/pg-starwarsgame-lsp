// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The localisation editor's messages (#208): LocalisationEditorPanel and the two editors it hosts:
// translationEditor.tsx and creditsEditor.tsx, which share their half through loc/useLocPanel.ts.
//
// The staged commands are typed by the editor that builds them - a translation addresses an entry by
// key, a credits row by position - and the host never looks inside one: it mirrors the queue for the
// close-time prompt and hands each batch to the server as it is. So the host messages take the command
// type as a parameter; the host reads them as plain records, the editors as their own command type.

import type { StoredGeometry } from "../../shared/modalGeometry";
import type {
    BaselineEntryDto,
    LocProblemDto,
    LocRowDto,
} from "../localisation";

/** The bundle is listening; the file's rows can go now. */
export interface LocalisationEditorReady {
    type: "ready";
}

/** Read the file again: after a save, or an invalidation with nothing staged. */
export interface LocalisationEditorFetch {
    type: "fetch";
    /**
     * The page dropped its staged edits and needs the file's rows back. Without it an unchanged
     * file is not sent again, and the dropped edits would stay on screen.
     */
    discarded?: boolean;
}

/** The staged queue as it stands, mirrored so a dirty tab can offer to save when it closes. */
export interface LocalisationEditorPendingSync<C> {
    type: "pendingSync";
    commands: C[];
}

/** Check what is staged without writing it; answered with `problems`. */
export interface LocalisationEditorValidateBatch<C> {
    type: "validateBatch";
    commands: C[];
}

/** Write what is staged; answered with `saveResult`. */
export interface LocalisationEditorSaveBatch<C> {
    type: "saveBatch";
    commands: C[];
}

/** What the layers below this file say; answered with `baselineRows`. */
export interface LocalisationEditorRequestBaseline {
    type: "requestBaseline";
}

/** The credits editor's staged rows, for the crawl preview: opened beside it, or updated if open. */
export interface LocalisationEditorCrawlRows {
    type: "crawlRows";
    /** True opens the preview beside the editor; false only updates one that is already open. */
    open: boolean;
    rows: LocRowDto[];
    languages: string[];
    language: string;
}

/** Rewrite this file in another format, keeping the original. */
export interface LocalisationEditorConvertFormat {
    type: "convertFormat";
    targetFormat: string;
}

/** Add a language by creating the sibling file that holds it. */
export interface LocalisationEditorAddLanguageFile {
    type: "addLanguageFile";
    language: string;
}

/** A dialog the reader moved or resized, kept for the next time it opens. */
export interface LocalisationEditorSaveDialogGeometry {
    type: "saveDialogGeometry";
    id: string;
    geometry: StoredGeometry;
}

/** Export the localisation project to a .dat file. */
export interface LocalisationEditorExportDat {
    type: "exportDat";
}

export type LocalisationEditorToHost<C = Record<string, unknown>> =
    | LocalisationEditorReady
    | LocalisationEditorFetch
    | LocalisationEditorPendingSync<C>
    | LocalisationEditorValidateBatch<C>
    | LocalisationEditorSaveBatch<C>
    | LocalisationEditorRequestBaseline
    | LocalisationEditorCrawlRows
    | LocalisationEditorConvertFormat
    | LocalisationEditorAddLanguageFile
    | LocalisationEditorSaveDialogGeometry
    | LocalisationEditorExportDat;

/** The file, merged across the set it was opened on. */
export interface LocalisationEditorRows {
    type: "rows";
    rows: LocRowDto[];
    languages: string[];
    category: string;
    /** Row order is significant and duplicate keys are legal: a credits file. */
    ordered: boolean;
    canAddLanguage: boolean;
    addLanguageCreatesFile: boolean;
    /** Why the file is shown and not edited, as each disabled control says it; null when it is edited. */
    readOnlyReason: string | null;
    /** The language shown alone at first, or null for all of them. */
    focusLanguage: string | null;
    /** The engine's supported languages: the only ones worth offering to add. */
    supportedLanguages: string[];
    /** Where each dialog belongs, sent with the file so a dialog opens in its place. */
    dialogGeometry: Record<string, StoredGeometry>;
}

/** The file could not be read; shown where the table would have been. */
export interface LocalisationEditorError {
    type: "error";
    message: string;
}

/** The result of a Validate; an empty list with an error when the check itself failed. */
export interface LocalisationEditorProblems {
    type: "problems";
    problems: LocProblemDto[];
    error: string | null;
}

/** The outcome of a Save; on success the editor drops its queue and reads the file again. */
export interface LocalisationEditorSaveResult {
    type: "saveResult";
    success: boolean;
    /** The first command the server refused, when it named one. */
    failedIndex?: number | null;
}

/** The tab was re-aimed at one language of its set, or back at all of them (null). */
export interface LocalisationEditorFocusLanguage {
    type: "focusLanguage";
    language: string | null;
}

/** The file may have changed on disk; a tab with nothing staged reads it again. */
export interface LocalisationEditorInvalidate {
    type: "invalidate";
    dirty: boolean;
}

/** The title bar asked for the crawl preview: played full screen, or opened beside. */
export interface LocalisationEditorRequestCrawlRows {
    type: "requestCrawlRows";
    fullScreen: boolean;
}

/** What the layers below this file already say, so the grid can hide rows that only repeat it. */
export interface LocalisationEditorBaselineRows {
    type: "baselineRows";
    entries: BaselineEntryDto[];
}

/** Another language file of this project, offered as a starting point for an empty credits file. */
export interface LocalisationSeedSource {
    filePath: string;
    label: string;
    language: string;
    rowCount: number;
    rows: LocRowDto[];
}

/** The project's other language files with rows, sent only while this credits file is empty. */
export interface LocalisationEditorSeedSources {
    type: "seedSources";
    sources: LocalisationSeedSource[];
}

export type LocalisationEditorToWebview =
    | LocalisationEditorRows
    | LocalisationEditorError
    | LocalisationEditorProblems
    | LocalisationEditorSaveResult
    | LocalisationEditorFocusLanguage
    | LocalisationEditorInvalidate
    | LocalisationEditorRequestCrawlRows
    | LocalisationEditorBaselineRows
    | LocalisationEditorSeedSources;
