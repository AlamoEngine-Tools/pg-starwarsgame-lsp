// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What is worked out about a particle system ONCE, whatever name it is filed under and however
// often it is placed (#436).
//
// A mod's effect files repeat: the same system saved under a dozen file names, each storing its
// own name and nothing else that differs, and one system placed on every engine of a ship. What
// follows from the emitters alone - the order they run in, which of them another one spawns,
// whether any is born on a mesh - is the same answer for all of those, so it is worked out for the
// first and read for the rest. What belongs to one placement - its buffers, its clock, its
// materials' textures - stays with the instance.
//
// `emitsFromMesh` is the answer that pays. A system born on a mesh needs the attached geometry
// copied into its own space, and on a capital ship that geometry is the hull: MEASURED, 546 ms of
// a 1.28 s `addParticleSystem` total for 17 placements on a 149 MB model went into that copy,
// for systems of which none is born on a mesh.

import type {
    AlamoEmitter,
    AlamoParticleContent,
} from "../../protocol/modelPreview";

/** What a system's emitters decide, the same for every placement of it. */
export interface PreparedParticleSystem {
    /** File indices in update and draw order: ascending priority ticket, file order on ties. */
    readonly order: readonly number[];
    /** Emitters another emitter spawns, which must not also run a clock of their own. */
    readonly dependent: ReadonlySet<number>;
    /** Whether any emitter is born on the attached geometry, so that geometry has to be read. */
    readonly emitsFromMesh: boolean;
}

/**
 * The index of the emitter a spawn link names, or -1.
 *
 * Measured in the game: after loading, a link is resolved by finding the emitter whose
 * PRIORITY TICKET equals the stored value - not the emitter at that position. Every shipped
 * and workshop system numbers its tickets in file order, so both readings agree there; a
 * reordered file is where they part. A system with no tickets at all (an older server, a
 * fixture) still reads the link as a position.
 */
export function emitterIndexOf(
    emitters: readonly AlamoEmitter[],
    link: number,
): number {
    if (link < 0) {
        return -1;
    }

    const ticketed = emitters.some(
        (e) => e.properties.priorityTicket !== undefined,
    );
    if (!ticketed) {
        return link < emitters.length ? link : -1;
    }

    return emitters.findIndex((e) => e.properties.priorityTicket === link);
}

/** Works out what a system's emitters decide. Pure: the same system gives the same answer. */
export function prepareParticleSystem(
    system: AlamoParticleContent,
): PreparedParticleSystem {
    const emitters = system.emitters;

    // Measured in the game: after loading, the emitters are sorted by priority ticket,
    // ascending, and that is their update and draw order from then on. The file order is kept
    // for everything that names an emitter by index; this is the order they run in.
    const ticketed = emitters.some(
        (e) => e.properties.priorityTicket !== undefined,
    );
    const order = emitters
        .map((e, index) => ({
            index,
            ticket: ticketed ? (e.properties.priorityTicket ?? 0) : index,
        }))
        .sort((a, b) => a.ticket - b.ticket || a.index - b.index)
        .map((e) => e.index);

    const dependent = new Set<number>();
    for (const emitter of emitters) {
        for (const link of [emitter.spawnOnDeath, emitter.spawnDuringLife]) {
            const child = emitterIndexOf(emitters, link);
            if (child >= 0) {
                dependent.add(child);
            }
        }
    }

    return {
        order,
        dependent,
        emitsFromMesh: emitters.some(
            (e) => e.properties.emitFromMesh !== "Disabled",
        ),
    };
}

/**
 * What makes two systems the same system: everything that is simulated or drawn. The stored
 * name is not - an instance is named by its id - and neither are the notices, which describe the
 * file to the reader.
 */
function contentKeyOf(system: AlamoParticleContent): string {
    return JSON.stringify([system.leaveParticles, system.emitters]);
}

/**
 * The systems one scene has prepared, each distinct one once.
 *
 * Held by the particle rig and cleared with its systems, so it never outlives the scene whose
 * effects it describes.
 */
export class ParticlePreparations {
    /** By the object handed in: one system placed many times is not even compared. */
    private byObject = new WeakMap<
        AlamoParticleContent,
        PreparedParticleSystem
    >();
    private readonly byContent = new Map<string, PreparedParticleSystem>();

    /** How many distinct systems were prepared since the last {@link clear}. */
    get prepared(): number {
        return this.byContent.size;
    }

    of(system: AlamoParticleContent): PreparedParticleSystem {
        const seen = this.byObject.get(system);
        if (seen !== undefined) {
            return seen;
        }

        const key = contentKeyOf(system);
        let prepared = this.byContent.get(key);
        if (prepared === undefined) {
            prepared = prepareParticleSystem(system);
            this.byContent.set(key, prepared);
        }

        this.byObject.set(system, prepared);
        return prepared;
    }

    clear(): void {
        this.byObject = new WeakMap();
        this.byContent.clear();
    }
}
