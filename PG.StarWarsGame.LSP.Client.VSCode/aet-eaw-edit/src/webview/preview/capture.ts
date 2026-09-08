// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Rendering the subject to a file.
//
// This is what the camera work is FOR: a correctly kitted-out unit, framed identically across a
// whole roster, written out at the size the icon pipeline wants. The webview cannot save a file
// itself, so it hands the bytes to the panel host - see the `capture` message.

/** The sizes offered, smallest first. Powers of two, because an MTD icon page is one. */
export const CAPTURE_SIZES: readonly number[] = [64, 128, 256, 512, 1024];

/**
 * The largest side this will render.
 *
 * A render target bigger than the context allows does not throw - it fails and hands back a blank
 * image, which looks exactly like a capture that worked. 4096 is the floor of what WebGL2
 * implementations guarantee, so it is the largest size that cannot fail this way.
 */
const MAX_SIDE = 4096;

/** What the capture will actually render, whatever it was asked for. */
export function captureSize(width: number, height: number): { width: number; height: number } {
    return { width: side(width), height: side(height) };
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
 */
export function captureFileName(subject: string, size: number): string {
    // Everything a filesystem refuses, plus the extension the subject may already carry: an
    // `EV_StarDestroyer.ALO` capture should not be called `EV_StarDestroyer.ALO_128.png`.
    const cleaned = subject
        .replace(/\.(alo|ala)$/i, '')
        .replace(/[\/:*?"<>|]+/g, ' ')
        .trim()
        .replace(/\s+/g, '_');

    return `${cleaned === '' ? 'capture' : cleaned}_${side(size)}.png`;
}

