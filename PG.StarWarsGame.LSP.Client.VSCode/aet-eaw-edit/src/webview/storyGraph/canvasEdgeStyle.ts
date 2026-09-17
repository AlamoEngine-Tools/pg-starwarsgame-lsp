// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// How the overview canvas strokes an edge.
//
// A windowed graph - every campaign past 60 nodes - mounts no rete connections; the canvas draws all
// of its edges. It drew them as ONE muted path, so on any real campaign the branch strand and the
// edge-kind colours were simply gone, and came back only when a filter shrank the graph below the
// window threshold (reported 2026-09-15). This gives the canvas the same answer the mounted
// connection gives: Requires in its branch's colour, every other kind in its EDGE_KINDS colour and
// dash.
//
// No glow, no shadow. Those are what cost the overview its frame rate before. A plain coloured stroke,
// bucketed so each distinct style is stroked once per frame.

import { type BranchColour, EDGE_KINDS } from './palette';

/** The stroke an edge with no colour of its own takes - what every canvas edge used to be. */
export const MUTED_EDGE_TOKEN = '--colour-muted';

export interface CanvasEdgeStyle {
    readonly token: string;
    /** Canvas line dash, from the kind's SVG dash array; empty is solid. */
    readonly dash: readonly number[];
    /** Equal for edges stroked alike, so the draw pass can bucket on it. */
    readonly key: string;
}

const kindStyles = new Map(EDGE_KINDS
    .filter(kind => kind.kind !== 'Prereq')
    .map(kind => [kind.kind, style(kind.token, kind.dash)]));
// The stub side of a tactical attachment is the same relation, drawn the same way.
kindStyles.set('TacticalEntry', kindStyles.get('Tactical')!);

const muted = style(MUTED_EDGE_TOKEN, '');

/**
 * @param branch The branch the edge feeds, as `edgeBranchFrom` resolves it - only ever set for
 *               Requires edges, and only Requires edges take it.
 */
export function canvasEdgeStyle(kind: string, branch: string | null, colourOf: BranchColour): CanvasEdgeStyle {
    if (kind === 'Prereq') {
        return branch ? style(colourOf(branch), '') : muted;
    }
    return kindStyles.get(kind) ?? muted;
}

function style(token: string, dash: string): CanvasEdgeStyle {
    return {
        token,
        dash: dash === '' ? [] : dash.split(/\s+/).map(Number),
        key: `${token}|${dash}`,
    };
}
