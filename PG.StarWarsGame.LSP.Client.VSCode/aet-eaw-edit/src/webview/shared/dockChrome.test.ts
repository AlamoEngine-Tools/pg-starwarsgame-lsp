// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { dockChromeCss, rotarySwitchCss } from './dockChrome';

/** Every class this stylesheet defines a rule for. */
function selectorsIn(css: string): Set<string> {
    return new Set([...css.matchAll(/\.([a-zA-Z][\w-]*)\s*(?=[,{:.\s])/g)].map(m => m[1]));
}

describe('dockChromeCss', () => {
    /**
     * Regression guard for a collision that cost a full round of debugging.
     *
     * The dock's width sash owns a bare `.resize-handle` in locGridStyles, which is interpolated
     * after this stylesheet at equal specificity - so when the modal's grips shared that name, the
     * sash's rule won and flattened all eight of them into its own 6px full-height strip on the
     * left edge. Every handle then sat on top of the others in one place, and the dialog behaved as
     * though only its left border could be resized.
     */
    it('does not use the bare resize-handle class the dock sash already owns', () => {
        assert.ok(!selectorsIn(dockChromeCss).has('resize-handle'));
    });

    it('namespaces every modal grip so the sash rule cannot reach them', () => {
        const selectors = selectorsIn(dockChromeCss);

        for (const direction of ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw']) {
            assert.ok(selectors.has(`modal-resize-${direction}`), `missing modal-resize-${direction}`);
            assert.ok(!selectors.has(`resize-${direction}`), `unnamespaced resize-${direction}`);
        }
    });

    // The box must not scroll: the grips are positioned against it, so a scrolling box would carry
    // them out of view and clip anything overhanging its edges.
    it('keeps the dialog box from scrolling, so the body scrolls instead', () => {
        assert.match(dockChromeCss, /\.modal\s*\{[^}]*overflow:\s*hidden/);
        assert.match(dockChromeCss, /\.modal-body\s*\{[^}]*overflow-y:\s*auto/);
    });

    // Without border-box the hook writes back a border-box measurement as a content-box width, and
    // the dialog grows by its own padding and border the first time it is touched.
    it('sizes the dialog as a border box, since this webview has no global reset', () => {
        assert.match(dockChromeCss, /\.modal\s*\{[^}]*box-sizing:\s*border-box/);
    });

    /**
     * The readout beside a field label has to reach the far edge.
     *
     * With the label as a plain span the count simply followed the text with nothing between them,
     * and the preview's whole settings pane read as a set of typos: "Wind speed1.0", "Light
     * around45 deg", "Speed1.00x". A section title already solves this the same way.
     */
    it('pushes a field label`s count to the far edge', () => {
        assert.match(dockChromeCss, /\.field-label\s*\{[^}]*display:\s*flex/);
        assert.match(dockChromeCss, /\.field-label \.section-count\s*\{[^}]*margin-left:\s*auto/);
    });

    /**
     * `.field-note` was in the markup of six settings with no rule anywhere for it, so every
     * explanatory sentence rendered at full body weight and drowned the control it described.
     */
    it('styles the note under a field as support text, not as content', () => {
        assert.match(dockChromeCss, /\.field-note\s*\{[^}]*descriptionForeground/);
    });

    /**
     * The warning variant of the same sentence. It shares every other property with `.field-note`
     * so that it stays the same kind of text in the same place - a note that changed size or weight
     * when it had something to warn about would make the panel jump.
     */
    it('gives a field note a warning colour without changing anything else about it', () => {
        assert.match(dockChromeCss, /\.field-warn\s*\{[^}]*editorWarning-foreground/);
        assert.match(dockChromeCss, /\.field-warn\s*\{[^}]*font-size:\s*0\.9em/);
    });

    /** An action in a settings pane needs a surface. A borderless glyph reads as an ornament. */
    it('lays a button out as a row, so it can carry a glyph beside its label', () => {
        assert.match(dockChromeCss, /\.btn\s*\{[^}]*display:\s*inline-flex/);
    });
});

describe('rotarySwitchCss', () => {
    /**
     * A position must not touch the readout.
     *
     * `RADIUS` in `RotaryModeSwitch` is the distance between their centres, so the clearance is
     * that minus both radii. At 34 against a 40px readout with a 2px border and a 24px position
     * the two overlapped, and the arc read as one lumpy shape rather than as three choices.
     */
    it('keeps the positions clear of the readout', () => {
        const size = (selector: string): number => {
            const rule = new RegExp(`\\.${selector}\\s*\\{[^}]*width:\\s*(\\d+)px`).exec(
                rotarySwitchCss);
            assert.ok(rule !== null, `no width for .${selector}`);
            return Number(rule[1]);
        };

        // Kept in step with RADIUS by hand; the constant is private to the component.
        const radius = 40;
        const border = 2;
        const clearance = radius - (size('rotary-center') / 2 + border) - size('rotary-pos') / 2;

        assert.ok(clearance > 0, `positions overlap the readout by ${-clearance}px`);
    });

    /** A glyph that fills its circle stops the circle reading as a switch position. */
    it('leaves a ring of ground around every glyph', () => {
        const glyph = /\.rotary-pos \.codicon\s*\{[^}]*font-size:\s*(\d+)px/.exec(rotarySwitchCss);
        const face = /\.rotary-pos\s*\{[^}]*width:\s*(\d+)px/.exec(rotarySwitchCss);

        assert.ok(glyph !== null && face !== null);
        assert.ok(Number(glyph[1]) / Number(face[1]) < 0.5,
            'the glyph covers half its button or more');
    });
});
