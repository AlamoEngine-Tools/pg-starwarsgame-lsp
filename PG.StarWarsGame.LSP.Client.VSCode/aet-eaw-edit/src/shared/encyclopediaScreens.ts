// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * The screens the encyclopedia card can be drawn for. The game sizes the popup's glyphs from the
 * display it runs on, and its options menu offers whatever modes the graphics card reports, so
 * there is no list of the game's own to copy: these are the detail presets' starting resolutions
 * (800x600, 1024x768, 1280x1024) and the common displays. Shared by the extension host, which
 * stores the choice, and the preview, which offers it.
 */
export interface EncyclopediaScreen {
    width: number;
    height: number;
}

/** By height, then width: the height is what sets the glyph size. */
export const ENCYCLOPEDIA_SCREENS: readonly EncyclopediaScreen[] = [
    { width: 800, height: 600 },
    { width: 1280, height: 720 },
    { width: 1024, height: 768 },
    { width: 1366, height: 768 },
    { width: 1600, height: 900 },
    { width: 1280, height: 1024 },
    { width: 1680, height: 1050 },
    { width: 1920, height: 1080 },
    { width: 1920, height: 1200 },
    { width: 2560, height: 1440 },
    { width: 3840, height: 2160 },
];

export const DEFAULT_ENCYCLOPEDIA_SCREEN: EncyclopediaScreen = {
    width: 1920,
    height: 1080,
};

/** `1920x1080`: the option's value and its label. */
export function encyclopediaScreenId(screen: EncyclopediaScreen): string {
    return `${screen.width}x${screen.height}`;
}

/**
 * A listed screen from a stored value or an option id, else the default. The read never trusts
 * what it finds: a stored value outlives the build that wrote it.
 */
export function encyclopediaScreenFrom(value: unknown): EncyclopediaScreen {
    const id =
        typeof value === "string"
            ? value
            : typeof value === "object" &&
                value !== null &&
                typeof (value as EncyclopediaScreen).width === "number" &&
                typeof (value as EncyclopediaScreen).height === "number"
              ? encyclopediaScreenId(value as EncyclopediaScreen)
              : null;
    const listed = ENCYCLOPEDIA_SCREENS.find(
        (s) => encyclopediaScreenId(s) === id,
    );
    return listed ? { ...listed } : { ...DEFAULT_ENCYCLOPEDIA_SCREEN };
}
