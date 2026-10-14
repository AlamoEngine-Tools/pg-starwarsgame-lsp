// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a particle file holds that the game does not read, as problems for the panel.
//
// The server skips an unknown chunk or property exactly as the game does and reports each one
// by name; here they become warnings, not errors, because the system loaded and plays - the
// file merely carries a setting nothing applies.

import {
    type AlamoParticleContent,
    type PreviewProblem,
} from "../../protocol/modelPreview";

/** The problems for `system`, named after the file or object it was asked for as. */
export function particleNotices(
    name: string,
    system: Pick<AlamoParticleContent, "notices">,
): PreviewProblem[] {
    return (system.notices ?? []).map((notice) => ({
        severity: "warning",
        message: `${name}: ${notice}`,
        hardpointId: null,
    }));
}
