// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * `data:image/<subtype>;base64,<base64>` and nothing else - the exact shape the server emits.
 *
 * Anchored at both ends, and the payload is restricted to the base64 alphabet so a quote or an
 * angle bracket cannot ride along inside it. SVG is deliberately absent from the subtypes: it is
 * the one image format that can carry script, so allowing it would give back exactly what this
 * guard removes.
 *
 * Written as a LITERAL rather than built with `new RegExp(...)` from a subtype list. That is not
 * style: a static analyser can see that a literal is anchored and treat it as a sanitising guard,
 * while a dynamically assembled pattern is opaque to it. The first version of this file composed
 * the pattern from an array, and CodeQL went on reporting js/xss and
 * js/client-side-unvalidated-url-redirection at every call site because it could not tell what
 * the guard admitted.
 */
const DATA_IMAGE = /^data:image\/(?:png|jpeg|jpg|gif|webp|bmp);base64,[A-Za-z0-9+/]*={0,2}$/i;

/** The literal prefix every allowed value carries - see the `startsWith` guard below. */
const DATA_IMAGE_PREFIX = 'data:image/';

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

    // Trim FIRST, then test and return that same string. The earlier version tested `value.trim()`
    // and returned `value`, so the string that was checked was not the string that was handed on -
    // a gap in its own right, and the reason a taint analyser could not follow the guard either.
    const uri = value.trim();

    // Two guards, and the first is not redundant. A literal-prefix `startsWith` is the shape a
    // taint analyser recognises as a barrier; the regex below is what actually constrains the
    // value. Keeping both means the check reads the same to a human and to CodeQL - the first
    // attempt here carried only the regex and every call site went on being reported.
    if (!uri.startsWith(DATA_IMAGE_PREFIX)) {
        return undefined;
    }

    if (!DATA_IMAGE.test(uri)) {
        return undefined;
    }

    return uri;
}
