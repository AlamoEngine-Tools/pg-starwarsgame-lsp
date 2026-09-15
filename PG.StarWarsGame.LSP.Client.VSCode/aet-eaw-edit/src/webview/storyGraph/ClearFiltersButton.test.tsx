// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { renderToStaticMarkup } from 'react-dom/server';

import { ClearFiltersButton } from './ClearFiltersButton';

const render = (element: React.JSX.Element): string => renderToStaticMarkup(element);

const none = { nameFilter: '', branch: '', lifecycle: '', reachableFrom: '', plotState: '' };

describe('ClearFiltersButton', () => {
    /**
     * The reported defect. It was rendered only while a filter was set, and it is the first child
     * of `.overview-tools` - a vertically CENTRED column beside the minimap. So typing in the
     * filter box grew the column by one button, the centring moved every tool up by half of one,
     * and the button itself appeared out of nowhere at the top left of the minimap.
     *
     * The rule the rest of this UI follows: a control with nothing to act on is disabled and says
     * why. It is never removed.
     */
    it('is drawn whether or not anything is filtered, so nothing moves when a filter is set', () => {
        const idle = render(<ClearFiltersButton filters={none} onClear={() => undefined} />);
        const busy = render(
            <ClearFiltersButton filters={{ ...none, branch: 'Empire' }} onClear={() => undefined} />);

        assert.match(idle, /<button/);
        assert.match(busy, /<button/);
    });

    it('is disabled with nothing to clear, and says so instead of naming the action', () => {
        const html = render(<ClearFiltersButton filters={none} onClear={() => undefined} />);

        assert.match(html, /disabled/);
        assert.match(html, /title="[^"]*no filters are set"/i);
    });

    it('is live as soon as any one of them is set', () => {
        for (const filters of [
            { ...none, nameFilter: 'destroy' },
            { ...none, branch: 'Empire' },
            { ...none, lifecycle: 'Armed' },
            { ...none, reachableFrom: 'Event_01' },
            // A plot's registration state - Active_Plot or Suspended_Plot in the faction's
            // manifest - which is a different question from an event's lifecycle.
            { ...none, plotState: 'Suspended' },
        ]) {
            const html = render(<ClearFiltersButton filters={filters} onClear={() => undefined} />);

            assert.equal(/disabled/.test(html), false, JSON.stringify(filters));
            assert.match(html, /title="Clear all filters"/);
        }
    });

    it('treats an absent field as unset, since the protocol makes every one optional', () => {
        assert.match(render(<ClearFiltersButton filters={{}} onClear={() => undefined} />), /disabled/);
    });
});
