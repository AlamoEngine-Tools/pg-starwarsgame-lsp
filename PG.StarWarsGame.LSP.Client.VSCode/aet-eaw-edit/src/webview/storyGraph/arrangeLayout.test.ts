// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The auto-arrange upgrade guard.
//
// `elkjs` is pinned to ^0.8.2 because `rete-auto-arrange-plugin@2.0.2` declares that peer range,
// and until this file existed nothing exercised a real auto-arrange - so a green test run said
// nothing about whether a newer elk still lays the story graph out sensibly. The pin was therefore
// not "verified safe", it was "unverifiable". This closes that: run the suite against a candidate
// elk and it either holds these invariants or it does not.
//
// The assertions are INVARIANTS, never golden coordinates. Pinning exact positions would fail on
// every upgrade, including the good ones, which is the opposite of a useful guard.
//
// Scope: this covers elk itself plus the plugin's graph construction (ports, FIXED_POS, edge port
// ids), which is where an elk change would show. It does NOT cover the applier writing positions
// back through `area.translate`, nor esbuild's `elkjs -> elkjs/lib/elk.bundled.js` alias; both need
// a DOM and stay with the manual smoke steps.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { AutoArrangePlugin, Presets as ArrangePresets } from 'rete-auto-arrange-plugin';

import { ARRANGE_OPTIONS } from './arrangeOptions';

/** The three options the plugin bakes into every layout before merging ours over them. */
const PLUGIN_BAKED_OPTIONS = {
    'elk.algorithm': 'layered',
    'elk.hierarchyHandling': 'INCLUDE_CHILDREN',
    'elk.edgeRouting': 'POLYLINE',
};

/** The smallest gap `elk.spacing.nodeNode` promises between two nodes sharing a layer. */
const NODE_SPACING = Number(ARRANGE_OPTIONS['elk.spacing.nodeNode']);

interface LaidOutNode { id: string; x: number; y: number; width: number; height: number }

/** A node as the plugin reads it: the size the DOM measured, plus its port keys. */
function eventNode(id: string, height = 220) {
    return { id, width: 280, height, inputs: { in: { index: 0 } }, outputs: { out: { index: 0 } } };
}

/** AND-junctions are the small boxes; two inputs, one output. */
function junctionNode(id: string) {
    return { id, width: 120, height: 60, inputs: { a: { index: 0 }, b: { index: 1 } }, outputs: { out: { index: 0 } } };
}

function connect(id: string, source: string, target: string, targetInput = 'in') {
    return { id, source, sourceOutput: 'out', target, targetInput };
}

/**
 * A branch-and-merge campaign shape: one event fans out to two, both feed an AND-junction, and the
 * junction leads on. Junction ids embed their owner event exactly as the real graph builds them.
 */
function branchAndMergeGraph() {
    return {
        nodes: [eventNode('e1'), eventNode('e2', 160), eventNode('e3', 300), junctionNode('e1#g0'), eventNode('e4'), eventNode('e5')],
        connections: [
            connect('c1', 'e1', 'e2'),
            connect('c2', 'e1', 'e3'),
            connect('c3', 'e2', 'e1#g0', 'a'),
            connect('c4', 'e3', 'e1#g0', 'b'),
            connect('c5', 'e1#g0', 'e4'),
            connect('c6', 'e4', 'e5'),
        ],
    };
}

/** A long chain, which is what a linear campaign act actually looks like. */
function chainGraph(length: number) {
    const nodes = Array.from({ length }, (_, i) => eventNode(`n${i}`, 140 + (i % 4) * 60));
    const connections = Array.from({ length: length - 1 }, (_, i) => connect(`c${i}`, `n${i}`, `n${i + 1}`));
    return { nodes, connections };
}

/**
 * Lays a graph out exactly as the webview does: the plugin builds the elk graph (so the ports and
 * `portConstraints: FIXED_POS` come from the classic preset rather than from a copy here), and the
 * root carries the plugin's baked options with ARRANGE_OPTIONS merged over them.
 */
async function arrange(context: ReturnType<typeof branchAndMergeGraph>): Promise<LaidOutNode[]> {
    const plugin = new AutoArrangePlugin();
    plugin.addPreset(ArrangePresets.classic.setup());
    // graphToElk and the plugin's own elk instance are past its public typings. Reaching for them
    // is deliberate: it is what keeps this test on the real graph shape and the real elk build,
    // instead of a hand-written approximation that would pass whatever elk did.
    const graph = (plugin as any).graphToElk(context, undefined);
    const elk = (plugin as any).elk;
    const result = await elk.layout({
        id: 'root',
        layoutOptions: { ...PLUGIN_BAKED_OPTIONS, ...ARRANGE_OPTIONS },
        ...graph,
    });
    return (result.children ?? []) as LaidOutNode[];
}

