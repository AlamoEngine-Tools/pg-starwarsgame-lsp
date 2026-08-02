// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { emptyNavigatorMessage } from './navigatorPlaceholder';

describe('emptyNavigatorMessage', () => {
    // The bug: the tree announced an empty workspace while the scan was still running, then
    // refreshed into the real file list.
    it('does not claim there are none before the workspace has been read', () => {
        assert.equal(emptyNavigatorMessage(false, 'localisation files'), 'Loading...');
    });

    it('states it plainly once the workspace has been read', () => {
        assert.equal(
            emptyNavigatorMessage(true, 'localisation files'),
            'No localisation files found in this workspace.');
    });

    it('phrases both navigators the same way', () => {
        assert.equal(
            emptyNavigatorMessage(true, 'story campaigns'),
            'No story campaigns found in this workspace.');
        assert.equal(emptyNavigatorMessage(false, 'story campaigns'), 'Loading...');
    });
});
