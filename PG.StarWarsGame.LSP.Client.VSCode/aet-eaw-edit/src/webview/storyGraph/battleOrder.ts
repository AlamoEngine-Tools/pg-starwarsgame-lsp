// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// A battle's place in the order of play, as the galactic graph shows it: on the battle's stand-in,
// and in where the layout puts the stand-ins. The server decides the order (the rank each stand-in
// carries); this is the graph's reading of it. Pure, so the unit harness covers it.

import type { StoryGraphEdgeDto, StoryGraphNodeDto } from "../../protocol";

/** "Sits left of": a battle's stand-in and the stand-in of the battle played after it. */
export interface BattleOrderEdge {
    readonly source: string;
    readonly target: string;
}

/**
 * The pairs of stand-ins the layout has to keep in the order of play, each a battle and the next
 * one the graph shows.
 *
 * Given to the layout as edges, because that is the one thing a layered layout promises about two
 * nodes: an edge's target lies in a later layer than its source. A pair is left out when the story
 * already leads from the later battle to the earlier one - the layout breaks a loop by turning an
 * edge round, and that edge could as well be one of the story's own.
 */
export function battleOrderEdges(
    nodes: readonly Pick<StoryGraphNodeDto, "id" | "battleOrder">[],
    edges: readonly Pick<StoryGraphEdgeDto, "fromId" | "toId">[],
): BattleOrderEdge[] {
    const stubs = nodes
        .filter((node) => node.battleOrder)
        .sort((a, b) => a.battleOrder!.rank - b.battleOrder!.rank);
    if (stubs.length < 2) {
        return [];
    }

    const next = new Map<string, string[]>();
    const link = (from: string, to: string): void => {
        const targets = next.get(from);
        if (targets) {
            targets.push(to);
        } else {
            next.set(from, [to]);
        }
    };
    for (const edge of edges) {
        link(edge.fromId, edge.toId);
    }

    const leads = (from: string, to: string): boolean => {
        const seen = new Set<string>([from]);
        const open = [from];
        while (open.length > 0) {
            for (const target of next.get(open.pop()!) ?? []) {
                if (target === to) {
                    return true;
                }
                if (!seen.has(target)) {
                    seen.add(target);
                    open.push(target);
                }
            }
        }
        return false;
    };

    const order: BattleOrderEdge[] = [];
    for (let i = 0; i + 1 < stubs.length; i++) {
        const source = stubs[i].id;
        const target = stubs[i + 1].id;
        if (leads(target, source)) {
            continue;
        }
        link(source, target);
        order.push({ source, target });
    }
    return order;
}

/**
 * The size of a node drawn as a labelled box - a portal, a script state, a battle's stand-in. One
 * line high, and a second line for the stand-in that carries its battle's place in the order.
 * The mounted node and the unmounted model both size from this, so they cannot drift apart.
 */
export function boxNodeSize(
    dto: Pick<StoryGraphNodeDto, "label" | "battleOrder">,
): { w: number; h: number } {
    return {
        w: Math.max(140, Math.min(dto.label.length, 30) * 6.5 + 32),
        h: dto.battleOrder ? 54 : 40,
    };
}
