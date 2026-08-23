// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What to say when something asks to hang off a bone that is not there.
//
// `Viewport.attachmentFor` answers the part's root for a bone it cannot find, and the model root
// for a part it cannot find. Both are the right thing to DRAW - a mount at the hull's origin is
// recoverable, a crash is not - and both are silent, which is the problem. Two separate bugs in one
// session were hidden by them: hardpoint fire cones resolving on the mounted model instead of the
// hull, and reticles piling up at the origin.
//
// Kept out of the viewport so the wording has tests without a GPU in the room.

/** An attachment a caller asked for by name. */
export interface AttachmentRequest {
    partId: string;
    /** Absent when the caller wanted the part itself rather than one of its bones. */
    bone?: string;
    /** Preferred over the name where the caller has one - bone names repeat. */
    boneIndex?: number;
}

/** What the viewport could actually find for that request. */
export interface AttachmentFound {
    part: boolean;
    bone: boolean;
}

/**
 * Why an attachment did not resolve, or null when it did.
 *
 * States the FACT and stops there. It used to name the fallback too - "drawn at that model's
 * origin" - which stopped being true the moment `bonePosition` started answering null instead of
 * placing anything: the same unresolved bone now means a misplaced object to one caller and a
 * missing one to another. A message that describes a consequence it cannot know is worse than one
 * that describes only what it can.
 *
 * What it does carry is everything needed to FIND the thing: the part, the bone, and the bone index
 * where there is one, because bone names repeat.
 */
export function attachmentProblem(
    request: AttachmentRequest, found: AttachmentFound,
): string | null {
    if (!found.part) {
        return `'${request.partId}' has not loaded, so nothing can hang off ${describe(request)}.`;
    }

    if (request.bone !== undefined && !found.bone) {
        return `'${request.partId}' has no bone '${request.bone}'`
            + `${request.boneIndex === undefined ? '' : ` (index ${request.boneIndex})`}`
            + ', so nothing can hang off it.';
    }

    return null;
}

function describe(request: AttachmentRequest): string {
    return request.bone === undefined
        ? 'it'
        : `its bone '${request.bone}'`;
}
