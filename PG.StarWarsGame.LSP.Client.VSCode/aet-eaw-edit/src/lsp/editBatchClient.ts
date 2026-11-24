// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The visual editors' one way to write (#339).
//
// An editor stages its changes and sends them as one batch. Checking a batch is one request. Saving
// one is three steps, and they are the same for every editor: the server composes the batch into
// one edit, the editor applies that edit, and the files the edit touched are saved. The edit goes
// through the editor rather than onto the disk so the save is one step of its undo history, and the
// files are saved so that Save means what it says.
//
// The server cannot apply the edit itself and still answer: applying it makes the editor report the
// change, and that report cancels the request that is waiting for the answer.
//
// Kept free of `vscode` so it can be unit-tested: what it needs from the editor is one method, and
// the extension host supplies the real one.

import type { LspOutcome } from "./lspGateway";
import type {
    EditBatchDocument,
    EditBatchEditor,
    EditBatchParams,
    EditBatchSubject,
    ValidateEditBatchResult,
} from "../protocol/editBatch";
import type { RequestParams, RequestResult } from "../protocol/requests";

/** The slice of the gateway this needs - so a test can supply a function. */
export interface EditBatchRequests {
    request<M extends "aet/validateEditBatch" | "aet/applyEditBatch">(
        method: M,
        params: RequestParams<M>,
    ): Promise<LspOutcome<RequestResult<M>>>;
}

/**
 * Why the window did not take an edit.
 *
 * - `refused`: VS Code turned the edit down, or no server runs to read it with.
 * - `changedSince`: a document moved on from the version the edit was composed from.
 * - `unsavedChanges`: the edit was composed from a saved file whose document has unsaved changes.
 */
export type EditRefusal = "refused" | "changedSince" | "unsavedChanges";

/** What became of an edit handed to the editor. */
export type AppliedEdit =
    | {
          readonly applied: true;
          /** The files the edit changed that could not be saved afterwards, by path. */
          readonly unsaved: readonly string[];
      }
    | {
          /** Nothing changed. */
          readonly applied: false;
          readonly reason: EditRefusal;
          /** The files the refusal is about, by path; empty when it is about none in particular. */
          readonly files: readonly string[];
      };

/** The editor's side of a save. `vscode.workspace` in production. */
export interface EditWorkspace {
    /**
     * Applies an edit as the server composed it and saves every file it changed.
     *
     * @param edit A language-protocol WorkspaceEdit.
     */
    applyAndSave(edit: unknown): Promise<AppliedEdit>;
}

/** What the window holds for a file it has open. */
export interface OpenDocumentState {
    readonly version: number;
    /** Whether the document has changes that are not saved. */
    readonly dirty: boolean;
}

/**
 * The files of an edit the window must not take as they stand, or undefined when it may take all.
 *
 * Each replacement in an edit says what the server composed it from, the way the protocol provides
 * for: a version when it read the open document, none when it read the saved file. A replacement
 * composed from a version the document has left would undo what was typed since, and one composed
 * from the saved file would overwrite changes the window holds and the server never saw - both
 * replace the whole text. Either is refused, and nothing is applied.
 *
 * @param stateOf The window's document for a URI, or undefined when it holds none.
 */
export function unwritable(
    edit: { documentChanges?: readonly unknown[] | null },
    stateOf: (uri: string) => OpenDocumentState | undefined,
): { reason: "changedSince" | "unsavedChanges"; uris: string[] } | undefined {
    const changedSince: string[] = [];
    const unsavedChanges: string[] = [];

    for (const change of edit.documentChanges ?? []) {
        const target = (
            change as {
                textDocument?: { uri?: string; version?: number | null };
            }
        ).textDocument;
        if (typeof target?.uri !== "string") {
            continue;
        }

        const state = stateOf(target.uri);
        if (typeof target.version === "number") {
            // Composed from an open document: it has to be that document still, at that version.
            if (state?.version !== target.version) {
                changedSince.push(target.uri);
            }
        } else if (state?.dirty) {
            unsavedChanges.push(target.uri);
        }
    }

    if (changedSince.length > 0) {
        return { reason: "changedSince", uris: changedSince };
    }
    return unsavedChanges.length > 0
        ? { reason: "unsavedChanges", uris: unsavedChanges }
        : undefined;
}

