// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// ── aet/validateEditBatch, aet/applyEditBatch - Editing/EditBatchProtocol.cs ─────────────────────
// The visual editors' one way to write. The envelope is the same for every editor; the commands it
// carries are the editor's own.

/** The `editor` values, mirroring each composer's `EditorName`. */
export const EDIT_BATCH_EDITOR = {
    /** StoryEditBatchComposer.EditorName - the story graph. */
    story: "story",
    /** TranslationEditBatchComposer.EditorName - the localisation grid. */
    translation: "translation",
    /** CreditsEditBatchComposer.EditorName - the credits editor. */
    credits: "credits",
} as const;

export type EditBatchEditor =
    (typeof EDIT_BATCH_EDITOR)[keyof typeof EDIT_BATCH_EDITOR];

/**
 * One localisation file an editor shows, as the server listed it.
 *
 * @param contentHash In a request, the hash of the text the editor last read: a batch is staged
 *     against that text and is refused for a file that has changed since. In an answer, the hash
 *     the file has once the batch is written, which the editor's next batch has to carry.
 */
export interface EditBatchDocument {
    filePath: string;
    contentHash?: string | null;
}

/**
 * What a batch is about, as the editor knows it. The server resolves it to files; an editor never
 * says which file a change goes to.
 */
export interface EditBatchSubject {
    /** The story editor's campaign. */
    campaign?: string | null;
    /** The story editor's faction within it. */
    faction?: string | null;
    /**
     * The localisation editors' file, or the set of single-language files the grid shows as one
     * table. Which file of a set a command goes to is the server's decision.
     */
    documents?: EditBatchDocument[] | null;
}

/** The params of both requests. */
export interface EditBatchParams<TCommand> {
    editor: EditBatchEditor;
    subject: EditBatchSubject;
    /** The staged commands in the order they were made. */
    commands: readonly TCommand[];
}

/**
 * One problem of the pending state. A flat union: each editor fills the anchors it has.
 *
 * @param severity `error`, `warning` or `info`.
 * @param nodeId Story: the graph node the problem belongs to; null for a problem of the file.
 * @param side Story: `event` or `reward` when the problem lies in a parameter slot.
 * @param position Story: the 0-based parameter slot, when `side` is set.
 * @param key Localisation: the entry the problem is about; null for a problem of the whole file.
 * @param language Localisation: the language the problem is about.
 * @param index Localisation: the ROW the problem is about, 0-based in the file. A failure that is
 *     about a staged command names the command in its message and leaves this null; a command
 *     number here would tint and jump to an unrelated row.
 */
export interface EditBatchProblemDto {
    severity: string;
    message: string;
    nodeId?: string | null;
    side?: string | null;
    position?: number | null;
    uri?: string | null;
    line?: number | null;
    column?: number | null;
    key?: string | null;
    language?: string | null;
    index?: number | null;
}

/** @param error Why the batch could not be checked at all; the problems are then empty. */
export interface ValidateEditBatchResult {
    problems: EditBatchProblemDto[];
    error?: string | null;
}

/**
 * @param failedIndex 0-based index of the command that failed, when `success` is false.
 * @param edit Every file the batch changes, as one language-protocol WorkspaceEdit for the client
 *     to apply. Absent when the batch failed, and when it changes nothing.
 * @param documents Localisation: each file of the subject with the content hash it has once the
 *     edit is written.
 */
export interface ApplyEditBatchResult {
    success: boolean;
    failedIndex?: number | null;
    error?: string | null;
    edit?: unknown;
    documents?: EditBatchDocument[] | null;
}
