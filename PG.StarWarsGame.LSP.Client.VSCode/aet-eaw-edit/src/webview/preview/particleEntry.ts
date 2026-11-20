// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What the viewport remembers about one running particle system. Its own module because the viewport
// and the particle layer cut out of it both read it.

import type { AlamoParticleContent } from "../../protocol/modelPreview";
import type { LevelTagged } from "./levels";
import type { ParticleOrigin } from "./particleScene";
import type { ParticleSystemInstance } from "./particleRenderer";
import type { StateSwitch } from "./visibility";

/** What the viewport remembers about one running system, so it can be rebuilt on restart. */
export interface ParticleEntry {
    /** Its key in `ParticleRig.particleSystems`, so an entry alone can answer for itself. */
    id: string;
    system: AlamoParticleContent;
    attachToPartId?: string;
    attachBone?: string;
    /** The bone's index, which is what tells two same-named bones apart. */
    attachBoneIndex?: number;
    /** See `ParticlePlacement.proxyName`. */
    proxyName?: string;
    /**
     * The effect's state switch, as its last writer left it: the proxy's own flag, or what the
     * effect rules wrote over it in a unit preview.
     *
     * Recomputed whenever a hardpoint blows up or is repaired, which is why the reader's own choice
     * cannot live here - it would be overwritten every time anything else on the model changed.
     */
    state: StateSwitch;
    /** The damage-level switch, held apart so a level change cannot clobber the state. */
    altVisible: boolean;
    /** The detail-level switch, the same way. */
    lodVisible: boolean;
    levels: LevelTagged;
    /** The owning object's uniform render scale, so a rebuild keeps it. */
    scaleFactor: number;
    /** Whether the MODEL declares this system or the Gameplay lens asked for it. */
    origin: ParticleOrigin;
    instance: ParticleSystemInstance;
}
