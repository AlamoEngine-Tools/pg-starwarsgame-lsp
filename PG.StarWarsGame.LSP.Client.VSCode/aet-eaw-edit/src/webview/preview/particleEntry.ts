// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the viewport remembers about one running particle system. Its own module because the viewport
// and the particle layer cut out of it both read it.

import type { AlamoParticleContent } from "../../protocol/modelPreview";
import type { LevelTagged } from "./levels";
import type { ParticleOrigin } from "./particleScene";
import type { ParticleSystemInstance } from "./particleRenderer";

/** What the viewport remembers about one running system, so it can be rebuilt on restart. */
export interface ParticleEntry {
    /** Its key in `ParticleRig.particleSystems`, so an entry alone can answer for itself. */
    id: string;
    system: AlamoParticleContent;
    attachToPartId?: string;
    attachBone?: string;
    /** The bone's index, which is what tells two same-named bones apart. */
    attachBoneIndex?: number;
    /**
     * What the game's own rules say: the opening rule and the damage state, together.
     *
     * Recomputed whenever a hardpoint blows up or is repaired, which is why the reader's own choice
     * cannot live here - it would be overwritten every time anything else on the model changed.
     */
    gateVisible: boolean;
    /** What the current ALT/LOD says, held apart again so a level change cannot clobber a toggle. */
    levelVisible: boolean;
    levels: LevelTagged;
    /** The owning object's uniform render scale, so a rebuild keeps it. */
    scaleFactor: number;
    /** Whether the MODEL declares this system or the Gameplay lens asked for it. */
    origin: ParticleOrigin;
    instance: ParticleSystemInstance;
}
