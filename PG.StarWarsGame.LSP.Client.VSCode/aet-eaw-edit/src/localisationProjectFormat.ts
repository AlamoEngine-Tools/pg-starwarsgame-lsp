// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as l10n from "@vscode/l10n";
import type { SetLocalisationProjectFormatResult } from "./protocol/localisation";

/**
 * The choices and the outcome wording for "Set Localisation Project Format" (#121), kept free of the
 * VS Code API so the command, the navigator button that runs it, and the tests all read one thing.
 */

export interface FormatChoice {
    /** Sent to the server as-is. */
    format: string;
    label: string;
    description: string;
    /** Whether the project already declares this format. */
    current: boolean;
}

export interface FormatOutcome {
    kind: "info" | "warning" | "error";
    text: string;
}

/**
 * Every format a `.pgproj` may declare. DAT is here although the conversion command leaves it out:
 * converting writes files, this only changes which ones the project loads, and switching to the DAT
 * files a project already has is what #121 asked for.
 */
const FORMATS: readonly { format: string; description: string }[] = [
    {
        format: "DAT",
        description: l10n.t("The game's binary files (.dat), one per language"),
    },
    { format: "CSV", description: l10n.t("Comma-separated values (.csv)") },
    { format: "XML", description: l10n.t("eaw-translation v1 XML (.xml)") },
    {
        format: "NLS",
        description: l10n.t(
            "Java-style properties (.properties), one per language",
        ),
    },
];

export function formatChoices(current: string | null): FormatChoice[] {
    const declared = current?.toUpperCase() ?? null;
    return FORMATS.map(({ format, description }) => ({
        format,
        label: format,
        description:
            format === declared
                ? l10n.t("Current - {0}", description)
                : description,
        current: format === declared,
    }));
}

export function formatOutcome(
    result: SetLocalisationProjectFormatResult,
): FormatOutcome {
    if (result.error) {
        return { kind: "error", text: l10n.t("EaWEdit: {0}", result.error) };
    }

    const format = result.format ?? "";
    if (!result.changed) {
        return {
            kind: "info",
            text: l10n.t("EaWEdit: The project already loads {0}.", format),
        };
    }

    const from = result.previousFormat
        ? ` instead of ${result.previousFormat.toUpperCase()}`
        : "";
    if (result.filesInFormat === 0) {
        return {
            kind: "warning",
            text: l10n.t(
                "EaWEdit: The project now loads {0}{1}, but there are no {2} files in its localisation directory, so no text is loaded. No files were converted.",
                format,
                from,
                format,
            ),
        };
    }

    const files =
        result.filesInFormat === 1 ? "1 file" : `${result.filesInFormat} files`;
    return {
        kind: "info",
        text: l10n.t(
            "EaWEdit: The project now loads {0}{1}: {2}. No files were converted.",
            format,
            from,
            files,
        ),
    };
}
