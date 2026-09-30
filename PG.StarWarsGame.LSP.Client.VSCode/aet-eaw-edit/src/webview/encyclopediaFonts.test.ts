// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {cssFontStack, isSubstitutedFont} from './encyclopediaFonts';

describe('cssFontStack', () => {
    // The body asks for Arial and now GETS Arial. It used to be remapped to Tahoma, chosen because
    // Arial "could not reproduce" the game's line breaks - but no font can, because the game wraps
    // on a character count and measures no glyphs at all. The remap was answering a question the
    // engine never asks, and it made every row render in the wrong face.
    it('draws Arial in Arial', () => {
        assert.match(cssFontStack('Arial'), /^'Arial'/);
    });

    // Whitespace would leave a family name no browser matches. Case is left exactly as the data
    // writes it, because CSS family matching is case-insensitive and rewriting it would only be
    // guessing at the modder's intent.
    it('trims surrounding whitespace and leaves case alone', () => {
        assert.match(cssFontStack('  aRiAl '), /^'aRiAl'/);
    });

    it('passes an unrecognised mod font through as asked', () => {
        assert.match(cssFontStack('Verdana'), /^'Verdana'/);
    });

    // Each weight now ASKS FOR ITSELF first - they are distinct installed faces, and a reader who
    // has them gets the right one - then shares the same fallback chain behind it. That chain is
    // what every weight used to return outright.
    it('asks for each EmpireAtWar weight by name, over a shared fallback', () => {
        const bold = cssFontStack('EmpireAtWar-Bold');
        const medium = cssFontStack('EmpireAtWar-Medium');

        assert.match(bold, /^'EmpireAtWar-Bold', 'EmpireAtWar'/);
        assert.match(medium, /^'EmpireAtWar-Medium', 'EmpireAtWar'/);
        assert.notEqual(bold, medium);

        for (const stack of [bold, medium, cssFontStack('EmpireAtWar-Light')]) {
            assert.match(stack, /'Trebuchet MS'/);
            assert.match(stack, /sans-serif$/);
        }
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
