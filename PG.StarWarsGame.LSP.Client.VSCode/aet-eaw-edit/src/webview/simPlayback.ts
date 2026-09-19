// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Pure helpers for playing a simulation trace on the graph: which steps belong to which tick,
 * which connections a step travels along, and how the fire counts change. The rete editor and
 * the timers stay in storyGraph.tsx; everything here is testable without a DOM.
 */

import {StorySimStepDto} from '../protocol/story';

/** Mirrors StorySimCause.Fires on the server: the causes that mean "this event fired". */
export const FIRE_CAUSES: ReadonlySet<string> =
    new Set(['manual', 'lua', 'poll', 'prereq', 'trigger', 'speech', 'movie']);

/** One outgoing edge of the adjacency the path search walks. */
export interface Adjacent {
    to: string;
    connectionId: string;
}

/** Steps grouped by tick, in trace order; ticks come out ascending. */
export function groupByTick(steps: readonly StorySimStepDto[]): StorySimStepDto[][] {
    const groups: StorySimStepDto[][] = [];
    let current: StorySimStepDto[] | null = null;
    let currentTick = Number.NaN;
    for (const step of steps) {
        if (!current || step.tick !== currentTick) {
            current = [];
            currentTick = step.tick;
            groups.push(current);
        }
        current.push(step);
    }
    return groups;
}

/**
 * The connection ids from `sourceId` to `targetId`, passing only through nodes `passThrough`
 * accepts (junctions and portals - never another event). Breadth-first, so the shortest route
 * wins; empty when there is none within `maxHops`.
 */
export function resolvePath(
    sourceId: string,
    targetId: string,
    adjacency: ReadonlyMap<string, readonly Adjacent[]>,
    passThrough: (nodeId: string) => boolean,
    maxHops = 4,
): string[] {
    if (sourceId === targetId) {
        return [];
    }
    const cameFrom = new Map<string, { from: string; connectionId: string }>();
    let frontier = [sourceId];
    for (let hop = 0; hop < maxHops && frontier.length > 0; hop++) {
        const next: string[] = [];
        for (const nodeId of frontier) {
            for (const edge of adjacency.get(nodeId) ?? []) {
                if (edge.to === sourceId || cameFrom.has(edge.to)) {
                    continue;
                }
                cameFrom.set(edge.to, {from: nodeId, connectionId: edge.connectionId});
                if (edge.to === targetId) {
                    return unwind(cameFrom, sourceId, targetId);
                }
                if (passThrough(edge.to)) {
                    next.push(edge.to);
                }
            }
        }
        frontier = next;
    }
    return [];
}

function unwind(
    cameFrom: ReadonlyMap<string, { from: string; connectionId: string }>, sourceId: string, targetId: string,
): string[] {
    const path: string[] = [];
    let at = targetId;
    while (at !== sourceId) {
        const link = cameFrom.get(at);
        if (!link) {
            return [];
        }
        path.unshift(link.connectionId);
        at = link.from;
    }
    return path;
}

/** How many times each node fired within these steps. */
export function fireDelta(steps: readonly StorySimStepDto[]): Map<string, number> {
    const delta = new Map<string, number>();
    for (const step of steps) {
        if (step.to === 'Fired' && FIRE_CAUSES.has(step.cause)) {
            delta.set(step.nodeId, (delta.get(step.nodeId) ?? 0) + 1);
        }
    }
    return delta;
}

/** True when the step changes a lifecycle the canvas paints. */
export function isLifecycleStep(step: StorySimStepDto): boolean {
    return step.to !== null && step.to !== undefined;
}
