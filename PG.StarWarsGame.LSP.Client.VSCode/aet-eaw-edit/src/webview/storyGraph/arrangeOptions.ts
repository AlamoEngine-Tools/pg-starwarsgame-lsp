// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The elk options every story graph auto-arrange runs with.

/**
 * elk auto-arrange options - Event nodes are full-blueprint-style forms, several times taller
 * than plain boxes; the default spacing crowded them together enough to overlap.
 *
 * A module of its own so `arrangeLayout.test.ts` can lay a graph out with the very options the
 * webview uses. That test is the guard on the `elkjs` pin, and a copy of these values would let it
 * drift into passing against options nobody ships.
 */
export const ARRANGE_OPTIONS = {
    'elk.direction': 'RIGHT',
    'elk.spacing.nodeNode': '60',
    'elk.layered.spacing.nodeNodeBetweenLayers': '90',
    // Named rather than left at elk's default, which is BRANDES_KOEPF: it straightens long edges by
    // moving whatever is in their way out of the way, and a campaign is mostly long edges - a
    // RESET_BRANCH reward reaches every event carrying that branch, a flag joins every writer to
    // every reader. Measured on the merged Empire campaign (672 events): the default arranged it
    // into 60880 x 43332 with a 22440px hole inside one layer and 2.08% of the box covered;
    // SIMPLE gives 47619 x 21962, a 205px worst gap and 5.23% coverage. Straightness was not even
    // the trade - mean edge slant improved too (1362px -> 917px), because the derived edges defeat
    // the alignment and only the spreading survives. `arrangeLayout.test.ts` guards the gap.
    'elk.layered.nodePlacement.strategy': 'SIMPLE',
};
