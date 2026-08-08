// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { dockChromeCss } from './dockChrome';

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
});
