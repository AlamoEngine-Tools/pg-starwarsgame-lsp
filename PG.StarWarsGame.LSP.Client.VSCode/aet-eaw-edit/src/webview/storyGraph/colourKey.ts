// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The branch half of the colour key (issue #128).
//
// Zoomed out, a node that belongs to a branch is drawn in that branch's colour rather than its
// lifecycle's (`overviewToken`), and the same hue glows round a mounted node and strokes the Requires
// edges into it. Which colour a branch gets depends on where it sits in the campaign's branch list
// (`branchColours`), so no fixed legend can say which branch is which - the key has to be built from
// the branches actually in the graph.

import type { BranchColour } from './palette';

export interface BranchKeyEntry {
    readonly branch: string;
    /** The palette token the overview, the glow and the edge stroke all resolve. */
    readonly token: string;
    /** Other branches in view drawn in the same colour - six hues cycle past the sixth branch. */
    readonly sharedWith: readonly string[];
}

/**
 * One entry per branch named by an event in the graph, in the order colours are handed out.
 *
 * @param colourOf The same assignment the graph draws with - pass that one, not a fresh one, or a
 *                 swatch can disagree with the node it describes.
 */
export function branchKey(
    nodes: readonly { readonly kind: string; readonly branch?: string | null }[],
    colourOf: BranchColour,
): BranchKeyEntry[] {
    // Events only: a junction's glow is inherited from its owner event, which is already counted.
    const names = [...new Set(nodes
        .filter(node => node.kind === 'Event' && node.branch !== null && node.branch !== undefined && node.branch !== '')
        .map(node => node.branch as string))]
        .sort();

    const tokens = new Map(names.map(name => [name, colourOf(name)]));

    return names.map(name => ({
        branch: name,
        token: tokens.get(name)!,
        sharedWith: names.filter(other => other !== name && tokens.get(other) === tokens.get(name)),
    }));
}
