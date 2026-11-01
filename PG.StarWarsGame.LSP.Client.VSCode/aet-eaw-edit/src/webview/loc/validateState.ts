// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How the Validate control describes itself.
//
// The file is checked as soon as it opens, so this reports something true from the start rather
// than sitting inert until it is pressed. It is always pressable: a file can have problems with
// nothing staged at all - a duplicate key it was saved with - and re-checking is always a
// reasonable thing to ask for.

import * as l10n from "@vscode/l10n";

import { ValidationState } from "./useLocPanel";

/**
 * The state the tag shows: the highest level anything reported.
 *
 * The same rule the story graph editor's Validate button uses, so the two controls mean the same
 * thing - error beats warning beats info beats clean. Shared rather than reimplemented, because
 * two copies of
 * a precedence rule drift and the whole point is that they match.
 *
 * An unrecognised severity is treated as a warning: a level this does not know about must still be
 * visible, and neither reading it as clean nor softening it to information would show it honestly.
 */
export function worstSeverity(
    problems: readonly { severity: string }[],
): ValidationState {
    if (problems.some((p) => p.severity === "error")) {
        return "error";
    }
    if (problems.some((p) => p.severity !== "info")) {
        return "warning";
    }
    if (problems.length > 0) {
        return "info";
    }
    return "ok";
}

export function severityIconFor(validation: ValidationState): string {
    return validation === "unvalidated"
        ? "question"
        : validation === "error"
          ? "error"
          : validation === "warning"
            ? "warning"
            : validation === "info"
              ? "info"
              : "check";
}

/**
 * What the control says on hover: the verdict when there is one, and what pressing it will do.
 *
 * Never states a verdict while edits are staged - the last result described a document that no
 * longer exists.
 */
export function validateTitle(
    validation: ValidationState,
    problemCount: number,
    pendingCount: number,
): string {
    if (pendingCount > 0 || validation === "unvalidated") {
        return l10n.t(
            "Validate - check the file and everything staged, without writing.",
        );
    }

    if (validation === "ok") {
        return l10n.t("Validate - no problems found. Press to check again.");
    }

    // Each level is named for what it is. Calling a case-only key difference or a heading with no
    // entries a "problem" overstates both - the game reads either perfectly well - and a tag that
    // cries problem at valid files is one people learn to ignore. Whole sentences, one per count,
    // so each reads right in any language.
    if (validation === "warning") {
        return problemCount === 1
            ? l10n.t("Validate - 1 warning found. Press to check again.")
            : l10n.t(
                  "Validate - {0} warnings found. Press to check again.",
                  problemCount,
              );
    }
    if (validation === "info") {
        return problemCount === 1
            ? l10n.t("Validate - 1 note found. Press to check again.")
            : l10n.t(
                  "Validate - {0} notes found. Press to check again.",
                  problemCount,
              );
    }
    return problemCount === 1
        ? l10n.t("Validate - 1 problem found. Press to check again.")
        : l10n.t(
              "Validate - {0} problems found. Press to check again.",
              problemCount,
          );
}
