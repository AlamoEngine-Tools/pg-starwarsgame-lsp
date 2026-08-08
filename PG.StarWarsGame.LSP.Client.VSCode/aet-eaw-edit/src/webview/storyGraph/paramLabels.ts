// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Turning a schema param's prose description into a field label.
//
// The schema documents params in sentences, which is right for hover text and far too long for a
// 280px node body. These take the first useful phrase out of one.

import { StoryParamSchemaDto } from '../../protocol';

/**
 * A short row label from a param description ("Attacker faction." -> "Attacker faction"), or null
 * when the schema has nothing usable - the caller falls back to "Param N".
 */
export function shortParamLabel(schema: StoryParamSchemaDto | undefined): string | null {
    const description = schema?.description?.trim();
    if (!description) { return null; }

    // Cut at the first clause break: the rest of a description is qualification, not the name.
    const label = description.split(/[(,;.]/)[0].trim();
    return label.length ? label : null;
}

/**
 * A checkbox label from a boolean param's description.
 *
 * The checkbox already encodes the 0/1 mechanics, so the "1 = " prefix is noise beside it:
 * "1 = loop the movie; 0 = ..." becomes "Loop the movie". Null when the description has another
 * shape, which is the caller's cue to use the generic label instead.
 */
export function booleanParamLabel(description: string | null | undefined): string | null {
    if (!description) { return null; }

    const match = /^\s*[01]\s*=\s*([^;.(]+)/.exec(description);
    const text = match?.[1].trim();
    return text ? text[0].toUpperCase() + text.slice(1) : null;
}
