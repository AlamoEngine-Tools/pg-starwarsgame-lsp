// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/** The subdirectories or the files of a directory, or undefined when it cannot be read. */
type Listing = (directory: string) => readonly string[] | undefined;

/** The folders under `Data` the game reads; a mod holds at least one of them. */
const MOD_DATA_FOLDERS = ["xml", "scripts", "art", "audio", "text"];

/** The executables the game runs from; the folder holding one is the game, not a mod. */
const GAME_EXECUTABLES = [
    "sweaw.exe",
    "swfoc.exe",
    "starwarsg.exe",
    "starwarsi.exe",
];

/** The Steam Workshop description a mod's folder holds. */
const MOD_INFO = "modinfo.json";

/**
 * Whether a folder is laid out like a mod: it is offered a project file (see
 * {@link offersProject}), or it is a `Data` folder holding one of the game's folders or a MEG
 * archive, outside the game's own folder. Names match in any case, as the game reads them.
 *
 * @param list The subdirectories of a directory, or undefined when it cannot be read.
 * @param files The files of a directory, or undefined when it cannot be read.
 */
export function isModFolder(
    directory: string,
    list: Listing,
    files: Listing,
): boolean {
    const segments = directory.split(/[\\/]/).filter((s) => s !== "");
    if (segments.at(-1)?.toLowerCase() === "data") {
        const parent = directory.slice(
            0,
            directory.length - segments.at(-1)!.length - 1,
        );
        return holdsModData(directory, list, files) && !isGame(parent, files);
    }
    return offersProject(directory, list, files);
}

/**
 * Whether a folder without a project file is offered one: it holds `modinfo.json`, or a `Data`
 * folder holding one of the game's folders or a MEG archive, so the project file can sit beside
 * `Data`; and it is not the game's own folder.
 *
 * @param list The subdirectories of a directory, or undefined when it cannot be read.
 * @param files The files of a directory, or undefined when it cannot be read.
 */
export function offersProject(
    directory: string,
    list: Listing,
    files: Listing,
): boolean {
    if (isGame(directory, files)) {
        return false;
    }
    if ((files(directory) ?? []).some((f) => f.toLowerCase() === MOD_INFO)) {
        return true;
    }
    const data = list(directory)?.find((e) => e.toLowerCase() === "data");
    return (
        data !== undefined && holdsModData(`${directory}/${data}`, list, files)
    );
}

/** Whether a `Data` folder holds one of the game's folders or a MEG archive. */
function holdsModData(data: string, list: Listing, files: Listing): boolean {
    return (
        (list(data) ?? []).some((e) =>
            MOD_DATA_FOLDERS.includes(e.toLowerCase()),
        ) || (files(data) ?? []).some((f) => f.toLowerCase().endsWith(".meg"))
    );
}

/** Whether a folder holds one of the game's executables. */
function isGame(directory: string, files: Listing): boolean {
    return (files(directory) ?? []).some((f) =>
        GAME_EXECUTABLES.includes(f.toLowerCase()),
    );
}

/**
 * Whether a folder without a project file is still to be offered one: it is offered one (see
 * {@link offersProject}) and has not been asked about yet - not declined for good, not answered
 * earlier this session. The server is told, so it does not raise its own error over the offer.
 *
 * @param asked The folders already asked about, lower-cased.
 */
export function projectOfferDue(
    directory: string,
    list: Listing,
    files: Listing,
    asked: readonly string[],
): boolean {
    return (
        !asked.includes(directory.toLowerCase()) &&
        offersProject(directory, list, files)
    );
}
