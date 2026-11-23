// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a visual editor says when a batch did not come to a clean save (#339). Said once here so the
// editors word the same outcome the same way; a batch the server refused is the editor's own to
// report, since only it knows how to name the change that was refused.

import * as path from "path";

import * as vscode from "vscode";

import type { EditBatchSave } from "./lsp/editBatchClient";
import type { LspFailure } from "./lsp/lspGateway";

/**
 * Says why a request got no answer, as the gateway's own helpers word it.
 *
 * @param context What was being done, so the reader is told which action failed.
 */
export function reportUnreached(
    outcome: { readonly reason: LspFailure; readonly message: string },
    context: string,
): void {
    if (outcome.reason === "offline") {
        void vscode.window.showWarningMessage(outcome.message);
    } else {
        void vscode.window.showErrorMessage(
            vscode.l10n.t("EaWEdit: {0}: {1}", context, outcome.message),
        );
    }
}

/**
 * Says what a save came to, unless it is on disk or the server refused it: a server that was not
 * reached, an edit the editor did not take, or files that hold the change but were not written.
 *
 * @param context What was being done, so the reader is told which action failed.
 */
export function reportSave(saved: EditBatchSave, context: string): void {
    if (saved.saved) {
        if (saved.unsaved.length > 0) {
            void vscode.window.showWarningMessage(
                vscode.l10n.t(
                    "EaWEdit: Not written to disk - {0}",
                    saved.unsaved.map((file) => path.basename(file)).join(", "),
                ),
            );
        }
        return;
    }

    if (saved.reason === "offline" || saved.reason === "failed") {
        reportUnreached(
            { reason: saved.reason, message: saved.message ?? "" },
            context,
        );
    } else if (saved.reason === "notApplied") {
        void vscode.window.showErrorMessage(
            vscode.l10n.t(
                "EaWEdit: {0}: {1}",
                context,
                vscode.l10n.t("The changes could not be applied."),
            ),
        );
    }
}
