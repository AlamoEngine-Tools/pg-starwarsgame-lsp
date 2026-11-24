// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The editor's side of a visual editor's save (#339): an edit the server composed is applied through
// VS Code, so it is one step of the files' undo history, and the files it changed are then saved, so
// the change is on disk when Save returns.

import * as vscode from "vscode";
import type {
    Protocol2CodeConverter,
    WorkspaceEdit,
} from "vscode-languageclient";

import {
    type AppliedEdit,
    type EditWorkspace,
    type OpenDocumentState,
    unwritable,
} from "./lsp/editBatchClient";

/**
 * @param converter The running language client's converter from the protocol's edit to VS Code's,
 *     looked up per call: a client is replaced on every restart. Undefined while none runs.
 */
export function createEditWorkspace(
    converter: () => Protocol2CodeConverter | undefined,
): EditWorkspace {
    return {
        async applyAndSave(edit: unknown): Promise<AppliedEdit> {
            const convert = converter();
            if (convert === undefined) {
                return { applied: false, reason: "refused", files: [] };
            }

            const composed = edit as WorkspaceEdit;
            const stale = unwritable(composed, (uri) =>
                stateOf(convert.asUri(uri)),
            );
            if (stale !== undefined) {
                return {
                    applied: false,
                    reason: stale.reason,
                    files: stale.uris.map((uri) => convert.asUri(uri).fsPath),
                };
            }

            const converted = await convert.asWorkspaceEdit(composed);
            if (!(await vscode.workspace.applyEdit(converted))) {
                return { applied: false, reason: "refused", files: [] };
            }

            // Every file the edit changed has text in it - a created file gets its content as an
            // insert - so the text entries are the whole list of files to write.
            const unsaved: string[] = [];
            for (const [uri] of converted.entries()) {
                const document = await vscode.workspace.openTextDocument(uri);
                if (document.isDirty && !(await document.save())) {
                    unsaved.push(uri.fsPath);
                }
            }
            return { applied: true, unsaved };
        },
    };
}

/** What the window holds for a file: nothing, or a document at a version that is saved or not. */
function stateOf(uri: vscode.Uri): OpenDocumentState | undefined {
    const wanted = uri.toString();
    const document = vscode.workspace.textDocuments.find(
        (open) => open.uri.toString() === wanted,
    );
    return document === undefined
        ? undefined
        : { version: document.version, dirty: document.isDirty };
}
