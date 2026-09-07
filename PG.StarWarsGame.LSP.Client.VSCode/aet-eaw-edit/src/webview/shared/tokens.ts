// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The scale every panel in this extension measures itself against.
//
// Why this file exists. Before it, src/webview held 903 hardcoded px literals against three defined
// custom properties, and all three of those were tile dimensions - so every spacing decision was
// re-made at its call site from whatever happened to sit next to it. The measured result is what
// you would expect: nine different paddings for what is semantically one compact control, radius
// split 3px fourteen times against 4px thirteen times for the same intent, and five relative font
// sizes inside 0.12em of each other. None of that was carelessness about a design system. There was
// no design system to be careless about.
//
// The naming rule is that the name carries the value. --space-6 rather than --space-3 or
// --space-snug: a reader never has to know which step is third, or guess how snug snug is, and the
// names still sort. Colours are the exception and stay role-named, because a colour's value is the
// one thing that must be free to change per theme - those arrive with the colour block.
//
// How it reaches a panel. These are bare declarations, so interpolating this block at the top of a
// styled.div lands them on that Shell element and they cascade to everything under it. That is the
// same mechanism the tile properties already used, which is why moving them here changed nothing.
// It is also why this is CSS custom properties rather than a styled-components theme object: the
// shared chrome is consumed by class name from plain template strings, and a theme object would
// only ever reach the handful of real styled components.

/**
 * The scales. Every value here is free to move as a set - that is what makes it a scale.
 *
 * Steps were chosen from the measured distribution rather than invented: 4, 6 and 8 already carried
 * most of the spacing, so they are steps, and the strays either side snap to the nearest one.
 */
export const scaleCss = `
    /* Space. The dock is dense, so the scale is finer at the bottom than a 4-based scale would be:
       2 and 6 both carry real weight (74 and 103 sites) and dropping either would round a third of
       the dock's spacing to something visibly wrong. */
    /* The hairline: borders, outlines, and the one gap that is deliberately as tight as a gap can
       be - the mode toggles packed against each other inside the search box. */
    --space-1: 1px;
    --space-2: 2px;
    --space-4: 4px;
    --space-6: 6px;
    --space-8: 8px;
    --space-12: 12px;
    --space-16: 16px;
    --space-24: 24px;

    /* Radius. Three steps, down from eight distinct values. 2/3/4 were one intent expressed three
       ways and 5/6/8/9/10 another; only the circle is genuinely its own thing.

       The pill is not a step and is not a length: it is "as round as this box can be", which is what
       both a severity tag and the rounded end of a 2px tick mark are asking for. Written as a step,
       each of those breaks in its own direction - 6px squares off the tag, and 3px on a 2px-wide bar
       exceeds half its width and turns it into a lozenge. */
    --radius-3: 3px;
    --radius-6: 6px;
    --radius-pill: 999px;
    --radius-round: 50%;

    /* Icon sizes. A glyph box, NOT typography, even though the property setting it is font-size.
       These were on the type scale until it became clear that running them through it shrank every
       inline severity icon by 2px, and that a future change to the reading size would drag every
       icon along with it. 13px and 14px were the same intent one pixel apart. */
       Six steps for eight measured values is a thin consolidation, and deliberately so: an icon
       size tracks the size of the control holding it, and this extension genuinely has that many
       control sizes. Only the two clear drifts are folded - 13 into 14 and 15 into 16. The win here
       is not fewer numbers, it is that a change to the reading size no longer drags icons with it. */
    --icon-size-11: 11px;
    --icon-size-12: 12px;
    --icon-size-14: 14px;
    --icon-size-16: 16px;
    --icon-size-18: 18px;
    --icon-size-22: 22px;

    /* Type. 11px is the dock heading and 12px everything smaller than body - the five relative
       sizes between 0.8em and 0.92em were all reaching for the second of those.

       Body defers to the host rather than naming a number, because the reader may have set it. */
    --font-size-10: 10px;
    --font-size-11: 11px;
    --font-size-12: 12px;
    --font-size-body: var(--vscode-font-size, 13px);
    --font-size-16: 16px;

    /* The two steps that are not lengths, and the only place a name cannot carry its value.
       Across the panels these were written eight different ways - 0.75, 0.8, 0.82, 0.85, 0.88, 0.9
       and 0.92em - which is two intentions ("a little smaller" and "distinctly smaller") expressed
       once per call site. They stay relative rather than collapsing onto --font-size-11 and -12,
       because being relative is the point: a reader who raises the editor font size gets these with
       it, and a px step would pin them. 0.85 goes up to -smaller; 0.82 and below go to -smallest. */
    --font-size-smaller: 0.9em;
    --font-size-smallest: 0.8em;
`;

