// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { renderToStaticMarkup } from 'react-dom/server';

import { ModeSelector } from './ModeSelector';

const render = (element: React.JSX.Element): string => renderToStaticMarkup(element);

const VIEWS = [
    { id: 'front', label: 'Front' },
    { id: 'side', label: 'Side' },
] as const;

describe('ModeSelector', () => {
    /**
     * A radiogroup, not a row of toggles. One of these can hold at a time, and that is the whole
     * difference: three independent buttons each reporting their own pressed state say nothing
     * about being alternatives to one another.
     */
    it('is a radio group whose options are radios', () => {
        const html = render(
            <ModeSelector label="View" value="front" options={VIEWS} onSelect={() => undefined} />);

        assert.match(html, /role="radiogroup"[^>]*aria-label="View"|aria-label="View"[^>]*role="radiogroup"/);
        assert.match(html, /role="radio"[^>]*aria-checked="true"/);
        assert.match(html, /role="radio"[^>]*aria-checked="false"/);
    });

    /**
     * A preset can be DEPARTED from - dragging the camera off a saved view leaves it on no preset
     * at all - and the honest readout for that is an empty group, not a highlight on a view you are
     * no longer looking from.
     */
    it('marks nothing chosen when the value is null', () => {
        const html = render(
            <ModeSelector label="View" value={null} options={VIEWS} onSelect={() => undefined} />);

        assert.equal(/aria-checked="true"/.test(html), false, html);
    });

    it('shows the label when an option carries no glyph', () => {
        const html = render(
            <ModeSelector label="View" value="front" options={VIEWS} onSelect={() => undefined} />);

        assert.match(html, />Front</);
    });

    /**
     * Through Icon, so an option names a MEANING rather than a library's glyph. The field used to
     * be "a codicon name without the prefix", which no call site ever passed - the one escape hatch
     * from the icon seam, and it was dead.
     */
    it('draws a glyph through the icon seam, not a codicon class', () => {
        const html = render(
            <ModeSelector
                label="Search in"
                value="text"
                options={[{ id: 'text', label: 'Plain text', icon: 'searchLiteral' }]}
                onSelect={() => undefined}
            />);

        assert.match(html, /codicon-case-sensitive/);
        assert.equal(/>Plain text</.test(html), false, html);
    });

    /**
     * Disable, don't hide - the same rule the buttons carry, and for the same reason. An option
     * that vanishes when unavailable takes its own explanation with it, and every option beside it
     * shifts under the reader's eye.
     */
    it('keeps an unavailable option on screen, saying why', () => {
        const html = render(
            <ModeSelector
                label="Mode"
                value="view"
                options={[
                    { id: 'view', label: 'View' },
                    {
                        id: 'simulate', label: 'Simulate',
                        disabled: true, disabledReason: 'This story has no simulation',
                    },
                ]}
                onSelect={() => undefined}
            />);

        assert.match(html, /Simulate/);
        assert.match(html, /disabled/);
        assert.match(html, /title="This story has no simulation"/);
    });

    it('titles an available option with what it is, or the longer form when given', () => {
        const html = render(
            <ModeSelector
                label="View"
                value="front"
                options={[{ id: 'front', label: 'Front', title: 'Look from the front' }]}
                onSelect={() => undefined}
            />);

        assert.match(html, /title="Look from the front"/);
    });
});
