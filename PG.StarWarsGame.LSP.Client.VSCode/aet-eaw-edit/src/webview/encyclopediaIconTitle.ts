// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as l10n from "@vscode/l10n";

import { EncyclopediaIcon } from "../protocol/encyclopedia";

/**
 * Hover text for the card's portrait, naming where its pixels came from.
 *
 * An icon can now resolve from three different places, so an author looking at unfamiliar artwork
 * needs to know which: their own packed art, their own art that has not been repacked yet, or the
 * base game's. The stale case is called out first and in the game's own terms - the point is not
 * that the file is missing but that the game will not show it until the mega texture is rebuilt.
 *
 * Split out of the card so it can be tested without a DOM: the webview suite is pure logic modules.
 */
export function encyclopediaIconTitle(
    icon: EncyclopediaIcon | null | undefined,
): string {
    if (!icon) {
        return l10n.t("No icon resolved for this object");
    }
    if (icon.isMegaTextureStale) {
        return l10n.t(
            "{0} - from a source image; this project's mega texture does not contain it yet, so the game will not show it",
            icon.name,
        );
    }
    switch (icon.source) {
        case "WorkspaceMegaTexture":
            return l10n.t("{0} - from this project's mega texture", icon.name);
        case "LooseSource":
            return l10n.t(
                "{0} - from a source image in this project",
                icon.name,
            );
        case "Fallback":
            return l10n.t(
                "{0} - not found in any mega texture or source folder; showing a placeholder",
                icon.name,
            );
        default:
            return l10n.t("{0} - from the base game", icon.name);
    }
}