/**
 * Measurements that happen to be lengths, and are not on any scale.
 *
 * The distinction matters more than it looks. A scale step is chosen and can be re-chosen; each
 * value below was FITTED against a specific failure and rounding it to the nearest step re-opens
 * that failure. They are kept apart so that neither a reader nor the lint rule that will ban raw px
 * mistakes them for values that are free to move.
 */
export const fittedCss = `
    /* One tile size for every dock in the extension.
       The width has to leave room for the dock's own padding AND its vertical scrollbar, which is
       always there once the content is taller than the panel - 136px fitted the padding but not the
       scrollbar, so two tiles overflowed by a few pixels and the dock grew a horizontal one. */
    --dock-tile-w: 128px;
    /* Tall enough for the worst case a tile actually holds: a glyph, a label and the raw token
       badge under it. At 64px that combination overflowed and was clipped mid-word. */
    --dock-tile-h: 78px;
    /* On the scale, unlike its two neighbours: nothing was ever fitted about the gap, 6px is just
       the dock's ordinary rhythm. It sits here because this is where a consumer looks for it. */
    --dock-tile-gap: var(--space-6);

    /* Room at the trailing edge of a search box for the three mode toggles that overlay it: three
       buttons of 22px plus their padding, measured, not chosen. The scale's nearest step is 24px,
       which would run the search text under the buttons. */
    --search-modes-inset: 78px;

    /* The 3D viewport's own ground, and the one colour in the layer that deliberately does NOT
       follow the theme. The canvas has to paint something: left transparent it borrows the panel
       background, an unlit hull on a dark theme goes invisible, and a blend punches black through
       the page. Themed, it would do the first of those every time the reader picked a light skin.
       A colour token would be the wrong home for it - what it is, is a fitted measurement that
       happens to be a colour. */
    --colour-scene-ground: #1b1d21;
`;

/**
 * One colour role: the name panels use, the host variable behind it, and the literal to fall back
 * on when the host has not defined that variable.
 *
 * Kept as data rather than as a block of CSS text because two consumers need it in two shapes. The
 * panels want the declarations; a canvas wants a concrete colour, since a 2D context cannot take a
 * var(). Generating both from one list is what stops the second from drifting out of step with the
 * first - which is precisely what had happened four times over in the story graph.
 */
export interface ColourToken {
    /** The role name panels write, e.g. --colour-muted. */
    readonly name: string;
    /** The host variable it defers to. In an IntelliJ port this is the column that changes. */
    readonly from: string;
    /** Used when the host defines nothing. Always the value VS Code's own dark theme ships. */
    readonly fallback: string;
}

/**
 * Every colour the layer publishes, and with it every host variable the webviews depend on.
 *
 * Roles, not values: a colour is the one thing on a themed surface that must be free to change, so
 * naming it for its value the way --space-6 is named would be actively wrong.
 */
