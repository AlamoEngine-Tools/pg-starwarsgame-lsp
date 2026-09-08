// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { dockChromeCss } from './dockChrome';
import { colourCss, colourTokens, fittedCss, scaleCss, tokensCss, tokensRootCss } from './tokens';

/**
 * Every custom property a block declares, as name to value.
 *
 * Only bare declarations at the top of a block count - a `--name:` inside a rule body would be
 * scoped to that selector rather than to the Shell, and is not a token.
 */
function declarationsIn(css: string): Map<string, string> {
    const found = new Map<string, string>();

    for (const line of css.split('\n')) {
        const match = /^\s*(--[a-z0-9-]+)\s*:\s*([^;]+);/.exec(line);

        if (match) {
            found.set(match[1], match[2].trim());
        }
    }

    return found;
}

describe('the space scale', () => {
    // The name carries the value, so a reader never has to know which step is third. Steps were
    // chosen from the measured distribution across src/webview: 4, 6 and 8 already dominate, and
    // the strays either side of them snap to the nearest.
    it('defines every step, named for its own value', () => {
        const tokens = declarationsIn(scaleCss);

        for (const px of [1, 2, 4, 6, 8, 12, 16, 24]) {
            assert.equal(tokens.get(`--space-${px}`), `${px}px`, `--space-${px}`);
        }
    });

    it('has no two steps sharing a value, which would make one of them unreachable', () => {
        const steps = [...declarationsIn(scaleCss)]
            .filter(([name]) => name.startsWith('--space-'))
            .map(([, value]) => value);

        assert.equal(new Set(steps).size, steps.length);
    });
});

