// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { CAPTURE_SIZES, captureFileName, captureSize } from './capture';

describe('captureSize', () => {
    it('takes a size it is given', () => {
        assert.deepEqual(captureSize(256, 256), { width: 256, height: 256 });
    });

    it('refuses a size no GPU will allocate', () => {
        // A texture bigger than the context allows fails the render silently and hands back a blank
        // image, which looks like the capture worked.
        const size = captureSize(100000, 100000);

        assert.ok(size.width <= 4096 && size.height <= 4096, `${size.width}x${size.height}`);
    });

    it('refuses a size with no pixels in it', () => {
        assert.deepEqual(captureSize(0, -10), { width: 1, height: 1 });
    });

    it('rounds to whole pixels', () => {
        assert.deepEqual(captureSize(100.6, 50.2), { width: 101, height: 50 });
    });

    it('offers the icon sizes the game actually uses', () => {
        // MTD icon pages are powers of two, and a roster shot wants to match what it is going into.
        assert.ok(CAPTURE_SIZES.includes(64));
        assert.ok(CAPTURE_SIZES.includes(128));
    });
});

describe('captureFileName', () => {
    it('names the file after the subject', () => {
        assert.match(captureFileName('Rebel_Xwing', 128), /^Rebel_Xwing/);
    });

    it('says how big it is, so a folder of them is sortable by eye', () => {
        assert.match(captureFileName('Rebel_Xwing', 128), /128/);
    });

    it('ends in png, because that is what is written', () => {
        assert.match(captureFileName('x', 64), /\.png$/);
    });

    it('drops what a filesystem will not take', () => {
        // A model reference can arrive as `EV_StarDestroyer.ALO`, and a subject can be a path.
        const name = captureFileName('Data/Art/EV_Star Destroyer.ALO', 64);

        assert.doesNotMatch(name.slice(0, -4), /[\/:*?"<>|]/);
    });

    it('still produces a name for a subject with nothing usable in it', () => {
        assert.match(captureFileName('///', 64), /^capture/);
    });
});

