// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { EncyclopediaIcon, EncyclopediaIconSource } from '../protocol/encyclopedia';
import { encyclopediaIconTitle } from './encyclopediaIconTitle';

function icon(
    source: EncyclopediaIconSource,
    isMegaTextureStale = false,
    name = 'I_BUTTON_LUKE.TGA',
): EncyclopediaIcon {
    return {
        name, dataUri: 'data:image/png;base64,AAAA', source, isMegaTextureStale,
        width: 50, height: 50,
    };
}

describe('encyclopediaIconTitle: provenance', () => {
    it('names the project mega texture as the source', () => {
        const title = encyclopediaIconTitle(icon('WorkspaceMegaTexture'));
        assert.match(title, /^I_BUTTON_LUKE\.TGA/);
        assert.match(title, /this project's mega texture/);
    });

    it('names a raw source image', () => {
        assert.match(encyclopediaIconTitle(icon('LooseSource')), /source image in this project/);
    });

    it('names the base game', () => {
        assert.match(encyclopediaIconTitle(icon('Baseline')), /from the base game/);
    });
});

describe('encyclopediaIconTitle: the stale case', () => {
    // The whole point of the message: the art is not missing, it just is not packed yet, and the
    // consequence the author cares about is that the GAME will not show it.
    it('explains that the game will not show an unrepacked icon', () => {
        const title = encyclopediaIconTitle(icon('LooseSource', true));
        assert.match(title, /does not contain it yet/);
        assert.match(title, /the game will not show it/);
    });

    // Staleness outranks the plain source wording - otherwise it would read as an ordinary hit.
    it('takes precedence over the source wording', () => {
        assert.doesNotMatch(
            encyclopediaIconTitle(icon('LooseSource', true)),
            /source image in this project$/,
        );
    });
});

describe('encyclopediaIconTitle: no icon', () => {
    for (const absent of [null, undefined]) {
        it(`reports plainly when the icon is ${absent}`, () => {
            assert.equal(encyclopediaIconTitle(absent), 'No icon resolved for this object');
        });
    }
});
