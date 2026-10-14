// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a particle file holds that the game does not apply, as problems for the panel.
//
// The server classifies each one after what the game does with it and attaches the id and the
// severity; here they only get the system's name in front, because a system asked for by an
// object - a death explosion - lands in the same list as the subject's own.

import {
    type GetParticleSystemResult,
    type PreviewProblem,
} from "../../protocol/modelPreview";

/** The problems for `result`, named after the file or object the system was asked for as. */
export function particleNotices(
    name: string,
    result: Pick<GetParticleSystemResult, "problems">,
): PreviewProblem[] {
    return (result.problems ?? []).map((problem) => ({
        ...problem,
        message: `${name}: ${problem.message}`,
        hardpointId: null,
    }));
}
