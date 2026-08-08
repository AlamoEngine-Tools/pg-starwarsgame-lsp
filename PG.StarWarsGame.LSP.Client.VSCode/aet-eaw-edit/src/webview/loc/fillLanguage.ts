// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which languages the "Fill in a language" dialog may offer as the source of the text.
//
// Both of its dropdowns used to list every language the file has, unfiltered - so the language you
// were filling *in* was also offered to copy *from*, and picking it left the Fill button disabled
// with a note telling you to pick two different ones. On a single-language file that was the only
// thing on offer: the dropdown listed that file's one language and nothing could ever be confirmed.

/** Where the text being filled in comes from. */
export type FillSource =
    /** Another language column of this same file. */
    | 'language'
    /** What the game itself ships. */
    | 'baseline'
    /** A sibling language file of this project, copied in wholesale. */
    | 'file';

/**
 * The source actually in effect, given what this file can offer.
 *
 * Returns exactly one, which is the point: the dialog used to derive a boolean per source and read
 * "from another language" as `!fromGame`. That was true while there were two sources and quietly
 * wrong once there were three - two radios read as selected at once, and the language dropdown
 * rendered beside the file one under the same label. One value cannot be two things.
 *
 * A choice this file cannot satisfy falls back rather than leaving the dialog on a source that can
 * never produce anything - which is what left Fill permanently disabled.
 */
export function resolveFillSource(
    chosen: FillSource, offers: { canCopy: boolean; canSeed: boolean },
): FillSource {
    if (chosen === 'file' && offers.canSeed) { return 'file'; }
    if (chosen === 'language' && offers.canCopy) { return 'language'; }
    return 'baseline';
}

/** Whether this file has two languages to copy between at all. */
export function canCopyBetweenLanguages(languages: readonly string[]): boolean {
    return copyFromOptions(languages, '').length > 1;
}

/**
 * The languages that may be copied from, given the one being filled in.
 *
 * The target is excluded: copying a language into itself fills nothing, so offering it can only
 * produce a disabled button. Compared case-insensitively, because the language a file declares and
 * the one a dropdown holds have travelled through different layers - see LocValueDto on why these
 * arrive as pairs rather than as dictionary keys.
 */
export function copyFromOptions(
    languages: readonly string[], fillInto: string,
): string[] {
    const target = fillInto.toUpperCase();
    const seen = new Set<string>();

    return languages.filter(language => {
        const upper = language.toUpperCase();
        if (upper === target || seen.has(upper)) { return false; }
        seen.add(upper);
        return true;
    });
}

/**
 * The source to select once the target changes.
 *
 * Keeps the current choice when it is still offerable, so changing what you are filling in does not
 * silently re-aim where the text comes from. Falls back to the first remaining language - which,
 * with two languages, is the only one left and therefore the only thing the user could have meant.
 */
export function nextCopyFrom(
    languages: readonly string[], fillInto: string, current: string,
): string {
    const options = copyFromOptions(languages, fillInto);
    return options.some(l => l.toUpperCase() === current.toUpperCase())
        ? current
        : options[0] ?? '';
}
