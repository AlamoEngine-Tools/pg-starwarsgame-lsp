// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * The image subtypes an icon may legitimately be. SVG is deliberately absent: it is the one image
 * format that can carry script, so allowing it would give back exactly what this guard removes.
 */
const ALLOWED_SUBTYPES = ['png', 'jpeg', 'jpg', 'gif', 'webp', 'bmp'];

/**
 * `data:image/<subtype>;base64,<base64>` and nothing else - the exact shape the server emits.
 *
 * Anchored at both ends, and the payload is restricted to the base64 alphabet so a quote or an
 * angle bracket cannot ride along inside it.
 */
const DATA_IMAGE = new RegExp(
    `^data:image/(?:${ALLOWED_SUBTYPES.join('|')});base64,[A-Za-z0-9+/]*={0,2}$`,
    'i');

/**
 * The value to hand an `<img src>`, or `undefined` when it is not a data-image URI this webview
 * produced.
 *
 * Every icon the webviews draw arrives as `data:image/png;base64,...`, built server-side from the
 * image's bytes (`GetEncyclopediaEntryHandler`). Nothing else is ever legitimate, so this ALLOWS
 * that one shape rather than trying to strip dangerous ones - a deny-list on URLs is a guessing
 * game, and an allow-list here costs nothing because the contract is so narrow.
 *
 * Why it matters even though the values come from our own server: what the server encodes comes
 * from files in the workspace, so the content is ultimately author-supplied, and a webview is a
 * privileged context. An unchecked string reaching `src` could name a remote URL - a network
 * callback fired merely by opening a file, which is a signal the author never agreed to send.
 *
 * Returning `undefined` rather than a placeholder is deliberate: `<img src={undefined}>` renders
 * nothing and makes no request, and the callers already draw an empty-slot state.
 */
export function safeImageSource(value: string | null | undefined): string | undefined {
    if (!value) {
        return undefined;
    }

    return DATA_IMAGE.test(value.trim()) ? value : undefined;
}
