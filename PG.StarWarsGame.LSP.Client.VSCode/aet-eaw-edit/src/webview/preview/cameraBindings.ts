// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which saved shot a subject opens with.
//
// The point of the whole camera feature is a roster framed identically, and nobody wants to pick the
// preset by hand forty times. So a preset can be BOUND to what a subject is, and the binding is
// applied when the subject opens - always overridable afterwards, because a rule is a default and
// not a cage.
//
// Three kinds of rule, tried most specific first. That order is the design: a reader who bound one
// unit meant THAT unit, and a category rule they wrote last month must not quietly take it back.

/** What the subject is, as far as a rule can see. */
export interface PreviewSubject {
    /** The game object's id, or null when a bare model is open. */
    objectId: string | null;

    /** Its `Type` tag, or null. */
    objectType: string | null;

    /**
     * Its category tokens.
     *
     * A LIST, because `CategoryMask` names several at once - `Vehicle | AntiInfantry | AntiVehicle`
     * - which is exactly why a category cannot be used as a plain lookup key. A rule tests
     * membership.
     */
    categories: readonly string[];
}

/** One rule tying a preset to a kind of subject. */
export interface CameraBinding {
    id: string;

    /** What the rule matches ON. Ordered by how specific it is: object, then type, then category. */
    kind: 'object' | 'type' | 'category';

    value: string;
    presetId: string;
}

/** Most specific first. A per-object rule always beats a type, and a type always beats a category. */
const ORDER: CameraBinding['kind'][] = ['object', 'type', 'category'];

/**
 * The rule that decides this subject's opening shot, or null when none applies.
 *
 * Within one kind the FIRST matching rule wins, because the list is ordered and the reader ordered
 * it - taking the last would make adding a rule silently change what an existing one does.
 */
export function bindingFor(
    subject: PreviewSubject, bindings: readonly CameraBinding[],
): CameraBinding | null {
    for (const kind of ORDER) {
        const match = bindings.find(
            binding => binding.kind === kind && matches(subject, binding));

        if (match !== undefined) {
            return match;
        }
    }

    return null;
}

function matches(subject: PreviewSubject, binding: CameraBinding): boolean {
    const wanted = binding.value.trim();

    // A rule with nothing in it would match everything, which is never what an empty field means.
    if (wanted === '') {
        return false;
    }

    switch (binding.kind) {
        case 'object':
            return same(subject.objectId, wanted);

        case 'type':
            return same(subject.objectType, wanted);

        case 'category':
            // Membership, not equality - see PreviewSubject.categories.
            return subject.categories.some(category => same(category, wanted));

        default:
            return false;
    }
}

/** The XML is written every which way, so nothing here is case-sensitive. */
function same(value: string | null, wanted: string): boolean {
    return value !== null && value.toLowerCase() === wanted.toLowerCase();
}
