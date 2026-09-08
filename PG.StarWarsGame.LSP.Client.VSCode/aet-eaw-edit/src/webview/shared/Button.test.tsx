// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { renderToStaticMarkup } from 'react-dom/server';

import { Button, IconButton } from './Button';

const render = (element: React.JSX.Element): string => renderToStaticMarkup(element);

describe('Button', () => {
    it('is a button that does not submit anything, with its label inside', () => {
        const html = render(
            <Button title="Frame the model" onClick={() => undefined}>Frame</Button>);

        assert.match(html, /<button[^>]*type="button"/);
        assert.match(html, /class="btn"/);
        assert.match(html, /Frame</);
    });

    /**
     * A labelled button already names itself. Requiring a title would put "Cancel" on a control
     * that reads Cancel, which tells the reader nothing and reads as noise on hover.
     */
    it('needs no title, because its label is its name', () => {
        const html = render(<Button onClick={() => undefined}>Cancel</Button>);

        assert.equal(/title=/.test(html), false, html);
        assert.match(html, /Cancel</);
    });

    /**
     * A dialog's confirm button is usually inside a <form>, where Enter in a text field has to
     * submit it. Defaulting every button to type="button" is right - a stray submit is how a menu
     * button reloads a page - but the one that IS the submit has to be able to say so.
     */
    it('can be the submit for the form it sits in', () => {
        const html = render(<Button submit onClick={() => undefined}>Add</Button>);

        assert.match(html, /type="submit"/);
    });

    it('is not a submit unless asked, so a stray button cannot post a form', () => {
        const html = render(<Button onClick={() => undefined}>Cancel</Button>);

        assert.match(html, /type="button"/);
    });

    it('takes an editor-specific variant alongside the base class', () => {
        const html = render(
            <Button onClick={() => undefined} className="primary">Fill</Button>);

        assert.match(html, /class="btn primary"/);
    });

    it('takes the compact variant without losing the base class', () => {
        const html = render(
            <Button title="Copy" onClick={() => undefined} compact>Copy</Button>);

        assert.match(html, /class="btn compact"/);
    });

    /**
     * Disable, don't hide - and say why.
     *
     * A control with nothing to act on stays on screen, because the reader needs to see that the
     * choice exists; what it must not do is sit there mute. The reason replaces the title rather
     * than joining it: the title of an unusable control describes something it cannot do.
     */
    it('shows the reason it cannot be pressed instead of what it would have done', () => {
        const html = render(
            <Button
                title="Copy this shot as a camera key"
                onClick={() => undefined}
                disabled
                disabledReason="No model is loaded"
            >
                Copy
            </Button>);

        assert.match(html, /disabled/);
        assert.match(html, /title="No model is loaded"/);
        assert.equal(/camera key/.test(html), false, html);
    });

    /**
     * `disabled` is a runtime expression at every real call site, so the reason has to be owed for
     * the state existing at all rather than for it being on right now. While the control is usable
     * its title still describes what pressing it does.
     */
    it('shows what it does while it is still usable', () => {
        const html = render(
            <Button
                title="Play"
                onClick={() => undefined}
                disabled={false}
                disabledReason="Pick a clip below to play it"
            >
                Play
            </Button>);

        assert.match(html, /title="Play"/);
        assert.equal(/disabled/.test(html), false, html);
    });

    it('keeps the control on screen when disabled, rather than dropping it', () => {
        const html = render(
            <Button title="Copy" onClick={() => undefined} disabled disabledReason="Nothing to copy">
                Copy
            </Button>);

        assert.match(html, /Copy</);
    });
});

describe('IconButton', () => {
    it('draws its glyph and carries the title as its accessible name', () => {
        const html = render(
            <IconButton icon="close" title="Close" onClick={() => undefined} />);

        assert.match(html, /class="icon-btn"/);
        assert.match(html, /aria-label="Close"/);
        assert.match(html, /<svg|codicon/);
    });

    /**
     * An icon-only control has no text to fall back on, so the title is the only thing naming it -
     * to a reader hovering it and to a screen reader both. It is required, not optional.
     */
    it('names itself for a screen reader as well as for the pointer', () => {
        const html = render(
            <IconButton icon="download" title="Fetch the shader sources" onClick={() => undefined} />);

        assert.match(html, /title="Fetch the shader sources"/);
        assert.match(html, /aria-label="Fetch the shader sources"/);
    });

    it('reports why it cannot be pressed, the same way a labelled button does', () => {
        const html = render(
            <IconButton
                icon="capture"
                title="Save a still"
                onClick={() => undefined}
                disabled
                disabledReason="The scene has not finished loading"
            />);

        assert.match(html, /disabled/);
        assert.match(html, /title="The scene has not finished loading"/);
        assert.match(html, /aria-label="The scene has not finished loading"/);
    });

    /**
     * A toggle is not an action. `aria-pressed` is what tells a screen reader that this control has
     * a state rather than performing a one-off, and every call site that wrote it by hand was one
     * that could have forgotten to.
     */
    it('reports a toggle state when it has one', () => {
        const on = render(
            <IconButton icon="visible" title="Show" onClick={() => undefined} pressed />);
        const off = render(
            <IconButton icon="visible" title="Show" onClick={() => undefined} pressed={false} />);

        assert.match(on, /aria-pressed="true"/);
        assert.match(off, /aria-pressed="false"/);
    });

    it('says nothing about pressed state when it is a plain action', () => {
        const html = render(<IconButton icon="close" title="Close" onClick={() => undefined} />);

        assert.equal(/aria-pressed/.test(html), false);
    });

    /** A disclosure - the control that opens a flyout - reports whether the flyout is open. */
    it('reports whether the thing it opens is open', () => {
        const html = render(
            <IconButton icon="tune" title="Settings" onClick={() => undefined} expanded />);

        assert.match(html, /aria-expanded="true"/);
    });

    /**
     * The count belongs INSIDE the button, not beside it: pressing the number has to do what
     * pressing the glyph does, and a count that is a sibling is a hit area the reader can miss.
     */
    /**
     * A sentence is the right tooltip and the wrong accessible name: heard before every other
     * control on the row, "Choose which language columns to show" is what a screen-reader user
     * skips past. The two are allowed to differ, and default to the same string when they do not.
     */
    it('can be called something shorter than its tooltip', () => {
        const html = render(
            <IconButton
                icon="settings"
                title="Choose which language columns to show"
                label="Columns"
                onClick={() => undefined}
            />);

        assert.match(html, /title="Choose which language columns to show"/);
        assert.match(html, /aria-label="Columns"/);
    });

    it('carries a count beside its glyph, inside the same hit area', () => {
        const html = render(
            <IconButton icon="save" title="Save" onClick={() => undefined} badge=" 3" />);

        assert.match(html, /<button[^>]*>.*<\/svg>\s*3\s*<\/button>|3<\/button>/s);
    });

    it('takes a placement variant for the header without losing the base class', () => {
        const html = render(
            <IconButton icon="close" title="Close" onClick={() => undefined} className="header-right" />);

        assert.match(html, /class="icon-btn header-right"/);
    });
});
