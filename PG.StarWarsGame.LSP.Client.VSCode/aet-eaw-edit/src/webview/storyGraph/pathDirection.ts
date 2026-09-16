// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which way the reachable-from filter reaches.
//
// The filter answered one question - "what does this event lead to" - and an author asked for the
// inverse, which is the one you want when an event fires and you need to know what could have armed
// it. Both together is the whole path through the event.
//
// The strings are the wire values the server parses (`GetStoryGraphParams.ReachableDirection`);
// anything else there means Downstream, so an older client and an unfiltered request behave as they
// always did.

export type PathDirection = 'Upstream' | 'Both' | 'Downstream';

export interface PathDirectionChoice {
    readonly direction: PathDirection;
    /** What the reader sees: one word naming the edges kept, to pair with the in/both/out icons. */
    readonly label: string;
    /** Codicon name, as the rest of an event node's header uses. */
    readonly icon: string;
}

/**
 * In to out, the way the arrows point.
 *
 * The labels name the EDGES rather than the events, which is what keeps them to one word each and
 * lets the icon carry the direction: an event's incoming paths are what can arm it, its outgoing
 * paths are what it arms. Order is what the menu renders, so it is reading order and nothing else.
 */
export const PATH_DIRECTIONS: readonly PathDirectionChoice[] = [
    { direction: 'Upstream', label: 'Incoming', icon: 'arrow-left' },
    { direction: 'Both', label: 'Both', icon: 'arrow-both' },
    { direction: 'Downstream', label: 'Outgoing', icon: 'arrow-right' },
];

const DEFAULT_DIRECTION: PathDirection = 'Downstream';

export function isPathDirection(value: string): value is PathDirection {
    return PATH_DIRECTIONS.some(choice => choice.direction === value);
}

/** The label for a wire value, falling back to the default direction for anything unrecognised. */
export function directionLabel(value: string | undefined): string {
    const direction = value !== undefined && isPathDirection(value) ? value : DEFAULT_DIRECTION;
    return PATH_DIRECTIONS.find(choice => choice.direction === direction)!.label;
}
