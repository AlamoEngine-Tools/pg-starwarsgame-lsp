// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';
import type {EncyclopediaFactionFrame} from '../protocol/encyclopedia';
import {BODY_MODES, bodyModeOf, factionModes, factionSlotOf} from './encyclopediaModes';

function frame(slot: number, slotName: string | null): EncyclopediaFactionFrame {
    return {
        slot,
        textureName: `i_tooltip_${slotName ?? slot}_frame.tga`,
        slotName,
        image: {dataUri: 'data:image/png;base64,AA', width: 4, height: 4},
    };
}

describe('factionModes', () => {
    it('offers one option per declared frame, named by its slot', () => {
        const modes = factionModes([frame(0, 'Rebel'), frame(1, 'Empire')]);

        assert.deepEqual(modes.map(m => m.label), ['Rebel', 'Empire']);
        assert.deepEqual(modes.map(m => m.id), ['0', '1']);
        assert.equal(modes.every(m => m.disabled !== true), true);
    });

    // A slot the data does not name is still a slot - the card draws it, so it must be selectable.
    it('falls back to the slot number when nothing names it', () => {
        assert.equal(factionModes([frame(0, 'Rebel'), frame(1, null)])[1].label, 'Slot 1');
    });

    // Disable, do not hide: the control used to vanish below two frames, which left the reader with
    // no way to learn that the choice exists at all.
    it('keeps a lone frame visible but disabled, and says why', () => {
        const modes = factionModes([frame(0, 'Rebel')]);

        assert.equal(modes.length, 1);
        assert.equal(modes[0].disabled, true);
        assert.match(modes[0].disabledReason ?? '', /only one/i);
    });

    it('still shows something when the card declares no frames at all', () => {
        const modes = factionModes([]);

        assert.equal(modes.length, 1);
        assert.equal(modes[0].disabled, true);
        assert.match(modes[0].disabledReason ?? '', /no faction frame/i);
    });

    // The texture name is what an author greps for, so it belongs in the tooltip.
    it('names the texture in the tooltip', () => {
        assert.match(factionModes([frame(0, 'Rebel'), frame(1, 'Empire')])[0].title ?? '',
            /i_tooltip_Rebel_frame\.tga/);
    });
});

describe('factionSlotOf', () => {
    it('reads the slot back out of the chosen id', () => {
        assert.equal(factionSlotOf('1'), 1);
    });

    it('falls back to the first slot for anything unreadable', () => {
        assert.equal(factionSlotOf('nonsense'), 0);
    });
});

describe('BODY_MODES', () => {
    it('offers single-player and multiplayer', () => {
        assert.deepEqual(BODY_MODES.map(m => m.id), ['sp', 'mp']);
    });

    /**
     * Neither option is ever disabled, and that is load-bearing. Driving this from the response's
     * `usedMultiplayerBody` is what broke the old checkbox: choosing multiplayer on a unit with no
     * MP text immediately snapped back, because the server correctly answered with the
     * single-player body. What the user ASKED for and what the data could give are two facts, and
     * the notice beside the card is where the second one belongs.
     */
    it('never disables either option', () => {
        assert.equal(BODY_MODES.every(m => m.disabled !== true), true);
    });
});

describe('bodyModeOf', () => {
    it('maps the request both ways', () => {
        assert.equal(bodyModeOf(true), 'mp');
        assert.equal(bodyModeOf(false), 'sp');
    });
});
