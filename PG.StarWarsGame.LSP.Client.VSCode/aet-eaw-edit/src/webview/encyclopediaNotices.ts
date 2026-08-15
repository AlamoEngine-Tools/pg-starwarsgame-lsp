// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Everything the preview has to say about the card it is showing, as data.
//
// These used to be five coloured paragraphs scattered through the dock, each beside the control it
// happened to relate to. Collecting them here does two things: the dock's top level can report a
// single count and severity the way every other editor's does, and the wording becomes testable -
// which matters, because most of these describe a fault in the MOD's data and the whole value of
// saying so is naming the fix.

import { GetEncyclopediaEntryResult } from '../protocol/encyclopedia';
import { isSubstitutedFont } from './encyclopediaFonts';
import { worstSeverity } from './loc/validateState';
import { ValidationState } from './loc/useLocPanel';

export interface EncyclopediaNotice {
    /**
     * `warning` for something wrong with the data the author can fix; `info` for a limitation of
     * the preview itself.
     *
     * The split is deliberate and load-bearing. Font substitution applies to nearly every card and
     * is never the author's to fix, so raising it as a warning would leave the control permanently
     * yellow - and a tag that cries warning at healthy data is one people stop reading.
     */
    severity: 'info' | 'warning';
    message: string;
}

/** Warnings first: the worst of it should read first, whatever order it was collected in. */
const RANK: Record<EncyclopediaNotice['severity'], number> = { warning: 0, info: 1 };

/**
 * What the preview has to report about `entry`, given how the panel is currently set up.
 *
 * Takes the panel's own view state, because two of these depend on it: only the SELECTED faction
 * slot's missing artwork is worth mentioning (the card draws one frame), and the missing
 * multiplayer body is only news to someone who asked for the multiplayer body.
 */
export function encyclopediaNotices(
    entry: GetEncyclopediaEntryResult | null,
    view: { multiplayer: boolean; factionSlot: number },
): EncyclopediaNotice[] {
    // Nothing is drawn for an object with no entry, so there is nothing to report about it.
    if (entry === null || !entry.found) { return []; }

    const notices: EncyclopediaNotice[] = [];

    const icon = entry.icon;
    if (icon) {
        // The server only sends its placeholder when the object NAMED an icon and no layer had it,
        // so this is always a real dangling reference rather than an object that simply has no art.
        if (icon.source === 'Fallback') {
            notices.push({
                severity: 'warning',
                message: `No artwork anywhere for '${icon.name}' - the card is showing the `
                    + 'missing-icon placeholder.',
            });
        } else if (icon.isMegaTextureStale) {
            notices.push({
                severity: 'warning',
                message: `'${icon.name}' was found as a source image but is not in the workspace `
                    + 'mega texture. Rebuild the MTD, or the game will not show it.',
            });
        }
    }

    const ships = entry.shipNames;
    if (ships && !ships.fileFound) {
        notices.push({
            severity: 'warning',
            message: `Ship name file not found: ${ships.sourcePath}. The card falls back to the `
                + 'class line.',
        });
    } else if (ships && ships.names.length === 0) {
        // A different fault with a different fix, so it gets its own sentence rather than being
        // folded into the one above: the path is right and the encoding is not.
        notices.push({
            severity: 'warning',
            message: `No names read from ${ships.sourcePath}. These files must be UTF-16 with a `
                + 'byte order mark, which is how the game ships them.',
        });
    } else if (ships) {
        // Nothing is wrong here - it explains why this card shows a name where every other object
        // shows a class, which is surprising the first time you meet it. Only said when a name is
        // actually drawn: with an empty pool the card falls back to the class line, and claiming
        // otherwise would be untrue.
        notices.push({
            severity: 'info',
            message: 'This object draws an individual name instead of showing its class. The game '
                + 'picks one and remembers which are used; the preview just picks, and keeps its '
                + `pick while this panel is open. Names come from ${ships.sourcePath}.`,
        });
    }

    const frame = entry.chrome?.factionFrames?.[view.factionSlot];
    if (frame !== undefined && (frame.image === null || frame.image === undefined)) {
        notices.push({
            severity: 'warning',
            message: `No artwork in the mega texture for '${frame.textureName}' - the card falls `
                + 'back to its measured border.',
        });
    }

    const fonts = substitutedFonts(entry);
    if (fonts.length > 0) {
        notices.push({
            severity: 'info',
            message: `${fonts.join(' and ')} ${fonts.length > 1 ? 'are' : 'is'} substituted. The `
                + 'game ships those fonts inside its executable under a commercial licence, so they '
                + 'are not ours to redistribute; glyph widths in the affected rows are approximate.',
        });
    }

    if (view.multiplayer && !entry.usedMultiplayerBody) {
        notices.push({
            severity: 'info',
            message: 'No MP_Encyclopedia_Text on this object - showing the single-player body.',
        });
    }

    return notices.sort((a, b) => RANK[a.severity] - RANK[b.severity]);
}

/**
 * The level the dock's tag shows.
 *
 * Delegates to the rule the story graph and the localisation grids already use, so the same
 * situation is coloured the same way in all three - the point of the tag is that it means one thing
 * across the extension.
 */
export function noticeSeverity(notices: readonly EncyclopediaNotice[]): ValidationState {
    return worstSeverity(notices);
}

/**
 * Which game fonts this card asks for that the preview cannot draw with, in row order and without
 * repeats.
 *
 * Read off the layout rather than hardcoded: a mod that re-fonts the affected rows to something
 * installed should say nothing at all.
 */
function substitutedFonts(entry: GetEncyclopediaEntryResult): string[] {
    const { header, body, rightText, centerText, costText } = entry.layout;
    return [...new Set(
        [header, body, rightText, centerText, costText]
            .map(s => s.fontName)
            .filter(isSubstitutedFont))];
}
