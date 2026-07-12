// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Pure Edit-mode staging logic, kept free of rete/React so it can be unit-tested on its own. The
// story graph webview (storyGraph.tsx) owns the command queue and the editor handle; this module
// only decides which command kinds stage and how each maps to an optimistic dto patch.

// Property edits reflected instantly by an optimistic dto patch (no server round trip).
const PROPERTY_KINDS = [
    'setBranch', 'setPerpetual', 'setDialog',
    'setEventType', 'setRewardType', 'clearEventType', 'clearRewardType', 'setParams',
] as const;

// Structural gestures whose effect on the graph (new/removed/renamed nodes, prereq edges and their
// junctions) is too involved to materialise locally — after staging one, the webview asks the server
// for a preview graph built from the composed working copy (aet/previewStoryGraph, no file write).
export const PREVIEW_KINDS = new Set<string>([
    'createEvent', 'deleteEvent', 'renameEvent', 'createTacticalAttachment',
    'addPrereq', 'addPrereqGroup', 'addPrereqAlternatives', 'removePrereq', 'retargetControlEdge',
]);

/** Every command kind staged locally in Edit mode (nothing is written to disk until Save). */
export const STAGED_KINDS = new Set<string>([...PROPERTY_KINDS, ...PREVIEW_KINDS]);

/** The dto fields an optimistic edit can touch — a structural subset of the graph node dto. */
export interface PatchableEventNode {
    branch?: string | null;
    perpetual?: boolean;
    storyDialog?: string | null;
    eventType?: string | null;
    rewardType?: string | null;
    eventParams?: { position: number; value: string }[] | null;
    rewardParams?: { position: number; value: string }[] | null;
}

/** An optimistic edit: which node to patch, and how to derive its next dto. */
export interface OptimisticEdit {
    nodeId: string;
    apply: <T extends PatchableEventNode>(dto: T) => T;
}

/** The canonical graph-node id for an event — must match the server's StoryGraphBuilder.EventNodeId. */
export function eventNodeId(threadUri: string | null | undefined, eventName: string): string {
    return `${threadUri ?? ''}#${eventName.toLowerCase()}`;
}

/** Upserts param-slot edits (null/empty value removes the slot) into a param list, keeping it sorted. */
export function upsertParams(
    existing: { position: number; value: string }[],
    edits: { position: number; value: string | null }[],
): { position: number; value: string }[] {
    const byPosition = new Map(existing.map(p => [p.position, p.value]));
    for (const edit of edits) {
        if (!edit.value) { byPosition.delete(edit.position); } // null or empty removes the slot
        else { byPosition.set(edit.position, edit.value); }
    }
    return [...byPosition.entries()]
        .sort((a, b) => a[0] - b[0])
        .map(([position, value]) => ({ position, value }));
}

/**
 * Maps a staged command envelope to the optimistic dto patch that mirrors it locally, or null when
 * the kind has no node-level optimistic representation (structural gestures reconcile via re-fetch).
 */
export function optimisticEdit(payload: Record<string, unknown>): OptimisticEdit | null {
    const eventName = payload.eventName as string | undefined;
    if (eventName === undefined) { return null; }
    const nodeId = eventNodeId(payload.threadUri as string | null | undefined, eventName);

    switch (payload.kind as string) {
        case 'setBranch':
            return { nodeId, apply: d => ({ ...d, branch: (payload.value as string) || null }) };
        case 'setPerpetual':
            return { nodeId, apply: d => ({ ...d, perpetual: payload.flag === true }) };
        case 'setDialog':
            return { nodeId, apply: d => ({ ...d, storyDialog: (payload.value as string) || null }) };
        case 'setEventType':
            return { nodeId, apply: d => ({ ...d, eventType: (payload.value as string) || null }) };
        case 'setRewardType':
            return { nodeId, apply: d => ({ ...d, rewardType: (payload.value as string) || null }) };
        case 'clearEventType':
            return { nodeId, apply: d => ({ ...d, eventType: null, eventParams: [] }) };
        case 'clearRewardType':
            return { nodeId, apply: d => ({ ...d, rewardType: null, rewardParams: [] }) };
        case 'setParams': {
            const isReward = (payload.paramKind as string) === 'reward';
            const edits = (payload.params as { position: number; value: string | null }[]) ?? [];
            return {
                nodeId,
                apply: d => ({
                    ...d,
                    [isReward ? 'rewardParams' : 'eventParams']:
                        upsertParams((isReward ? d.rewardParams : d.eventParams) ?? [], edits),
                }),
            };
        }
        default:
            return null; // structural kinds handle their own local mutation before staging
    }
}
