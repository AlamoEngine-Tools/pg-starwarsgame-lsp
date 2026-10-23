// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from "vscode";

import {
    type EncyclopediaScreen,
    encyclopediaScreenFrom,
} from "./webview/encyclopediaScreens";

/**
 * The screen the encyclopedia card is drawn for.
 *
 * Backed by `globalState`, like the preview's room settings: it is the display the modder targets,
 * which follows the person rather than the mod or the card. Held in a module, as
 * `viewerSettingsStorage` is, because the card is requested from the command and from the Mod
 * Browser alike.
 */
const KEY = "aet-eaw-edit.encyclopediaScreen";

let store: vscode.Memento | undefined;

/** Called once from `activate`, with `context.globalState`. */
export function initEncyclopediaScreenStorage(memento: vscode.Memento): void {
    store = memento;
}

/** What was stored, or 1920x1080. */
export function readEncyclopediaScreen(): EncyclopediaScreen {
    return encyclopediaScreenFrom(store?.get<unknown>(KEY));
}

/** Records the screen, normalised to a listed one; a failed write is not worth reporting. */
export function saveEncyclopediaScreen(screen: unknown): void {
    if (store === undefined) {
        return;
    }

    void Promise.resolve(
        store.update(KEY, encyclopediaScreenFrom(screen)),
    ).then(undefined, () => {
        /* a preference is not worth reporting a failure over */
    });
}

/** The screen as `aet/getEncyclopediaEntry` takes it. */
export function encyclopediaScreenParams(): {
    screenWidth: number;
    screenHeight: number;
} {
    const screen = readEncyclopediaScreen();
    return { screenWidth: screen.width, screenHeight: screen.height };
}
