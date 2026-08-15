// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The one identity a bone has, and the one place that knows how to read it.
//
// The exporter writes `Name#index` onto every bone node (`ModelGlbExporter`, `$"{bone.Name}#{i}"`),
// and that index is the bone's index in the ALO skeleton - which is exactly what an ALA references.
// So the node name is already an absolute id: unique even when two bones share a name, and carrying
// the number the animation format actually uses.
//
// The bugs came from every consumer deriving that for itself. One map was keyed by the full id and
// another by the stripped name, so a lookup crossed the two and quietly matched nothing; re-keying
// one of them fixed that consumer and broke the other. Three separate defects in this project, all
// the same shape.
//
// So: the canonical id is what gets stored and compared, ALWAYS. A bare name - which is what the
// XML, the ALA and a person all use - is resolved to one here, by `resolveBoneId`, and nowhere
// else. Names are for display.

/** A bone's canonical id: the exporter's node name, `Name#index`. */
export type BoneId = string;

/** The name a person reads, with the exporter's disambiguator taken off. */
export function displayName(id: BoneId): string {
    const hash = id.lastIndexOf('#');

    // The LAST hash is the exporter's. A bone may legitimately carry one of its own - `Crusher#0` is
    // a real sub-mesh name on the rancor - so splitting on the first would cut the name in half.
    return hash > 0 && isIndex(id.slice(hash + 1)) ? id.slice(0, hash) : id;
}

/** The skeleton index the id carries, or null when it is a bare name. */
export function boneIndexOf(id: BoneId): number | null {
    const hash = id.lastIndexOf('#');
    const tail = hash > 0 ? id.slice(hash + 1) : '';

    return isIndex(tail) ? Number.parseInt(tail, 10) : null;
}

/**
 * Finds the canonical id a key refers to.
 *
 * The ONLY place a name is turned into an identity. Tried in order of how much the key actually
 * pins down: an exact id first, because two bones can share a name and only the id separates them;
 * then the name; then the name with dots removed, which is what three's `GLTFLoader` does to node
 * names because a dot is illegal in an animation property path.
 *
 * Case-insensitive throughout: the engine uppercases bone names and the files do not.
 */
export function resolveBoneId(ids: Iterable<BoneId>, key: string): BoneId | undefined {
    const all = [...ids];
    const wanted = key.toLowerCase();

    const exact = all.find(id => id.toLowerCase() === wanted);
    if (exact !== undefined) {
        return exact;
    }

    // Sorted, so a model with two bones of one name resolves the bare name the same way twice.
    const named = [...all].sort();

    return named.find(id => displayName(id).toLowerCase() === wanted)
        ?? named.find(id => displayName(id).toLowerCase().replaceAll('.', '') === wanted);
}

function isIndex(value: string): boolean {
    return value.length > 0 && /^\d+$/.test(value);
}
