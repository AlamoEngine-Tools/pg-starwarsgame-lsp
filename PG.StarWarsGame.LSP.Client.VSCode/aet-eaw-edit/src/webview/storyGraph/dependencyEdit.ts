// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import type {StoryGraphNodeDto} from '../../protocol/story';

/**
 * Which threads in the current graph belong to a referenced project, and to which one.
 *
 * A parent project is a library: inspect it, run it, do not edit it. The server refuses an edit
 * staged against one, so the view must not offer it in the first place - the refusal used to arrive
 * at Save, after the work.
 *
 * The index covers what the graph currently holds, which a filter can narrow; it is the visible
 * half of the rule, not the authority. The command boundary decides, and a gesture that slips past
 * a filtered-out thread is still refused there.
 */
export function readOnlyThreadIndex(nodes: readonly StoryGraphNodeDto[]): Map<string, string> {
    const index = new Map<string, string>();
    for (const node of nodes) {
        if (node.threadUri && node.readOnlyOwner) {
            index.set(node.threadUri, node.readOnlyOwner);
        }
    }
    return index;
}

/**
 * The referenced project blocking a write to `threadUri`, or null when the write may proceed.
 *
 * A command carrying no thread is not judged here - the server knows what such a command targets.
 */
export function readOnlyOwnerOf(
    index: ReadonlyMap<string, string>, threadUri: string | null | undefined,
): string | null {
    if (!threadUri) {
        return null;
    }
    return index.get(threadUri) ?? null;
}

/** Why an editing control is disabled, and where the edit does belong. */
export function readOnlyMessage(owner: string): string {
    return `Read-only: '${owner}' is a referenced project. Edit this thread in '${owner}' itself`;
}

/**
 * Why Edit mode cannot be entered for this graph, or null when it can.
 *
 * ANY read-only thread blocks it, not just a graph made entirely of them. A mixed graph is the
 * worse case, not the easier one: the canvas takes drops wherever you aim them, a new event lands
 * in whichever thread is nearest, and a prereq drawn to a referenced event writes to that event's
 * file - so "some of this is editable" cannot be enforced by disabling controls on the nodes that
 * are not.
 *
 * The honest place to say so is the MODE SWITCH, before anything is typed, dragged or dropped.
 *
 * Only nodes that HAVE a thread count. Junctions and portals are structural - drawn from the events
 * around them and owning no file - so they neither block nor permit.
 */
export function editModeBlockedBy(nodes: readonly StoryGraphNodeDto[]): string | null {
    const threaded = nodes.filter(n => n.threadUri);
    const owners = [...new Set(
        threaded.map(n => n.readOnlyOwner).filter((o): o is string => Boolean(o)))];

    if (owners.length === 0) {
        return null;
    }

    const named = owners.length === 1
        ? `'${owners[0]}'`
        : owners.map(o => `'${o}'`).join(' and ');
    const all = threaded.every(n => n.readOnlyOwner);

    return `${all ? 'Every thread here belongs' : 'Part of this graph belongs'} to ${named}, which `
        + 'this workspace only references. Open the graph in that project to edit it.';
}