export const colourTokens: readonly ColourToken[] = [
    // Text.
    { name: '--colour-ink', from: '--vscode-foreground', fallback: '#cccccc' },
    { name: '--colour-muted', from: '--vscode-descriptionForeground', fallback: '#999999' },
    { name: '--colour-faint', from: '--vscode-disabledForeground', fallback: '#888888' },
    { name: '--colour-editor-ink', from: '--vscode-editor-foreground', fallback: '#cccccc' },

    /*
     * A dock section heading. Deliberately its own role rather than the panels naming textLink
     * directly, because the value here was measured and the obvious alternatives lose.
     *
     * Read out of the shipped themes: panelTitle.activeForeground and sideBarSectionHeader.foreground
     * are IDENTICAL to `foreground` in every default theme that defines them at all, and most do
     * not define them - so either would make a heading exactly the colour of the body text under
     * it. textLink.foreground is distinct from the body in every theme and lands between 5.2:1 and
     * 7.2:1 against the dock's ground, which is legible in all of them.
     *
     * That it is semantically the LINK role is the one real objection, and this token is the answer
     * to it: the stylesheet now says what the colour is for, and swapping it later is one line here
     * rather than a hunt through six files.
     */
    { name: '--colour-heading', from: '--vscode-textLink-foreground', fallback: '#4daafc' },
    { name: '--colour-heading-hover', from: '--vscode-textLink-activeForeground', fallback: '#6fb3ff' },

    // Grounds.
    { name: '--colour-editor-ground', from: '--vscode-editor-background', fallback: '#1e1e1e' },
    { name: '--colour-panel', from: '--vscode-editorWidget-background', fallback: '#252526' },
    { name: '--colour-sidebar', from: '--vscode-sideBar-background', fallback: '#252526' },
    { name: '--colour-input', from: '--vscode-input-background', fallback: '#3c3c3c' },
    { name: '--colour-input-ink', from: '--vscode-input-foreground', fallback: '#cccccc' },

    // Edges.
    { name: '--colour-line', from: '--vscode-panel-border', fallback: '#454545' },
    { name: '--colour-line-strong', from: '--vscode-widget-border', fallback: '#454545' },
    { name: '--colour-input-line', from: '--vscode-input-border', fallback: '#3c3c3c' },
    { name: '--colour-focus', from: '--vscode-focusBorder', fallback: '#007fd4' },

    // Actions.
    { name: '--colour-action', from: '--vscode-button-background', fallback: '#0e639c' },
    { name: '--colour-action-ink', from: '--vscode-button-foreground', fallback: '#ffffff' },
    { name: '--colour-action-2', from: '--vscode-button-secondaryBackground', fallback: '#3a3d41' },
    { name: '--colour-action-2-ink', from: '--vscode-button-secondaryForeground', fallback: '#cccccc' },
    { name: '--colour-hover', from: '--vscode-list-hoverBackground', fallback: '#2a2d2e' },
    { name: '--colour-selected', from: '--vscode-list-activeSelectionBackground', fallback: '#04395e' },
    { name: '--colour-toolbar-hover', from: '--vscode-toolbar-hoverBackground', fallback: '#5a5d5e' },

    // Severity. Separate from the data hues on purpose: these carry a meaning, and a chart series
    // that happens to be red does not.
    { name: '--colour-critical', from: '--vscode-errorForeground', fallback: '#f14c4c' },
    { name: '--colour-warning', from: '--vscode-editorWarning-foreground', fallback: '#cca700' },

    // Data. The theme's own chart hues, which is what a lane or a branch is choosing between.
    { name: '--colour-data-blue', from: '--vscode-charts-blue', fallback: '#3794ff' },
    { name: '--colour-data-green', from: '--vscode-charts-green', fallback: '#89d185' },
    { name: '--colour-data-orange', from: '--vscode-charts-orange', fallback: '#d18616' },
    { name: '--colour-data-purple', from: '--vscode-charts-purple', fallback: '#b180d7' },
    { name: '--colour-data-red', from: '--vscode-charts-red', fallback: '#f14c4c' },
    { name: '--colour-data-yellow', from: '--vscode-charts-yellow', fallback: '#cca700' },
    { name: '--colour-data-neutral', from: '--vscode-charts-foreground', fallback: '#e7e7e7' },
];

/** The colour block, generated from the list above so the two can never disagree. */
export const colourCss = `\n${colourTokens
    .map(token => `    ${token.name}: var(${token.from}, ${token.fallback});`)
    .join('\n')}\n`;

/** The whole layer, for a panel that wants one interpolation rather than three. */
export const tokensCss = `${scaleCss}${colourCss}${fittedCss}`;

/**
 * The same layer at the document root, for the two places a Shell cannot reach.
 *
 * Custom properties are inherited down the DOM, not applied by whichever stylesheet declared the
 * rule - so interpolating the layer into a Shell covers everything inside that Shell and nothing
 * else. Two things sit outside it: a global stylesheet's rules for `html, body, #root`, which are
 * the Shell's ANCESTORS, and anything portalled to the body, which is InfoBadge's tooltip. Both
 * would resolve every var() to nothing, and a padding that resolves to nothing is not a fallback -
 * it is zero.
 *
 * Same string, two mount points. Declaring it twice on one page is harmless; declaring it in two
 * PLACES in the source would be the duplication this whole layer exists to remove.
 */
export const tokensRootCss = `:root {${tokensCss}}`;
