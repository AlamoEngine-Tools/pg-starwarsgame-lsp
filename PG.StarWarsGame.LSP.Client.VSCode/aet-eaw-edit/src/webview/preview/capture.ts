// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Rendering the subject to a file.
//
// This is what the camera work is FOR: a correctly kitted-out unit, framed identically across a
// whole roster, written out at the size the icon pipeline wants. The webview cannot save a file
// itself, so it hands the bytes to the panel host - see the `capture` message.

/** One size the capture offers, with what the game uses it for. */
export interface CaptureSize {
    id: string;
    width: number;
    height: number;
    /** The selector's label: the side, or both sides when they differ. */
    label: string;
    /** What the game uses the size for, e.g. `50 - build icon`. */
    title: string;
    /**
     * Set on the sizes that ARE the unit's icon, and prefixed to Icon_Name to name the capture: empty
     * for the build icon, `big_` for the large one the engine finds by that name.
     */
    iconPrefix?: string;
}

function square(side: number, what: string): CaptureSize {
    return {id: String(side), width: side, height: side, label: String(side), title: `${side} - ${what}`};
}

/**
 * The sizes the game's icons are, measured from Mt_commandbar.mtd (eaw and foc) - the individual
 * icons, not the atlas page, which is the only power of two involved. The large icons are the
 * command bar's `big_` + Icon_Name lookup (measured, Setup_List_Button): a side
 * of 52 or more doubles the slot that way, so a large icon is 100x50 or 100x100.
 */
export const CAPTURE_SIZES: readonly CaptureSize[] = [
    square(24, 'passive ability icon'),
    square(26, 'special ability icon'),
    square(40, 'upgrade icon'),
    {...square(50, 'build icon'), iconPrefix: ''},
    {
        id: '100x50',
        width: 100,
        height: 50,
        label: '100x50',
        title: '100x50 - large icon, double width',
        iconPrefix: 'big_'
    },
    {...square(100, 'large icon, double both ways'), iconPrefix: 'big_'},
];

/** The build icon: the size most captures are for. */
export const DEFAULT_CAPTURE_SIZE: CaptureSize = CAPTURE_SIZES[3];

/**
 * The largest side this will render.
 *
 * A render target bigger than the context allows does not throw - it fails and hands back a blank
 * image, which looks exactly like a capture that worked. 4096 is the floor of what WebGL2
 * implementations guarantee, so it is the largest size that cannot fail this way.
 */
export const MAX_SIDE = 4096;

/** What the capture will actually render, whatever it was asked for. */
export function captureSize(width: number, height: number): { width: number; height: number } {
    return {width: side(width), height: side(height)};
}

function side(value: number): number {
    if (!Number.isFinite(value)) {
        return 1;
    }

    return Math.min(Math.max(Math.round(value), 1), MAX_SIDE);
}

/**
 * A default filename for one capture.
 *
 * Carries the subject and the size, because the thing this feature produces is a FOLDER of them -
 * one per unit in a roster - and they have to be tellable apart in a file list without opening any.
 * A square names its side once; any other shape names both.
 */
export function captureFileName(subject: string, width: number, height: number): string {
    // Everything a filesystem refuses, plus the extension the subject may already carry: an
    // `EV_StarDestroyer.ALO` capture should not be called `EV_StarDestroyer.ALO_128.png`.
    const cleaned = subject
        .replace(/\.(alo|ala)$/i, '')
        .replace(/[\/:*?"<>|]+/g, ' ')
        .trim()
        .replace(/\s+/g, '_');
    const w = side(width);
    const h = side(height);

    return `${cleaned === '' ? 'capture' : cleaned}_${w === h ? w : `${w}x${h}`}.png`;
}

/**
 * The file name for a capture that is the unit's icon: the size's prefix + Icon_Name, as a png, or
 * null when the size is not one of the unit's icons or the subject names no icon.
 *
 * The build icon is Icon_Name itself, and the engine finds the large one as `big_` + Icon_Name and
 * nothing else (Setup_List_Button), so a capture named any other way has to be
 * renamed before the game can use it. The icon's own extension goes: the file written is a png,
 * whatever the icon is packed as.
 */
export function iconFileName(size: CaptureSize | null, iconName: string | null | undefined): string | null {
    const stem = (iconName ?? '')
        .trim()
        .replace(/\.(tga|dds|png|bmp|jpe?g)$/i, '')
        .replace(/[\/\\:*?"<>|]+/g, '_');

    const prefix = size?.iconPrefix;
    return prefix !== undefined && stem !== '' ? `${prefix}${stem}.png` : null;
}

/**
 * The part of the viewport a capture writes: the largest rectangle of the capture's shape that fits,
 * centred. Drawn over the viewport while a size is chosen, and the capture renders exactly this
 * region (a view offset on the camera), so the frame and the file always agree.
 */
export function captureFrame(viewWidth: number, viewHeight: number, width: number, height: number):
    { x: number; y: number; width: number; height: number } {
    const aspect = width / Math.max(height, 1);
    const frameWidth = Math.min(viewWidth, viewHeight * aspect);
    const frameHeight = frameWidth / aspect;
    return {
        x: Math.round((viewWidth - frameWidth) / 2),
        y: Math.round((viewHeight - frameHeight) / 2),
        width: Math.round(frameWidth),
        height: Math.round(frameHeight),
    };
}
