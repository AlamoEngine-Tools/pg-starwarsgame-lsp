// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a search plate's field goes when it opens BESIDE the plate rather than inside it.
//
// A plate in a dock stands in a column a few hundred pixels wide, and a field sharing that row
// with the icons measured too narrow to read a name in. So the field of a dock's plate opens to
// the left of the plate, over the stage, where there is room - placed against the window, for the
// reason the details flyout is: the plate lives inside a panel that scrolls, and a child of one is
// clipped by it.

/** How wide the field is where the window leaves it the room. */
export const PLATE_FIELD_WIDTH = 280;

/** How close to the window's left edge the field may come. */
export const PLATE_FIELD_MARGIN = 8;

/** The plate's own box, in window coordinates. */
export interface PlateBox {
    left: number;
    top: number;
    height: number;
}

export interface PlateFieldPlacement {
    left: number;
    top: number;
    width: number;
    height: number;
}

/**
 * The field's box: as tall as the plate, its right edge one pixel under the plate's left edge so
 * the two borders draw as one line and the pair reads as a single plate that grew leftwards.
 */
export function placePlateField(plate: PlateBox): PlateFieldPlacement {
    const right = plate.left + 1;
    const width = Math.max(
        0,
        Math.min(PLATE_FIELD_WIDTH, right - PLATE_FIELD_MARGIN),
    );

    return { left: right - width, top: plate.top, width, height: plate.height };
}
