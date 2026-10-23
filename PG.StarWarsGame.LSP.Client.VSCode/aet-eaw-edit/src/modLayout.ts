// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * Whether a folder is laid out like a mod: it holds `Data/XML`, or it is the `Data` folder itself
 * and holds `XML`. Names match in any case, as the game reads them.
 *
 * @param list The subdirectories of a directory, or undefined when it cannot be read.
 */
export function isModFolder(
    directory: string,
    list: (directory: string) => readonly string[] | undefined,
): boolean {
    const child = (parent: string, name: string): string | undefined => {
        const entry = list(parent)?.find(
            (e) => e.toLowerCase() === name.toLowerCase(),
        );
        return entry === undefined ? undefined : `${parent}/${entry}`;
    };
    if (child(directory, "XML") !== undefined) {
        return true;
    }
    const data = child(directory, "Data");
    return data !== undefined && child(data, "XML") !== undefined;
}

/** The executables the game runs from; the folder holding one is the game, not a mod. */
const GAME_EXECUTABLES = [
    "sweaw.exe",
    "swfoc.exe",
    "starwarsg.exe",
    "starwarsi.exe",
];

/**
 * Whether a folder without a project file is offered one: it holds `Data/XML`, where the project
 * file can sit beside `Data`, and it is not the game's own folder.
 *
 * @param list The subdirectories of a directory, or undefined when it cannot be read.
 * @param files The files of a directory, or undefined when it cannot be read.
 */
export function offersProject(
    directory: string,
    list: (directory: string) => readonly string[] | undefined,
    files: (directory: string) => readonly string[] | undefined,
): boolean {
    const data = list(directory)?.find((e) => e.toLowerCase() === "data");
    const holdsXml =
        data !== undefined &&
        (list(`${directory}/${data}`) ?? []).some(
            (e) => e.toLowerCase() === "xml",
        );
    const isGame = (files(directory) ?? []).some((f) =>
        GAME_EXECUTABLES.includes(f.toLowerCase()),
    );
    return holdsXml && !isGame;
}
