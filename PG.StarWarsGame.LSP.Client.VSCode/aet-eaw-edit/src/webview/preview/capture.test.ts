// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import {describe, it} from 'node:test';

import {
    CAPTURE_SIZES, captureFileName, captureFrame, captureSize, DEFAULT_CAPTURE_SIZE, iconFileName,
} from './capture';

describe('captureSize', () => {
    it('takes a size it is given', () => {
        assert.deepEqual(captureSize(256, 256), {width: 256, height: 256});
    });

    it('refuses a size no GPU will allocate', () => {
        // A texture bigger than the context allows fails the render silently and hands back a blank
        // image, which looks like the capture worked.
        const size = captureSize(100000, 100000);

        assert.ok(size.width <= 4096 && size.height <= 4096, `${size.width}x${size.height}`);
    });

    it('refuses a size with no pixels in it', () => {
        assert.deepEqual(captureSize(0, -10), {width: 1, height: 1});
    });

    it('rounds to whole pixels', () => {
        assert.deepEqual(captureSize(100.6, 50.2), {width: 101, height: 50});
    });

    /**
     * The sizes the game's icons actually are, measured from Mt_commandbar.mtd - never the atlas
     * page's powers of two - plus the two large-icon shapes (big_ + Icon_Name, a side of 52 or more
     * doubling the slot that way).
     */
    it('offers the icon sizes the game uses and nothing else', () => {
        assert.deepEqual(CAPTURE_SIZES.map(s => [s.width, s.height]),
            [[24, 24], [26, 26], [40, 40], [50, 50], [100, 50], [100, 100]]);
        assert.deepEqual(CAPTURE_SIZES.map(s => s.title), [
            '24 - passive ability icon', '26 - special ability icon', '40 - upgrade icon',
            '50 - build icon', '100x50 - large icon, double width', '100 - large icon, double both ways',
        ]);
    });

    it('starts at the build icon', () => {
        assert.deepEqual([DEFAULT_CAPTURE_SIZE.width, DEFAULT_CAPTURE_SIZE.height], [50, 50]);
    });
});

describe('captureFileName', () => {
    it('names the file after the subject', () => {
        assert.match(captureFileName('Rebel_Xwing', 128, 128), /^Rebel_Xwing/);
    });

    it('says how big it is, so a folder of them is sortable by eye', () => {
        assert.equal(captureFileName('Rebel_Xwing', 50, 50), 'Rebel_Xwing_50.png');
        assert.equal(captureFileName('Rebel_Xwing', 100, 50), 'Rebel_Xwing_100x50.png');
    });

    it('ends in png, because that is what is written', () => {
        assert.match(captureFileName('x', 64, 64), /\.png$/);
    });

    it('drops what a filesystem will not take', () => {
        // A model reference can arrive as `EV_StarDestroyer.ALO`, and a subject can be a path.
        const name = captureFileName('Data/Art/EV_Star Destroyer.ALO', 64, 64);

        assert.doesNotMatch(name.slice(0, -4), /[\/:*?"<>|]/);
    });

    it('still produces a name for a subject with nothing usable in it', () => {
        assert.match(captureFileName('///', 64, 64), /^capture/);
    });
});

describe('iconFileName', () => {
    const size = (id: string) => CAPTURE_SIZES.find(s => s.id === id)!;

    it('names a large icon the way the engine looks it up: big_ + Icon_Name', () => {
        assert.equal(iconFileName(size('100x50'), 'I_BUTTON_LUKE.TGA'), 'big_I_BUTTON_LUKE.png');
        assert.equal(iconFileName(size('100'), 'i_button_executor.tga'), 'big_i_button_executor.png');
    });

    it('keeps an icon name that carries no extension whole', () => {
        assert.equal(iconFileName(size('100x50'), 'I_BUTTON_LUKE'), 'big_I_BUTTON_LUKE.png');
    });

    it('names the build icon Icon_Name itself', () => {
        assert.equal(iconFileName(size('50'), 'I_BUTTON_LUKE.TGA'), 'I_BUTTON_LUKE.png');
    });

    it('is only for the sizes that ARE the unit\'s icon', () => {
        // Upgrades and abilities have icons of their own; Icon_Name is not theirs to borrow.
        assert.deepEqual(
            CAPTURE_SIZES.filter(s => s.iconPrefix !== undefined).map(s => s.id), ['50', '100x50', '100']);
        assert.equal(iconFileName(size('40'), 'I_BUTTON_LUKE.TGA'), null);
        assert.equal(iconFileName(null, 'I_BUTTON_LUKE.TGA'), null);
    });

    it('has nothing to say without an icon name', () => {
        assert.equal(iconFileName(size('100x50'), null), null);
        assert.equal(iconFileName(size('100x50'), '   '), null);
    });
});

describe('captureFrame', () => {
    /**
     * The area the capture writes, drawn over the viewport: the largest rectangle of the capture's
     * shape that fits, centred. The capture renders exactly this region, so the frame never lies.
     */
    it('is the full height, centred, for a shape narrower than the viewport', () => {
        assert.deepEqual(captureFrame(800, 400, 50, 50), {x: 200, y: 0, width: 400, height: 400});
    });

    it('is the full width, centred, for a shape wider than the viewport', () => {
        assert.deepEqual(captureFrame(400, 800, 100, 50), {x: 0, y: 300, width: 400, height: 200});
    });

    it('is the whole viewport when the shapes match', () => {
        assert.deepEqual(captureFrame(640, 320, 100, 50), {x: 0, y: 0, width: 640, height: 320});
    });
});

