// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the two text editors put above their table and at the top of their dock.
//
// The page toolbar carries what the page does to its file: Save and the verdict. The dock's
// header keeps the one thing it reports - why nothing here can be changed - and is not drawn at all
// while there is nothing to report.
//
// Shared because the two editors had both byte-for-byte identical, and because they read out state
// (how many edits are staged, how the last validation went) that must be phrased the same way in
// both or the same situation looks like two different ones.

import * as l10n from "@vscode/l10n";
import { LocProblem, ValidationState } from "./useLocPanel";
import { useReadOnly } from "./readOnly";
import { validateTitle } from "./validateState";
import { Icon } from "../shared/Icon";
import { PageToolbar } from "../shared/PageToolbar";
import { SeverityTag } from "../shared/SeverityTag";

export function LocPageToolbar(props: {
    /** The staged queue; its length is the count beside Save and disables the button when empty. */
    queue: readonly unknown[];
    problems: readonly LocProblem[];
    validation: ValidationState;
    onSave: () => void;
    onValidate: () => void;
}): React.JSX.Element {
    const readOnly = useReadOnly();
    return (
        <PageToolbar
            save={{
                title: l10n.t("Save - write all staged changes to the file"),
                pending: props.queue.length,
                disabled: props.queue.length === 0 || readOnly !== null,
                disabledReason:
                    readOnly !== null
                        ? l10n.t("Save - {0}", readOnly)
                        : l10n.t("Save - nothing is staged"),
                onSave: props.onSave,
            }}
            verdict={
                <SeverityTag
                    severity={props.validation}
                    count={props.problems.length}
                    title={validateTitle(
                        props.validation,
                        props.problems.length,
                        props.queue.length,
                    )}
                    onClick={props.onValidate}
                />
            }
        />
    );
}

/**
 * State, where the dock keeps it: why nothing here can be changed, in full. The controls each say
 * it too, but a tile has room for half of it. Nothing while the file can be changed.
 */
export function LocDockHeader(): React.JSX.Element | null {
    const readOnly = useReadOnly();
    return readOnly === null ? null : (
        <span className="read-only-note" title={readOnly}>
            <Icon name="readOnly" />
            {readOnly}
        </span>
    );
}