describe('the radius and type scales', () => {
    it('collapses radius to three, since 3-vs-4 and 8-vs-9-vs-10 were the same intent twice', () => {
        const tokens = declarationsIn(scaleCss);

        assert.equal(tokens.get('--radius-3'), '3px');
        assert.equal(tokens.get('--radius-6'), '6px');
        assert.equal(tokens.get('--radius-round'), '50%');
    });

    it('defines the type scale, with the body size deferring to the host', () => {
        const tokens = declarationsIn(scaleCss);

        assert.equal(tokens.get('--font-size-10'), '10px');
        assert.equal(tokens.get('--font-size-11'), '11px');
        assert.equal(tokens.get('--font-size-12'), '12px');
        assert.equal(tokens.get('--font-size-16'), '16px');
        assert.match(tokens.get('--font-size-body') ?? '', /var\(--vscode-font-size/);
    });

    /**
     * Relative, and staying that way. These are the sizes a reader who has raised the editor font
     * should get raised along with it; a px step would pin them at whatever looked right here.
     */
    it('keeps the two smaller sizes relative rather than pinning them', () => {
        const tokens = declarationsIn(scaleCss);

        assert.equal(tokens.get('--font-size-smaller'), '0.9em');
        assert.equal(tokens.get('--font-size-smallest'), '0.8em');
    });

    /**
     * An icon size is a glyph box, not typography. Running these through the type scale shrank every
     * inline severity icon by 2px, and would have let a change to the reading size resize icons.
     */
    it('sizes icons on their own scale, apart from the type scale', () => {
        const tokens = declarationsIn(scaleCss);

        for (const px of [11, 12, 14, 16, 18, 22]) {
            assert.equal(tokens.get(`--icon-size-${px}`), `${px}px`, `--icon-size-${px}`);
        }
    });

    /**
     * The pill is not a length and must not be read as one. Both the severity tag and the rounded
     * end of a 2px tick mark ask for "as round as this box can be", and each breaks in its own
     * direction under a step - 6px squares the tag off, 3px turns the 2px bar into a lozenge.
     */
    it('offers a pill radius, which is not a step on the radius scale', () => {
        const tokens = declarationsIn(scaleCss);

        assert.equal(tokens.get('--radius-pill'), '999px');
        assert.ok(Number(tokens.get('--radius-6')?.replace('px', '')) < 999);
    });
});

describe('tokensRootCss', () => {
    /**
     * Custom properties are inherited down the DOM, not applied by whichever stylesheet declared
     * the rule. A Shell-scoped layer therefore misses two things: a global stylesheet's rules for
     * `html, body, #root`, which are the Shell's ancestors, and anything portalled to the body,
     * which is InfoBadge's tooltip. Both would resolve every var() to nothing - and a padding that
     * resolves to nothing is zero, not a fallback.
     */
    it('puts the same layer on the root, from the same string', () => {
        assert.ok(tokensRootCss.startsWith(':root {'));
        assert.ok(tokensRootCss.includes(scaleCss));
        assert.ok(tokensRootCss.includes(colourCss));
        assert.ok(tokensRootCss.includes(fittedCss));
    });
});

describe('the fitted block', () => {
    /**
     * These are measurements, not scale steps, and rounding them to the scale re-opens a closed
     * bug in each case: 136px overflowed once the dock grew a scrollbar, and 64px clipped a tile's
     * token badge mid-word. They live apart from the scale so that neither a reader nor the lint
     * rule mistakes them for values free to move.
     */
    it('keeps the two tile dimensions at exactly the values that were fitted', () => {
        const tokens = declarationsIn(fittedCss);

        assert.equal(tokens.get('--dock-tile-w'), '128px');
        assert.equal(tokens.get('--dock-tile-h'), '78px');
    });

    /**
     * The viewport ground is a fitted measurement that happens to be a colour, so it belongs here
     * rather than among the roles: theming it would make an unlit hull invisible on a light skin,
     * which is the bug the literal was there to prevent.
     */
    it('holds the scene ground as a literal, outside the themed colour roles', () => {
        assert.equal(declarationsIn(fittedCss).get('--colour-scene-ground'), '#1b1d21');
        assert.ok(!colourTokens.some(t => t.name === '--colour-scene-ground'));
    });

    it('holds nothing from the scale, so a raw px here is always deliberate', () => {
        for (const name of declarationsIn(fittedCss).keys()) {
            assert.ok(
                !/^--(space|radius|font-size)-/.test(name),
                `${name} is a scale step and does not belong in the fitted block`,
            );
        }
    });

    // The gap is on the scale even though its neighbours are not - 6px is the dock's ordinary
    // rhythm and nothing was ever fitted about it. It sits with the tile tokens because that is
    // where a consumer looks for it.
    it('takes the tile gap from the scale rather than restating it', () => {
        assert.equal(declarationsIn(fittedCss).get('--dock-tile-gap'), 'var(--space-6)');
    });
});

describe('the colour block', () => {
    /**
     * Colour is the one scale where the name cannot carry the value, so these are role names. It is
     * also the whole of the host coupling: every --vscode-* reference in the layer is here, which is
     * what makes an IntelliJ port a matter of redefining one block.
     */
    it('routes every token through a host variable with a literal fallback', () => {
        assert.ok(colourTokens.length > 0);

        for (const token of colourTokens) {
            assert.match(token.name, /^--colour-[a-z0-9-]+$/, token.name);
            assert.match(token.from, /^--vscode-[a-zA-Z0-9.-]+$/, token.name);
            assert.match(token.fallback, /^#[0-9a-f]{6}$/, `${token.name} fallback`);
        }
    });

    it('names each role once', () => {
        const names = colourTokens.map(t => t.name);
        assert.equal(new Set(names).size, names.length);
    });

    /**
     * The CSS is generated from the same list the canvas resolver reads, rather than written out
     * beside it. A second hand-maintained copy is exactly the defect this chunk removes from the
     * story graph, and there is no reason to introduce it here on the way.
     */
    it('generates its CSS from the list, so the two cannot drift', () => {
        for (const token of colourTokens) {
            assert.ok(
                colourCss.includes(`${token.name}: var(${token.from}, ${token.fallback});`),
                token.name,
            );
        }
    });

    it('publishes the chart hues the story graph indexes by hash', () => {
        const names = new Set(colourTokens.map(t => t.name));

        for (const hue of ['blue', 'green', 'orange', 'purple', 'red', 'yellow', 'neutral']) {
            assert.ok(names.has(`--colour-data-${hue}`), hue);
        }
    });
});

describe('reaching the panels', () => {
    it('combines every block, so one interpolation carries the whole layer', () => {
        assert.ok(tokensCss.includes(scaleCss));
        assert.ok(tokensCss.includes(fittedCss));
        assert.ok(tokensCss.includes(colourCss));
    });

    /**
     * Compatibility for chunk 1: the tile properties moved out of dockChromeCss, and all five of
     * its consumers have to keep getting them without being edited. Interpolating the layer at the
     * top of that block puts the declarations exactly where they already were.
     */
    it('is carried by dockChromeCss, which every dock already interpolates', () => {
        const tokens = declarationsIn(dockChromeCss);

        assert.equal(tokens.get('--space-6'), '6px');
        assert.equal(tokens.get('--dock-tile-w'), '128px');
    });

    it('declares each tile property once, so the move did not leave a copy behind', () => {
        for (const name of ['--dock-tile-w', '--dock-tile-h', '--dock-tile-gap']) {
            const occurrences = dockChromeCss.split(`${name}:`).length - 1;
            assert.equal(occurrences, 1, `${name} is declared ${occurrences} times`);
        }
    });
});
