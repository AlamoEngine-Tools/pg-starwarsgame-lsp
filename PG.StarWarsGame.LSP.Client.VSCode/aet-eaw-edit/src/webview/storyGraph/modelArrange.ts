// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Auto-arrange over the MODEL, with nothing mounted.
//
// The first open of a campaign used to mount every node as a real rete node purely so elk could
// measure it, then unmount back to the visible window. Measured on the shipped Underworld campaign
// (2860 nodes): 28.1 s, of which about 18 s went on that mounting and its measurements were thrown
// away (issue #131). The model already carries every size - `modelSizeFor` computes them for the
// windowing pass on every later open - so the layout can run on those sizes instead.
//
// The elk graph is still built by `rete-auto-arrange-plugin`'s own preset, and run by its own elk
// instance, so the ports, `portConstraints` and edge shape stay exactly what the plugin produced
// when it was handed mounted nodes. Only the SIZES now come from the model rather than the DOM.
// `arrangeLayout.test.ts` reaches into the plugin the same way, for the same reason.

import { AutoArrangePlugin, Presets as ArrangePresets } from 'rete-auto-arrange-plugin';

import { ARRANGE_OPTIONS } from './arrangeOptions';

/** The three options the plugin bakes into every layout before merging ours over them. */
const PLUGIN_BAKED_OPTIONS = {
    'elk.algorithm': 'layered',
    'elk.hierarchyHandling': 'INCLUDE_CHILDREN',
    'elk.edgeRouting': 'POLYLINE',
};

export interface ArrangeNode {
    readonly id: string;
    readonly width: number;
    readonly height: number;
    /** Whether the node carries the 'in' socket - an edge target does, as on the mounted node. */
    readonly hasIn: boolean;
    readonly hasOut: boolean;
}

export interface ArrangeEdge {
    readonly id: string;
    readonly source: string;
    readonly target: string;
}

/** Where each node lands, in graph coordinates. Empty for an empty graph. */
export async function arrangePositions(
    nodes: readonly ArrangeNode[],
    edges: readonly ArrangeEdge[],
): Promise<Map<string, { x: number; y: number }>> {
    const placed = new Map<string, { x: number; y: number }>();
    if (nodes.length === 0) { return placed; }

    const context = {
        nodes: nodes.map(node => ({
            id: node.id,
            width: node.width,
            height: node.height,
            inputs: node.hasIn ? { in: { index: 0 } } : {},
            outputs: node.hasOut ? { out: { index: 0 } } : {},
        })),
        // Only an edge whose endpoints both carry the socket it uses, or the plugin builds an elk
        // edge against a port that does not exist.
        connections: edges
            .filter(edge => nodes.some(n => n.id === edge.source && n.hasOut)
                            && nodes.some(n => n.id === edge.target && n.hasIn))
            .map(edge => ({
                id: edge.id, source: edge.source, sourceOutput: 'out', target: edge.target, targetInput: 'in',
            })),
    };

    const plugin = new AutoArrangePlugin();
    plugin.addPreset(ArrangePresets.classic.setup());
    // graphToElk and the plugin's elk instance are past its public typings - see the file comment.
    const graph = (plugin as any).graphToElk(context, undefined);
    const laidOut = await (plugin as any).elk.layout({
        id: 'root',
        layoutOptions: { ...PLUGIN_BAKED_OPTIONS, ...ARRANGE_OPTIONS },
        ...graph,
    });

    for (const child of (laidOut.children ?? []) as { id: string; x?: number; y?: number }[]) {
        placed.set(child.id, { x: child.x ?? 0, y: child.y ?? 0 });
    }
    return placed;
}
