// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { beforeEach, describe, it } from 'node:test';

import {
    applyStoredLayout, panelLayoutFrom, readPanelSize, resetPanelLayoutForTests, writePanelSize,
} from './panelLayout';

describe('panelLayoutFrom', () => {
    it('keeps finite positive numbers', () => {
        assert.deepEqual(panelLayoutFrom({ 'storyGraph.dock': 420 }), { 'storyGraph.dock': 420 });
    });

    // The blob outlives the build that wrote it, and a dock that opens at NaN pixels wide gives the
    // reader no way back - so every entry is validated on its own and a bad one is simply dropped.
    it('drops entries that are not usable sizes', () => {
        const layout = panelLayoutFrom({
            good: 300,
            zero: 0,
            negative: -40,
            fractional: 12.5,
            huge: Number.MAX_SAFE_INTEGER,
            nan: Number.NaN,
            infinite: Number.POSITIVE_INFINITY,
            text: '300',
            nested: { width: 300 },
            nothing: null,
        });

        assert.deepEqual(layout, { good: 300, fractional: 12.5 });
    });

    it('answers with an empty layout for anything that is not an object', () => {
        for (const value of [null, undefined, 42, 'x', [], true]) {
            assert.deepEqual(panelLayoutFrom(value), {});
        }
    });
});

describe('the panel size store', () => {
    beforeEach(() => { resetPanelLayoutForTests(); });

    it('falls back to the caller default when nothing is stored', () => {
        assert.equal(readPanelSize('modelPreview.dock', 260), 260);
    });

    it('reads back what was written', () => {
        writePanelSize('modelPreview.dock', 310);

        assert.equal(readPanelSize('modelPreview.dock', 260), 310);
    });

    // The whole point of the key: each editor keeps its own value, and the drawer keeps a value
    // separate from the dock beside it.
    it('keeps every surface and control apart', () => {
        writePanelSize('modelPreview.dock', 310);
        writePanelSize('storyGraph.dock', 480);
        writePanelSize('storyGraph.problems', 200);

        assert.equal(readPanelSize('modelPreview.dock', 260), 310);
        assert.equal(readPanelSize('storyGraph.dock', 300), 480);
        assert.equal(readPanelSize('storyGraph.problems', 140), 200);
        assert.equal(readPanelSize('localisation.dock', 300), 300);
    });

    it('reports each written size to the host exactly once', () => {
        const sent: [string, number][] = [];
        resetPanelLayoutForTests(([key, value]) => sent.push([key, value]));

        writePanelSize('modelPreview.dock', 310);
        writePanelSize('modelPreview.dock', 320);

        assert.deepEqual(sent, [['modelPreview.dock', 310], ['modelPreview.dock', 320]]);
    });

    it('takes the host layout on arrival', () => {
        applyStoredLayout({ 'storyGraph.dock': 480, bad: Number.NaN });

        assert.equal(readPanelSize('storyGraph.dock', 300), 480);
        assert.equal(readPanelSize('bad', 99), 99);
    });

    // The stored layout arrives asynchronously, after the dock has already mounted at its default.
    // A user who drags before it lands must not have their drag overwritten by the late arrival.
    it('does not overwrite a size the reader has already set', () => {
        writePanelSize('storyGraph.dock', 500);
        applyStoredLayout({ 'storyGraph.dock': 480, 'storyGraph.problems': 200 });

        assert.equal(readPanelSize('storyGraph.dock', 300), 500);
        assert.equal(readPanelSize('storyGraph.problems', 140), 200);
    });
});
