// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { renderToStaticMarkup } from 'react-dom/server';

import { ColourKeyFlyout } from './ColourKeyFlyout';
import { EDGE_KINDS, LIFECYCLE_TOKENS } from './palette';

const noop = (): void => undefined;

describe('ColourKeyFlyout', () => {
    it('is the shared stage flyout, opening upwards from the bottom-right corner', () => {
        const html = renderToStaticMarkup(<ColourKeyFlyout branches={[]} onClose={noop} />);

        assert.match(html, /class="stage-flyout from-bottom on-right colour-key"/);
        assert.match(html, /class="stage-flyout-head"/);
        assert.match(html, /aria-label="Close"/);
    });

    it('keys every lifecycle and every edge kind the graph draws', () => {
        const html = renderToStaticMarkup(<ColourKeyFlyout branches={[]} onClose={noop} />);

        for (const lifecycle of Object.keys(LIFECYCLE_TOKENS)) {
            assert.match(html, new RegExp(`>${lifecycle}<`), lifecycle);
        }
        for (const kind of EDGE_KINDS) {
            assert.match(html, new RegExp(`>${kind.label}<`), kind.label);
            assert.match(html, new RegExp(`stroke="var\\(${kind.token}\\)"`), kind.token);
        }
    });

    /** Issue #128: the colours the reporter could not read were branches, named nowhere. */
    it('keys each branch with the token the overview draws it in', () => {
        const html = renderToStaticMarkup(<ColourKeyFlyout
            branches={[
                { branch: 'Act1', token: '--colour-data-orange', sharedWith: [] },
                { branch: 'Act2', token: '--colour-data-yellow', sharedWith: [] },
            ]}
            onClose={noop}
        />);

        assert.match(html, /data-branch="Act1"[^>]*>.*?var\(--colour-data-orange\)/);
        assert.match(html, /data-branch="Act2"[^>]*>.*?var\(--colour-data-yellow\)/);
        assert.match(html, /section-count[^>]*>2</);
    });

    /**
     * The branch hues are the lifecycle hues. Zoomed out a green node can be a branch while the key
     * says green is Armed, so the key has to say which one wins.
     */
    it('says a branch colour replaces the lifecycle colour zoomed out', () => {
        const html = renderToStaticMarkup(<ColourKeyFlyout
            branches={[{ branch: 'Act1', token: '--colour-data-green', sharedWith: [] }]}
            onClose={noop}
        />);

        assert.match(html, /instead of its lifecycle/);
    });

    it('names the branches that share a colour', () => {
        const html = renderToStaticMarkup(<ColourKeyFlyout
            branches={[
                { branch: 'Act1', token: '--colour-data-red', sharedWith: ['Act3'] },
                { branch: 'Act3', token: '--colour-data-red', sharedWith: ['Act1'] },
            ]}
            onClose={noop}
        />);

        assert.match(html, /Same colour as Act3/);
        assert.match(html, /Same colour as Act1/);
    });

    /** Disable, don't hide: the section stays and says why it is empty. */
    it('keeps the branch section when nothing in view has a branch, and says so', () => {
        const html = renderToStaticMarkup(<ColourKeyFlyout branches={[]} onClose={noop} />);

        assert.match(html, />Branches</);
        assert.match(html, /No event in this view names a branch/);
    });
});
