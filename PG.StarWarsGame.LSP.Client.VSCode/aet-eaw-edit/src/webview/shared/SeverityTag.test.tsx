// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { renderToStaticMarkup } from 'react-dom/server';

import { SeverityTag } from './SeverityTag';

const render = (element: React.JSX.Element): string => renderToStaticMarkup(element);

describe('SeverityTag', () => {
    /**
     * Four panels wrote this class string out by hand, character for character. Sharing it is the
     * whole point, so it is asserted rather than left to a reader to compare.
     */
    it('carries the classes every panel was writing by hand', () => {
        const html = render(
            <SeverityTag severity="warning" count={2} title="Two problems" onClick={() => undefined} />);

        assert.match(html, /class="icon-btn validate-btn header-right sev-warning"/);
    });

    it('takes its glyph from the severity, as a codicon', () => {
        assert.match(
            render(<SeverityTag severity="error" count={1} title="One" onClick={() => undefined} />),
            /codicon codicon-error/);
        assert.match(
            render(<SeverityTag severity="ok" count={0} title="Fine" onClick={() => undefined} />),
            /codicon codicon-check/);
        assert.match(
            render(<SeverityTag severity="unvalidated" count={0} title="?" onClick={() => undefined} />),
            /codicon codicon-question/);
    });

    // A severity mark is the one thing this extension still draws with a codicon, so that it
    // matches the marks the rest of the editor shows. Not a Tabler glyph.
    it('does not draw a Tabler icon, because a severity mark is not one', () => {
        const html = render(
            <SeverityTag severity="error" count={1} title="One" onClick={() => undefined} />);

        assert.equal(/<svg/.test(html), false, html);
    });

    /**
     * The count is rendered only when there is one, with the leading space all four sites wrote.
     * Zero shows the glyph alone rather than a bare "0", which reads as a value rather than as the
     * absence of one.
     */
    it('shows the count beside the glyph, and nothing when there is none', () => {
        const some = render(
            <SeverityTag severity="error" count={3} title="Three" onClick={() => undefined} />);
        const none = render(
            <SeverityTag severity="ok" count={0} title="Fine" onClick={() => undefined} />);

        assert.match(some, / 3<\/button>/);
        assert.match(none, /<\/span><\/button>/);
        assert.equal(/>\s*0\s*</.test(none), false, none);
    });

    /**
     * What differs between the four sites stays a prop. Two of them disable the tag at zero and two
     * leave it live; neither is made to behave like the other by being given a component.
     */
    it('reports why it is dead where a panel chooses to disable it', () => {
        const html = render(
            <SeverityTag
                severity="ok"
                count={0}
                title="Read the problems"
                disabled
                disabledReason="Nothing to report about this card"
                onClick={() => undefined}
            />);

        assert.match(html, /disabled/);
        assert.match(html, /title="Nothing to report about this card"/);
    });

    it('reports whether the panel it opens is open, where a panel tracks that', () => {
        const html = render(
            <SeverityTag
                severity="warning" count={1} title="One" expanded onClick={() => undefined} />);

        assert.match(html, /aria-expanded="true"/);
    });

    it('says nothing about expansion where pressing it runs a check instead', () => {
        const html = render(
            <SeverityTag severity="warning" count={1} title="One" onClick={() => undefined} />);

        assert.equal(/aria-expanded/.test(html), false);
    });
});
