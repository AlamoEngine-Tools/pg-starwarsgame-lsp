// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a particle file holds that the game does not apply, as problems for the panel.
//
// The server classifies each one after what the game does with it and attaches the id and the
// severity; here they only get the file's name in front, because a system asked for by an object -
// a death explosion - lands in the same list as the subject's own. The FILE's name, not the name it
// was asked for as: two damage stages of one model ask for p_rb_station_damage_ALT1 and _ALT2, and
// that is one file with one set of notes (#394).

import {
    type GetParticleSystemResult,
    type PreviewProblem,
} from "../../protocol/modelPreview";

/** The problems for `result`, named after the file the server read - or, failing that, `name`. */
export function particleNotices(
    name: string,
    result: Pick<GetParticleSystemResult, "problems" | "file">,
): PreviewProblem[] {
    const file = result.file ?? name;
    return (result.problems ?? []).map((problem) => ({
        ...problem,
        message: `${file}: ${problem.message}`,
        hardpointId: null,
    }));
}

/**
 * `current` with whichever of `incoming` it does not already say: a file asked for by several
 * proxies is said once. The same array back when nothing is new, so React keeps the state it has.
 * Case-blind, as the game's file names are.
 */
export function withParticleNotices(
    current: PreviewProblem[],
    incoming: PreviewProblem[],
): PreviewProblem[] {
    const said = new Set(current.map(keyOf));
    const added = incoming.filter((problem) => {
        const key = keyOf(problem);
        if (said.has(key)) {
            return false;
        }
        said.add(key);
        return true;
    });

    return added.length === 0 ? current : [...current, ...added];
}

function keyOf(problem: PreviewProblem): string {
    return `${problem.diagnosticId ?? ""}\n${problem.message.toLowerCase()}`;
}
