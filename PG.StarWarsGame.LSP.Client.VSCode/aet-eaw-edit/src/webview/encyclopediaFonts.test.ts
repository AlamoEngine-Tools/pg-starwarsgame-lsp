// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { cssFontStack, isSubstitutedFont } from './encyclopediaFonts';

describe('cssFontStack', () => {
    it('remaps Arial to Tahoma, which is what the game actually wraps with', () => {
        // Not cosmetic: in Arial no width reproduces the game's line breaks at all.
        assert.match(cssFontStack('Arial'), /^'Tahoma'/);
    });

    it('remaps Arial case-insensitively and ignores surrounding whitespace', () => {
        assert.match(cssFontStack('  aRiAl '), /^'Tahoma'/);
    });

    it('passes an unrecognised mod font through as asked', () => {
        assert.match(cssFontStack('Verdana'), /^'Verdana'/);
    });

    it('substitutes every EmpireAtWar weight with one stack', () => {
        // The -Bold/-Medium/-Light suffix is a weight; fontOf applies that separately, so the
        // family stack must not differ between them.
        const medium = cssFontStack('EmpireAtWar-Medium');
        assert.equal(cssFontStack('EmpireAtWar-Bold'), medium);
        assert.equal(cssFontStack('EmpireAtWar-Light'), medium);
        assert.match(medium, /^'Trebuchet MS'/);
    });
});

describe('isSubstitutedFont', () => {
    it('flags the EmpireAtWar family whatever the weight or case', () => {
        assert.equal(isSubstitutedFont('EmpireAtWar-Bold'), true);
        assert.equal(isSubstitutedFont('empireatwar-medium'), true);
        // The shipped data contains a lowercase "EmpireAtWar-light" typo; it is the same font.
        assert.equal(isSubstitutedFont('EmpireAtWar-light'), true);
        assert.equal(isSubstitutedFont('EmpireAtWar'), true);
    });

    it('does not flag fonts the preview can actually draw with', () => {
        assert.equal(isSubstitutedFont('Arial'), false);
        assert.equal(isSubstitutedFont('Tahoma'), false);
        // Substring, not prefix: a mod font merely CONTAINING the name is a different family.
        assert.equal(isSubstitutedFont('MyEmpireAtWarClone'), false);
    });
});
