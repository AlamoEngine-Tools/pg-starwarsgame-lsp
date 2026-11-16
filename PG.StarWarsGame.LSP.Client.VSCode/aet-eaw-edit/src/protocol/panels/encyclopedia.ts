// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The encyclopedia preview's messages (#208): EncyclopediaPanel and encyclopediaPreview.tsx.

import type { EncyclopediaScreen } from "../../shared/encyclopediaScreens";
import type { GetEncyclopediaEntryResult } from "../encyclopedia";

/** The bundle is listening; anything held back can go now. */
export interface EncyclopediaReady {
    type: "ready";
}

/** The body text the reader picked: the singleplayer or the multiplayer one. */
export interface EncyclopediaSetMultiplayer {
    type: "setMultiplayer";
    multiplayer: boolean;
}

/** The screen the card is drawn for; the reader's choice, kept for every card. */
export interface EncyclopediaSetScreen {
    type: "setScreen";
    screen: EncyclopediaScreen;
}

export type EncyclopediaToHost =
    EncyclopediaReady | EncyclopediaSetMultiplayer | EncyclopediaSetScreen;

/** An entry, with the mode and the screen it was requested in, so the controls match what is drawn. */
export interface EncyclopediaEntry {
    type: "entry";
    entry: GetEncyclopediaEntryResult;
    multiplayer: boolean;
    screen: EncyclopediaScreen;
    /** The ship name drawn for this object, or null when it has no pool. */
    shipName: string | null;
}

export type EncyclopediaToWebview = EncyclopediaEntry;
