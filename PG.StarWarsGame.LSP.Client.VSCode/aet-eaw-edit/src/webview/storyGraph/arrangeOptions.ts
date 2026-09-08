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
};
