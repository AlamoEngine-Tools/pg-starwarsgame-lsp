// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Asking before an editor drops its staged changes (#222).
//
// One question for every editor that stages changes, asked by the host because only the host can
// raise a dialog over the whole window. The reader can answer it for good: "Discard & Don't Ask
// Again" turns the setting off, and the setting turns the question back on.

import * as vscode from "vscode";

/** The setting that keeps the question; on unless the reader turned it off. */
export const CONFIRM_DISCARD_SETTING = "aet-eaw-edit.editors.confirmDiscard";

/**
 * Whether `count` staged changes may be dropped: the reader's answer, or yes without asking once
 * the reader has said not to be asked again.
 *
 * A dialog that could not be shown is a no. Discarding is the one answer that loses work, so it is
 * never the answer nobody gave.
 */
export async function confirmDiscard(count: number): Promise<boolean> {
    const settings = vscode.workspace.getConfiguration();
    if (settings.get<boolean>(CONFIRM_DISCARD_SETTING, true) === false) {
        return true;
    }

    const discard = vscode.l10n.t("Discard");
    const never = vscode.l10n.t("Discard & Don't Ask Again");
    let choice: string | undefined;
    try {
        choice = await vscode.window.showWarningMessage(
            vscode.l10n.t("Discard all staged changes?"),
            {
                modal: true,
                detail: vscode.l10n.t(
                    "Staged changes: {0}. Nothing of them was written; the files keep what was last saved.",
                    count,
                ),
            },
            discard,
            never,
        );
    } catch {
        return false;
    }

    if (choice === never) {
        await settings.update(
            CONFIRM_DISCARD_SETTING,
            false,
            vscode.ConfigurationTarget.Global,
        );
    }
    return choice === discard || choice === never;
}