function byId(nodes: readonly LaidOutNode[]): Map<string, LaidOutNode> {
    return new Map(nodes.map(n => [n.id, n]));
}

/** True when two laid-out boxes share any area. Touching edges do not count as overlapping. */
function overlaps(a: LaidOutNode, b: LaidOutNode): boolean {
    return a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
}

describe('story graph auto-arrange', () => {
    it('places every node at a finite position, keeping the measured sizes', async () => {
        const laid = await arrange(branchAndMergeGraph());

        assert.equal(laid.length, 6, 'every node comes back from the layout');
        for (const node of laid) {
            assert.ok(Number.isFinite(node.x) && Number.isFinite(node.y), `${node.id} has a finite position`);
            assert.ok(node.width > 0 && node.height > 0, `${node.id} keeps a real size`);
        }
        // Sizes are the DOM's, not elk's to change - the applier writes positions back onto elements
        // that are already this big, so a resized node would land misaligned.
        assert.equal(byId(laid).get('e1')?.width, 280);
        assert.equal(byId(laid).get('e1#g0')?.width, 120);
    });

    it('flows left to right, so every edge target sits beyond its source', async () => {
        const context = branchAndMergeGraph();
        const laid = byId(await arrange(context));

        // 'elk.direction': 'RIGHT' is the whole reason the graph reads as a timeline. If an upgrade
        // ever ignored or renamed that option the layout would still succeed - just stacked wrongly.
        for (const c of context.connections) {
            const source = laid.get(c.source);
            const target = laid.get(c.target);
            assert.ok(source && target, `${c.id} connects two laid-out nodes`);
            assert.ok(target.x >= source.x + source.width,
                `${c.id}: ${c.target} (x=${target.x}) starts right of ${c.source} (ends at ${source.x + source.width})`);
        }
    });

    it('never overlaps two nodes', async () => {
        // The spacing options exist because event nodes are full blueprint forms, several times
        // taller than a plain box, and the defaults crowded them into each other.
        const laid = await arrange(branchAndMergeGraph());

        for (let i = 0; i < laid.length; i++) {
            for (let j = i + 1; j < laid.length; j++) {
                assert.ok(!overlaps(laid[i], laid[j]), `${laid[i].id} and ${laid[j].id} do not overlap`);
            }
        }
    });

    it('honours the node spacing between siblings in a layer', async () => {
        const context = branchAndMergeGraph();
        const laid = byId(await arrange(context));

        // e2 and e3 are the fan-out: same layer, so the vertical gap is the one nodeNode governs.
        const e2 = laid.get('e2');
        const e3 = laid.get('e3');
        assert.ok(e2 && e3);
        const gap = e3.y - (e2.y + e2.height);
        assert.ok(gap >= NODE_SPACING - 0.5, `siblings are at least ${NODE_SPACING}px apart (measured ${gap.toFixed(1)})`);
    });

    it('lays out a long chain in order', async () => {
        const length = 40;
        const laid = byId(await arrange(chainGraph(length)));

        assert.equal(laid.size, length);
        for (let i = 1; i < length; i++) {
            const previous = laid.get(`n${i - 1}`);
            const current = laid.get(`n${i}`);
            assert.ok(previous && current);
            assert.ok(current.x > previous.x, `n${i} advances past n${i - 1}`);
        }
    });

    it('lays out a graph with no connections at all', async () => {
        // A campaign whose events are all unlinked still opens, and elk is handed the empty edge
        // list rather than being skipped - so it has to survive it.
        const laid = await arrange({ nodes: [eventNode('a'), eventNode('b'), eventNode('c')], connections: [] });

        assert.equal(laid.length, 3);
        for (let i = 0; i < laid.length; i++) {
            for (let j = i + 1; j < laid.length; j++) {
                assert.ok(!overlaps(laid[i], laid[j]), `${laid[i].id} and ${laid[j].id} do not overlap`);
            }
        }
    });

    it('lays out a single node', async () => {
        const laid = await arrange({ nodes: [eventNode('only')], connections: [] });

        assert.equal(laid.length, 1);
        assert.ok(Number.isFinite(laid[0].x) && Number.isFinite(laid[0].y));
    });
});
