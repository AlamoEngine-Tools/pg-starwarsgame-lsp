// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {directoriesNeedingOwnWatcher} from './dependencyWatchers';

/**
 * Which reported directories need a watcher of their own.
 *
 * A bare glob handed to createFileSystemWatcher is resolved against the open workspace folders, so
 * everything inside one is already covered. A referenced project normally is not, and that is the
 * whole reason this exists: with the window on a leaf mod, editing its dependency reached nobody.
 */
describe('directoriesNeedingOwnWatcher', () => {
    it('keeps a directory outside every workspace folder', () => {
        const need = directoriesNeedingOwnWatcher(
            ['c:/dev/EaWX/Data', 'c:/dev/EaWX/Rev'],
            ['c:/dev/EaWX/Rev']);

        assert.deepEqual(need, ['c:/dev/EaWX/Data']);
    });

    it('drops a directory the workspace folder already contains', () => {
        const need = directoriesNeedingOwnWatcher(
            ['c:/dev/EaWX/Rev/Data/XML'],
            ['c:/dev/EaWX/Rev']);

        assert.deepEqual(need, []);
    });

    it('drops the workspace folder itself', () => {
        assert.deepEqual(
            directoriesNeedingOwnWatcher(['c:/dev/EaWX/Rev'], ['c:/dev/EaWX/Rev']),
            []);
    });

    /**
     * Windows paths arrive from the server forward-slashed and from VS Code with either separator
     * and either drive-letter case, so neither side can be compared raw.
     */
    it('compares paths regardless of separator or case', () => {
        assert.deepEqual(
            directoriesNeedingOwnWatcher(['C:/dev/EaWX/Rev/Data'], ['c:\\dev\\eawx\\rev']),
            []);
    });

    /**
     * A sibling whose name merely starts the same is NOT contained - the boundary has to be a
     * separator, or `Rev2` would be treated as covered by a folder open on `Rev`.
     */
    it('does not treat a same-prefix sibling as contained', () => {
        const need = directoriesNeedingOwnWatcher(
            ['c:/dev/EaWX/Rev2'],
            ['c:/dev/EaWX/Rev']);

        assert.deepEqual(need, ['c:/dev/EaWX/Rev2']);
    });

    it('reports each directory once', () => {
        const need = directoriesNeedingOwnWatcher(
            ['c:/dev/EaWX/Data', 'C:\\dev\\EaWX\\Data'],
            []);

        assert.equal(need.length, 1);
    });

    it('with no workspace folders, keeps everything', () => {
        const need = directoriesNeedingOwnWatcher(['c:/dev/EaWX/Data'], []);

        assert.deepEqual(need, ['c:/dev/EaWX/Data']);
    });
});
