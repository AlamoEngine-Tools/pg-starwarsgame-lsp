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

/**
 * The shape that made auto-arrange spread out: one event reaching a whole late group at once.
 *
 * `RESET_BRANCH` / `DISABLE_BRANCH` emit a control edge to EVERY event carrying that branch (157
 * of them in the merged Empire campaign), so a single node acquires edges that span most of the
 * layers between it and them. Every such edge is what elk's default node placement tries to
 * straighten, and straightening means pushing whatever is in the way out of the way.
 */
function branchResetGraph(chainLength: number, members: number) {
    const nodes = [eventNode('reset')];
    const connections = [];
    for (let i = 0; i < chainLength; i++) {
        nodes.push(eventNode(`s${i}`, 300));
        if (i > 0) { connections.push(connect(`sc${i}`, `s${i - 1}`, `s${i}`)); }
    }
    connections.push(connect('cr', 'reset', 's0'));
    for (let i = 0; i < members; i++) {
        nodes.push(eventNode(`m${i}`, 240));
        connections.push(connect(`mr${i}`, 'reset', `m${i}`), connect(`ms${i}`, `s${chainLength - 1}`, `m${i}`));
    }
    return { nodes, connections };
}

/** The widest vertical hole inside a single layer - layers being the columns of an elk.direction RIGHT layout. */
function largestGapInALayer(laid: readonly LaidOutNode[]): number {
    const layers = new Map<number, LaidOutNode[]>();
    for (const n of laid) {
        const key = Math.round(n.x);
        if (!layers.has(key)) { layers.set(key, []); }
        layers.get(key)!.push(n);
    }
    let worst = 0;
    for (const column of layers.values()) {
        column.sort((a, b) => a.y - b.y);
        for (let i = 1; i < column.length; i++) {
            worst = Math.max(worst, column[i].y - (column[i - 1].y + column[i - 1].height));
        }
    }
    return worst;
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

    // The gap guard, and the reason ARRANGE_OPTIONS names a node placement at all.
    //
    // elk's default placement is BRANDES_KOEPF, which straightens long edges by moving nodes apart.
    // Story nodes are 280x350-ish forms rather than boxes, and a campaign is full of edges that
    // span most of it, so "apart" came out in thousands of pixels: the merged Empire campaign
    // (672 events) arranged into a 60880 x 43332 box with a 22440px hole inside one layer, and
    // 2.08% of that box had a node on it. The layout was correct and unreadable - you scrolled
    // through empty space looking for the next event.
    //
    // Measured on this fixture: 1124px with the default placement, 60px - the configured spacing,
    // exactly - with SIMPLE. The bound is deliberately loose; anything near the spacing passes,
    // and a placement that spreads a layer out again cannot.
    it('keeps a layer packed when one event reaches a whole late group', async () => {
        const laid = await arrange(branchResetGraph(10, 6));

        const gap = largestGapInALayer(laid);
        assert.ok(gap <= NODE_SPACING * 1.5,
            `no layer holds a hole wider than ${NODE_SPACING * 1.5}px (measured ${gap.toFixed(1)})`);
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