/** Why a batch was not saved. */
export type EditBatchFailure =
    /** The server is not running - nothing was sent. */
    | "offline"
    /** The request rejected, or the server answered with an error. */
    | "failed"
    /** The server read the batch and refused it; `failedIndex` names the command when one did it. */
    | "refused"
    /** The server composed the edit and the window did not take it. */
    | "notApplied"
    /** The window did not take the edit: a document moved on while it was composed. */
    | "changedSince"
    /** The window did not take the edit: a file it was composed from has unsaved changes. */
    | "unsavedChanges";

/** What a save produced. */
export type EditBatchSave =
    | {
          readonly saved: true;
          /** The files that hold the change in the editor but could not be written, by path. */
          readonly unsaved: readonly string[];
          /** Localisation: each file of the subject with the content hash it has now. */
          readonly documents: readonly EditBatchDocument[];
      }
    | {
          readonly saved: false;
          readonly reason: EditBatchFailure;
          /** What the gateway or the server said, when either said anything. */
          readonly message?: string;
          /** 0-based index of the command the server refused. */
          readonly failedIndex?: number;
          /** The files the window would not write, by path. */
          readonly files?: readonly string[];
      };

export class EditBatchClient {
    /**
     * @param _requests Requests to the server of the project the editor belongs to.
     * @param _workspace Where a composed edit is applied and saved.
     */
    constructor(
        private readonly _requests: EditBatchRequests,
        private readonly _workspace: EditWorkspace,
    ) {}

    /** Checks a batch without writing. Never throws and never shows anything - the caller decides. */
    validate<TCommand>(
        editor: EditBatchEditor,
        subject: EditBatchSubject,
        commands: readonly TCommand[],
    ): Promise<LspOutcome<ValidateEditBatchResult>> {
        return this._requests.request(
            "aet/validateEditBatch",
            paramsOf(editor, subject, commands),
        );
    }

    /**
     * Saves a batch: composed by the server, applied by the editor, then written.
     *
     * Never throws and never shows anything - the caller decides what to say. A batch the server
     * composes to no change at all is saved: there is nothing to apply and nothing to write.
     */
    async save<TCommand>(
        editor: EditBatchEditor,
        subject: EditBatchSubject,
        commands: readonly TCommand[],
    ): Promise<EditBatchSave> {
        const outcome = await this._requests.request(
            "aet/applyEditBatch",
            paramsOf(editor, subject, commands),
        );
        if (!outcome.ok) {
            return {
                saved: false,
                reason: outcome.reason,
                message: outcome.message,
            };
        }

        const composed = outcome.value;
        if (!composed.success) {
            return {
                saved: false,
                reason: "refused",
                message: composed.error ?? undefined,
                failedIndex: composed.failedIndex ?? undefined,
            };
        }

        const documents = composed.documents ?? [];
        if (composed.edit === undefined || composed.edit === null) {
            return { saved: true, unsaved: [], documents };
        }

        const applied = await this._workspace.applyAndSave(composed.edit);
        if (applied.applied) {
            return { saved: true, unsaved: applied.unsaved, documents };
        }
        return applied.reason === "refused"
            ? { saved: false, reason: "notApplied" }
            : { saved: false, reason: applied.reason, files: applied.files };
    }
}

function paramsOf<TCommand>(
    editor: EditBatchEditor,
    subject: EditBatchSubject,
    commands: readonly TCommand[],
): EditBatchParams<TCommand> {
    return { editor, subject, commands };
}
