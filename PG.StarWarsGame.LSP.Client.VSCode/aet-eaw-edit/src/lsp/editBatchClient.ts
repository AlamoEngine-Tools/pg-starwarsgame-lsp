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
    ApplyEditBatchResult,
    EditBatchEditor,
    EditBatchParams,
    EditBatchSubject,
    ValidateEditBatchResult,
} from "../protocol/editBatch";

/** The slice of the gateway this needs - so a test can supply a function. */
export interface EditBatchRequests {
    request<T>(method: string, params?: unknown): Promise<LspOutcome<T>>;
}

/** What became of an edit handed to the editor. */
export interface AppliedEdit {
    /** Whether the editor took the edit. When it did not, nothing changed. */
    readonly applied: boolean;
    /** The files the edit changed that could not be saved afterwards, by path. */
    readonly unsaved: readonly string[];
}

/** The editor's side of a save. `vscode.workspace` in production. */
export interface EditWorkspace {
    /**
     * Applies an edit as the server composed it and saves every file it changed.
     *
     * @param edit A language-protocol WorkspaceEdit.
     */
    applyAndSave(edit: unknown): Promise<AppliedEdit>;
}

/** Why a batch was not saved. */
export type EditBatchFailure =
    /** The server is not running - nothing was sent. */
    | "offline"
    /** The request rejected, or the server answered with an error. */
    | "failed"
    /** The server read the batch and refused it; `failedIndex` names the command when one did it. */
    | "refused"
    /** The server composed the edit and the editor did not take it. */
    | "notApplied";

/** What a save produced. */
export type EditBatchSave =
    | {
          readonly saved: true;
          /** The files that hold the change in the editor but could not be written, by path. */
          readonly unsaved: readonly string[];
      }
    | {
          readonly saved: false;
          readonly reason: EditBatchFailure;
          /** What the gateway or the server said, when either said anything. */
          readonly message?: string;
          /** 0-based index of the command the server refused. */
          readonly failedIndex?: number;
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
        return this._requests.request<ValidateEditBatchResult>(
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
        const outcome = await this._requests.request<ApplyEditBatchResult>(
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
        if (composed.edit === undefined || composed.edit === null) {
            return { saved: true, unsaved: [] };
        }

        const applied = await this._workspace.applyAndSave(composed.edit);
        return applied.applied
            ? { saved: true, unsaved: applied.unsaved }
            : { saved: false, reason: "notApplied" };
    }
}

function paramsOf<TCommand>(
    editor: EditBatchEditor,
    subject: EditBatchSubject,
    commands: readonly TCommand[],
): EditBatchParams<TCommand> {
    return { editor, subject, commands };
}
