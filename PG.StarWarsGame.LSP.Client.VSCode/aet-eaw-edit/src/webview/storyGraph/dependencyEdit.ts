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
