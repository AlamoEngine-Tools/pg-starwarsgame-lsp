// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The card's two view choices, as mode selectors rather than as a dropdown and a checkbox.
//
// Both pick exactly one of a handful of alternatives, and in both the alternatives ARE the point: a
// reader who does not know the card has a faction frame will not find it behind a closed <select>,
// and a checkbox labelled "Multiplayer body" says nothing about there being a single-player one to
// compare it against. Split out of the panel so the options can be tested without a DOM.

import {type ChoiceOption} from './shared/choice';
import type {EncyclopediaFactionFrame} from '../protocol/encyclopedia';

/** Which body text the card is asked for. */
export type BodyMode = 'sp' | 'mp';

/**
 * One option per faction frame the card declares.
 *
 * Disabled rather than hidden when there is nothing to switch between. The control used to
 * disappear below two frames, which is the one arrangement where the reader most needs to be told
 * the choice exists - they are looking at a frame and cannot see that it was a choice at all.
 */
export function factionModes(
    frames: readonly EncyclopediaFactionFrame[],
): ChoiceOption<string>[] {
    if (frames.length === 0) {
        return [{
            id: '0',
            label: 'None',
            disabled: true,
            disabledReason: 'This card declares no faction frame, so there is none to draw.',
        }];
    }

    const lone = frames.length === 1;
    return frames.map(frame => ({
        id: String(frame.slot),
        label: frame.slotName ?? `Slot ${frame.slot}`,
        // The texture name is what an author searches their art folder for.
        title: `${frame.slotName ?? `Slot ${frame.slot}`} - ${frame.textureName}`,
        disabled: lone,
        disabledReason: lone
            ? `Only one faction frame is declared (${frame.textureName}), so there is nothing to `
            + 'switch to.'
            : undefined,
    }));
}

/** The slot a chosen option stands for; anything unreadable falls back to the first. */
export function factionSlotOf(id: string): number {
    const slot = Number.parseInt(id, 10);
    return Number.isNaN(slot) ? 0 : slot;
}

/**
 * Single-player against multiplayer body text.
 *
 * NEITHER is ever disabled, and that is deliberate. Driving this from the response's
 * `usedMultiplayerBody` is what broke the checkbox this replaces: choosing multiplayer on a unit
 * with no MP text snapped straight back, because the server had correctly answered with the
 * single-player body. What the reader ASKED to see and what the data could supply are two separate
 * facts; the second one is the notice beside the card, not a dead control.
 */
export const BODY_MODES: readonly ChoiceOption<BodyMode>[] = [
    {id: 'sp', label: 'Singleplayer', title: 'The Encyclopedia_Text body'},
    {id: 'mp', label: 'Multiplayer', title: 'The MP_Encyclopedia_Text body, where one is defined'},
];

export function bodyModeOf(multiplayer: boolean): BodyMode {
    return multiplayer ? 'mp' : 'sp';
}
