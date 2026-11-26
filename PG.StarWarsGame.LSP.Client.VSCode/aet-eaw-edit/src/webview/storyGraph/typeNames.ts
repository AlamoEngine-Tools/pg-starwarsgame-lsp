// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What an event or reward type is CALLED on its tile in the story graph's palette.
//
// A type has two names. The technical one is what XML writes - STORY_ELAPSED - and every event's
// starts the same way, so sixty-four tiles of them are sixty-four tiles of STORY_. The display name
// is the schema's label for the type - Time elapsed - which is shorter, says more, and is what a
// newcomer can read. The reader chooses which the tiles show
// (`aet-eaw-edit.editors.storyTypeNames`); the other one is in the tile's tooltip, and the search
// finds a type by either.

import type { TypeNameMode } from "../../shared/storyTypeNames";
import { compileMatcher, type SearchMode } from "../shared/searchPattern";

export { typeNameMode, type TypeNameMode } from "../../shared/storyTypeNames";

export interface TypeNames {
    /** Which of a type's two names the tiles show. */
    mode: TypeNameMode;

    /** The display name of each type the schema has named, by technical name. */
    labels: ReadonlyMap<string, string>;
}

function displayName(names: TypeNames, type: string): string | null {
    const label = names.labels.get(type)?.trim();
    return label === undefined || label === "" ? null : label;
}

/**
 * Whether a type answers a search pattern - by either name, whichever the tiles show. The pattern
 * is read in the search plate's mode, without case; an empty one answers everything.
 */
export function matchesType(
    names: TypeNames,
    type: string,
    pattern: string,
    mode: SearchMode = "text",
): boolean {
    const answers = compileMatcher(pattern.trim(), mode).test;

    return answers(type) || answers(displayName(names, type) ?? type);
}

/**
 * The name a tile shows, and whether that is the technical one.
 *
 * The display name where the reader asked for names and the schema has one. While a search is set
 * and matches ONLY the technical name, the tile shows that instead: typing `done` finds Movie
 * finished, and a result with nothing on it that says what was typed looks like a mistake.
 */
export function tileName(
    names: TypeNames,
    type: string,
    pattern: string,
    mode: SearchMode = "text",
): { text: string; technical: boolean } {
    const label = displayName(names, type);
    const q = pattern.trim();
    const answers = compileMatcher(q, mode).test;

    if (names.mode === "technical" || label === null) {
        return { text: type, technical: true };
    }

    if (q !== "" && !answers(label) && answers(type)) {
        return { text: type, technical: true };
    }

    return { text: label, technical: false };
}

/** The name the tile does NOT show, for its tooltip, or null for a type with one name only. */
export function otherName(
    names: TypeNames,
    type: string,
    pattern: string,
    mode: SearchMode = "text",
): string | null {
    const label = displayName(names, type);

    if (label === null) {
        return null;
    }

    return tileName(names, type, pattern, mode).technical ? label : type;
}

/**
 * A technical name in pieces, each ending after an underscore.
 *
 * A browser breaks a line at a space and not at an underscore, so a technical name on a tile either
 * overflows or is cut in the middle of a word. The tile puts a break opportunity between the pieces.
 */
export function breakPieces(name: string): string[] {
    return name.split(/(?<=_)/).filter((piece) => piece !== "");
}
