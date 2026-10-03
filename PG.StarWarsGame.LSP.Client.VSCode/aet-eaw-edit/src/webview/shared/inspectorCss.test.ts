// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {inspectorCss} from './dockChrome';

/** The body of the first rule whose selector is exactly `selector`. */
function rule(selector: string): string {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return inspectorCss.match(new RegExp(`(^|\\n)\\s*${escaped}\\s*\\{([^}]*)\\}`))?.[2] ?? '';
}

describe('inspectorCss geometry box', () => {
    /**
     * The box neither used the height it sat in nor the width: the inspector was a plain scrolling
     * block and the scroller was capped at 60vh. Every link from the box down to the scroller has to
     * take the space and be allowed to shrink, or the one that does not is where the fill stops.
     */
    it('fills the remaining height, link by link down to the scroller', () => {
        for (const selector of ['.inspect-geometry', '.geometry-panel']) {
            const body = rule(selector);
            assert.match(body, /display:\s*flex/, `${selector}: ${body}`);
            assert.match(body, /flex-direction:\s*column/, `${selector}: ${body}`);
            assert.match(body, /flex:\s*1/, `${selector}: ${body}`);
            assert.match(body, /min-height:/, `${selector}: ${body}`);
        }

        const scroll = rule('.geometry-scroll');
        assert.match(scroll, /flex:\s*1/, scroll);
        assert.match(scroll, /min-height:\s*0/, scroll);
        assert.equal(/max-height/.test(scroll), false, scroll);
    });

    it('spans the full width', () => {
        assert.match(rule('.geometry-table'), /width:\s*100%/);
    });

    /**
     * `-var(--x)` is not CSS: the browser drops the whole declaration, so the open tab never
     * overlapped the panel's top border the way its comment said it did.
     */
    it('writes the open tab`s negative margin as valid CSS', () => {
        const open = rule('.geometry-tab.open');

        assert.equal(/-var\(/.test(open), false, open);
        assert.match(open, /margin-bottom:\s*calc\(-1 \* var\(--space-1\)\)/);
    });
});
