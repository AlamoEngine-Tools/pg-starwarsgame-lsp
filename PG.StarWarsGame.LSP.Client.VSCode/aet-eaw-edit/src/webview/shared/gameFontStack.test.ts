// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';
import {gameFontStack} from './gameFontStack';

describe('gameFontStack', () => {
    // The rule in one line: ask for what the data names, then degrade.
    it('asks for the named face first', () => {
        assert.match(gameFontStack('Arial', ['Tahoma']), /^'Arial', /);
    });

    // A face we cannot ship is still named. Naming costs nothing and ships nothing, and a reader
    // who installed it themselves then sees the real thing instead of our approximation.
    it('names a face the preview could never ship, and falls back behind it', () => {
        const stack = gameFontStack('EmpireAtWar-Bold', ['Trebuchet MS', 'Segoe UI']);

        assert.match(stack, /^'EmpireAtWar-Bold'/);
        assert.match(stack, /'Trebuchet MS'/);
        assert.match(stack, /sans-serif$/);
    });

    // An installed copy may report either the full name or the base family with a subfamily, and
    // CSS matches on family, so both are listed.
    it('also asks for the base family of a hyphenated name', () => {
        const stack = gameFontStack('EmpireAtWar-Medium', ['Tahoma']);

        assert.equal(stack.indexOf("'EmpireAtWar-Medium'") < stack.indexOf("'EmpireAtWar'"), true);
        assert.match(stack, /'EmpireAtWar'/);
    });

    it('does not repeat a name that has no suffix to strip', () => {
        const stack = gameFontStack('EmpireAtWar', ['Tahoma']);

        assert.equal(stack.match(/'EmpireAtWar'/g)?.length, 1);
    });

    it('keeps the fallbacks in the order given', () => {
        assert.equal(
            gameFontStack('X', ['A', 'B', 'C']), "'X', 'A', 'B', 'C', sans-serif");
    });

    // The game names Windows faces; the editor is not Windows-only. Without a metric twin the
    // stack falls through to the generic on Linux - typically DejaVu Sans, appreciably wider than
    // Arial - and text stops fitting a card whose proportions were calibrated against the real
    // face.
    it('offers a metric twin for a Windows-only face, behind the real one', () => {
        const stack = gameFontStack('Arial', []);

        assert.match(stack, /^'Arial'/);
        assert.match(stack, /'Liberation Sans'/);
        assert.equal(stack.indexOf("'Arial'") < stack.indexOf("'Liberation Sans'"), true);
    });

    it('offers twins for the fallbacks too, since those are Windows faces as well', () => {
        assert.match(gameFontStack('EmpireAtWar-Bold', ['Tahoma']), /'DejaVu Sans'/);
    });

    it('never lists the same family twice, whatever route it arrives by', () => {
        const stack = gameFontStack('Arial', ['Helvetica', 'Arial']);

        assert.equal(stack.match(/'Arial'/g)?.length, 1);
        assert.equal(stack.match(/'Liberation Sans'/g)?.length, 1);
    });

    it('leaves a face nobody has a twin for alone', () => {
        assert.equal(gameFontStack('SomeModFont', []), "'SomeModFont', sans-serif");
    });

    it('trims what the data wrote', () => {
        assert.match(gameFontStack('  Arial  ', []), /^'Arial', /);
    });

    // A font name is data from a mod's XML; a stray quote must not be able to close the CSS string
    // and inject a declaration.
    it('drops quotes out of a font name rather than emitting them', () => {
        assert.equal(gameFontStack("Ari'al", []).includes("Ari'al"), false);
        assert.match(gameFontStack("Ari'al", []), /^'Arial', /);
    });

    it('asks for nothing of its own when the data names nothing', () => {
        assert.match(gameFontStack('   ', ['Tahoma']), /^'Tahoma', /);
    });

    it('is only the generic when there is nothing at all to ask for', () => {
        assert.equal(gameFontStack('   ', []), 'sans-serif');
    });
});
