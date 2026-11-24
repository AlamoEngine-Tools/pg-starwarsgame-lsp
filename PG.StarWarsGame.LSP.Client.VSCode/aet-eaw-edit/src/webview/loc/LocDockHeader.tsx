// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The dock's title row: Save pinned left, Validate a soft severity pill pinned right, both
// icon-first - laid out like the story graph editor's, which is where the shape came from.
//
// Shared because the two editors had it byte-for-byte identical, and because it reads out state
// (how many edits are staged, how the last validation went) that must be phrased the same way in
// both or the same situation looks like two different ones.

import * as l10n from "@vscode/l10n";
import { LocProblem, ValidationState } from "./useLocPanel";
import { useReadOnly } from "./readOnly";
import { validateTitle } from "./validateState";
import { IconButton } from "../shared/Button";
import { Icon } from "../shared/Icon";
import { SeverityTag } from "../shared/SeverityTag";

export function LocDockHeader(props: {
    /** The staged queue; its length is the badge on Save and disables the button when empty. */
    queue: readonly unknown[];
    problems: readonly LocProblem[];
    validation: ValidationState;
    onSave: () => void;
    onValidate: () => void;
}): React.JSX.Element {
    const readOnly = useReadOnly();
    return (
        <>
            <IconButton
                icon="save"
                className={`header-left${props.queue.length > 0 ? " active" : ""}`}
                title={l10n.t("Save - write all staged changes to the file")}
                badge={props.queue.length > 0 ? ` ${props.queue.length}` : ""}
                disabled={props.queue.length === 0 || readOnly !== null}
                disabledReason={
                    readOnly !== null
                        ? l10n.t("Save - {0}", readOnly)
                        : l10n.t("Save - nothing is staged")
                }
                onClick={props.onSave}
            />
            {/* State, where the dock keeps it: why nothing here can be changed, in full. The
                controls each say it too, but a tile has room for half of it. */}
            {readOnly !== null && (
                <span className="read-only-note" title={readOnly}>
                    <Icon name="readOnly" />
                    {readOnly}
                </span>
            )}
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
        </>
    );
}
