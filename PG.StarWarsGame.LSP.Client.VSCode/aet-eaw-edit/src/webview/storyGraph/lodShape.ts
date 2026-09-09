// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/** What the overview draws for a node: the mounted node's own silhouette, roughed in. */
export type LodShape = 'rect' | 'circle' | 'diamond';

/**
 * The shape a node keeps when it is too small to mount.
 *
 * Zoomed out, everything used to be a rectangle, so the structural nodes - the AND/OR junctions
 * that carry prereq logic - were indistinguishable from events. They also took the colour a FIRED
 * event takes, which is a lifecycle they do not have and cannot be in. Drawing the silhouette the
 * mounted node already has keeps the reading the same at every zoom level: a circle is an AND, a
 * rotated square an OR, and colour goes back to meaning only what it means for events.
 *
 * Kinds mirror StoryNodeKind on the server, plus the client-only staging junctions. Anything
 * unrecognised stays a rectangle, so a kind added later degrades quietly rather than claiming a
 * shape it has not been given.
 */
export function lodShape(kind: string): LodShape {
    switch (kind) {
        case 'AndJunction':
        case 'StagingAnd':
            return 'circle';
        case 'OrJunction':
        case 'StagingOr':
            return 'diamond';
        default:
            return 'rect';
    }
}
