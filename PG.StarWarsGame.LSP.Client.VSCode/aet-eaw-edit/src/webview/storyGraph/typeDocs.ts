// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the schema says about an event or reward type, as the palette shows it: the mark in the
// corner of the type's tile, and the lines of the documentation that mark opens.
//
// A tile has room for a name. Everything else the schema knows - what the type does, what is wrong
// with it, what each parameter holds - is one press away, in a flyout beside the dock. The pill
// that opens it is also the tile's only mark: a type the engine does not run and a type nobody has
// verified are worth knowing BEFORE the type is dragged onto the canvas, and a second mark for that
// would be a second corner taken on a tile fitted to two lines of text.

import * as l10n from "@vscode/l10n";

import type { StoryParamSchemaDto } from "../../protocol";
import type { BadgeSeverity } from "../shared/badges";
import { noteBadge, type StoryNote } from "./typeNotes";

/** The prose the schema carries for one type. Its parameters travel beside it. */
export interface TypeDoc {
    description: string | null;

    /** Ranked by the server, worst first. */
    notes: readonly StoryNote[];
}

/** How loud one note is. A kind this build has never heard of is information. */
export function noteSeverity(note: StoryNote): BadgeSeverity {
    switch (note.kind) {
        case "BuggedInEngine":
            return "error";
        case "Deprecated":
        case "Untested":
            return "warning";
        default:
            return "info";
    }
}

/** The severity of a tile's pill: the worst note's, which the server puts first. */
export function pillSeverity(
    notes: readonly StoryNote[] | undefined,
): BadgeSeverity {
    const worst = notes?.[0];
    return worst === undefined ? "info" : noteSeverity(worst);
}

/**
 * One note as a line: what kind of note it is, then the note.
 *
 * The words are the node's own (noteBadge), so a type reads the same on the canvas and in its
 * documentation. A node drops the version a type appeared in as noise; here it is said.
 */
export function noteLine(note: StoryNote): string {
    if (note.kind === "Since") {
        return l10n.t("Since {0}", note.value?.trim() ?? "");
    }

    return noteBadge([note])?.title ?? note.kind;
}

/** What a parameter holds: its value type, and what it refers to where it refers to something. */
export function paramKind(
    param: Pick<
        StoryParamSchemaDto,
        "valueType" | "referenceType" | "enumName"
    >,
): string {
    const target = param.referenceType ?? param.enumName;
    return target ? `${param.valueType} - ${target}` : param.valueType;
}
