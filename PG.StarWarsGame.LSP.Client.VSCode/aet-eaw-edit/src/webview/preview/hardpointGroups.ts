// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Hardpoint cards, gathered by TYPE and narrowed by a filter.
//
// A Star Destroyer's eight laser hardpoints are eight cards that differ only by which corner they
// sit on; read as one flat list they are eight things to scroll past on the way to the engine you
// wanted. The type is what a reader already thinks in - it is what picks the reticle, and it is the
// tag they would search their own files for - so it is what the list is broken on.
//
// The same shape the story graph uses for its events and rewards.

import { type HardpointCard } from './hardpointCards';

/** One heading and the cards under it. */
export interface HardpointGroup {
    /** The `Type` tag, or the empty string where a hardpoint declares none. */
    type: string;

    /** {@link typeLabel} of that type, for the heading. */
    label: string;

    cards: HardpointCard[];
}

/**
 * The cards a filter keeps, grouped by type.
 *
 * Matched against the type AND the name: the type is what the groups are, but a reader looking for
 * one hardpoint knows its id, and a filter that ignored that would be the harder half to use.
 *
 * Groups keep the order their type first APPEARS in rather than being sorted. The scene lists
 * hardpoints in the order the XML does, and that order carries the author's own grouping of the
 * hull - an alphabet would throw it away and give nothing back.
 */
export function groupHardpoints(
    cards: readonly HardpointCard[], filter: string,
): HardpointGroup[] {
    const wanted = filter.trim().toLowerCase();

    const matches = (card: HardpointCard): boolean =>
        wanted === ''
        || (card.type ?? '').toLowerCase().includes(wanted)
        || card.id.toLowerCase().includes(wanted);

    const groups = new Map<string, HardpointGroup>();

    for (const card of cards) {
        if (!matches(card)) {
            continue;
        }

        const type = card.type ?? '';
        const group = groups.get(type);

        if (group === undefined) {
            groups.set(type, { type, label: typeLabel(card.type), cards: [card] });
        } else {
            group.cards.push(card);
        }
    }

    return [...groups.values()];
}

/**
 * A hardpoint type as a heading.
 *
 * `HARD_POINT_WEAPON_LASER` is what the file says and what a reader greps for - it is also
 * shouting, four times over on a hull with four kinds of hardpoint. The heading says it plainly;
 * every card underneath still carries the tag verbatim, so nothing is lost.
 *
 * A type this does not recognise is tidied the same way rather than passed through: a mod's own
 * type deserves the same heading as a shipped one.
 */
export function typeLabel(type: string | null | undefined): string {
    if (type === null || type === undefined || type.trim() === '') {
        return 'No type declared';
    }

    const words = type.replace(/^HARD_?POINT_/i, '').replace(/_/g, ' ').trim().toLowerCase();

    return words === '' ? type : words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * Which group a card is in, or null when no group holds it.
 *
 * Clicking a targeting mark on the model picks that hardpoint, and the list then has to bring its
 * card into view - which it cannot do while the group holding it is folded shut, or while a filter
 * has left it out entirely. Null says there is nothing to reveal, which is the honest answer for a
 * card the reader has filtered away.
 */
export function groupOf(
    groups: readonly HardpointGroup[], cardId: string,
): string | null {
    return groups.find(group => group.cards.some(card => card.id === cardId))?.type ?? null;
}
