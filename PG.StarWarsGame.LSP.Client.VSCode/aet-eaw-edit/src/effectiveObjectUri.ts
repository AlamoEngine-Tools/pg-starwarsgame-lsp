// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What an effective-object document's URI carries (#276).
//
// The document is virtual, so its URI is all VS Code keeps of it: a tab restored after a reload is
// handed nothing else. The project therefore travels in the URI with the object rather than in a
// map beside it. A map lost every entry on reload, and two projects asking for one object shared a
// single document, the second request taking it over.
//
// Pure, so it is testable without a VS Code host.

/** The object a document shows and the project whose server resolves it. */
export interface EffectiveObjectRef {
    readonly objectId: string;
    /** Empty for a document opened before the project travelled in the URI. */
    readonly projectKey: string;
}

/** The URI query for one object of one project. */
export function effectiveObjectQuery(ref: EffectiveObjectRef): string {
    return new URLSearchParams({
        object: ref.objectId,
        project: ref.projectKey,
    }).toString();
}

/**
 * Reads a document's query back. A query that is not one of ours is the object id alone, as an
 * older document carried it.
 */
export function parseEffectiveObjectQuery(query: string): EffectiveObjectRef {
    const params = new URLSearchParams(query);
    const objectId = params.get("object");
    return objectId === null
        ? { objectId: query, projectKey: "" }
        : { objectId, projectKey: params.get("project") ?? "" };
}

/**
 * The document's path, whose base name is the tab's title: the object, and its project once the
 * window runs more than one.
 */
export function effectiveObjectPath(
    objectId: string,
    projectName: string,
    projectCount: number,
): string {
    const subject =
        projectCount > 1 && projectName
            ? `${objectId} - ${projectName}`
            : objectId;
    return `/${subject} (effective preview, read-only).xml`;
}
