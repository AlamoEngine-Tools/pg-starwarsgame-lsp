// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Non-ASCII inside a string literal, in C# and in TypeScript.
//
// Every string a reader sees is ASCII: no arrows, dashes, ellipses or emoji. It is a rule because
// each of those has cost something real - a `-` rendered as a box in the output panel, a hover
// whose emoji pushed the path off the line, a diagnostic whose en dash could not be pasted back
// into the XML it was quoting.
//
// It is a TEXT check rather than a compiler one for the same reason `lint-styled-css` is: no
// language reports a legal character it merely dislikes, and a review cannot see the difference
// between `-` and `-` on the screen it is being reviewed on.
//
// What it does NOT look at is comments. The repo's section dividers are box-drawing runs, 62,000
// characters of them, and they reach nobody. Only literals are scanned, so a divider or a comment
// that quotes a glyph the UI draws is left alone.

/** The one character allowed through anywhere: the section-divider run used in comments. */
const DIVIDER = '─';

/** How each language spells a comment and a string, which is the whole of what this needs. */
const LANGS = { cs: 'cs', ts: 'ts' };

/** What to write instead, where there is an obvious answer. Advice only; the check is the same. */
const SUGGESTIONS = new Map([
    ['→', "'->'"],
    ['←', "'<-'"],
    ['–', "'-'"],
    ['—', "'-'"],
    ['…', "'...'"],
    ['·', "' - '"],
    ['×', "'x'"],
    ['≥', "'>='"],
    ['≤', "'<='"],
    ['▲', "'^'"],
    ['▽', "'v'"],
]);

/**
 * Blanks out every comment, so the literal scan cannot see into one.
 *
 * Walks the file once rather than matching, because a `//` inside a string and a quote inside a
 * comment are both ordinary and neither can be told apart by a regex. The characters are replaced
 * with spaces rather than removed, so every offset still points at the real line.
 */
function withoutComments(text, lang) {
    const out = [...text];
    const n = text.length;
    let i = 0;

    const blank = (from, to) => {
        for (let k = from; k < to && k < n; k++) {
            if (out[k] !== '\n') { out[k] = ' '; }
        }
    };

    while (i < n) {
        // A C# verbatim or raw string, where a backslash is not an escape.
        if (lang === LANGS.cs && text.startsWith('"""', i)) {
            const end = text.indexOf('"""', i + 3);
            i = end === -1 ? n : end + 3;
            continue;
        }

        if (lang === LANGS.cs && (text.startsWith('@"', i) || text.startsWith('$@"', i)
            || text.startsWith('@$"', i))) {
            i = text.indexOf('"', i) + 1;
            while (i < n) {
                if (text[i] === '"') {
                    // A doubled quote is one quote, not the end.
                    if (text[i + 1] === '"') { i += 2; continue; }
                    i++;
                    break;
                }
                i++;
            }
            continue;
        }

        const c = text[i];

        if (c === '"' || c === "'" || (lang === LANGS.ts && c === '`')) {
            const quote = c;
            i++;
            while (i < n) {
                if (text[i] === '\\') { i += 2; continue; }
                if (text[i] === quote) { i++; break; }
                // A single-quoted string does not span lines in either language; a template does.
                if (quote !== '`' && text[i] === '\n') { break; }
                i++;
            }
            continue;
        }

        if (text.startsWith('//', i)) {
            const end = text.indexOf('\n', i);
            blank(i, end === -1 ? n : end);
            i = end === -1 ? n : end;
            continue;
        }

        if (text.startsWith('/*', i)) {
            const end = text.indexOf('*/', i + 2);
            blank(i, end === -1 ? n : end + 2);
            i = end === -1 ? n : end + 2;
            continue;
        }

        i++;
    }

    return out.join('');
}

/**
 * Every string literal in the file, as [body, offset of the body] pairs.
 *
 * A template literal's body keeps its CSS comments blanked: the styled stylesheets carry comments
 * that name the glyph a button draws, and those are comments however they are nested.
 */
function literals(text, lang) {
    const found = [];
    const n = text.length;
    let i = 0;

    while (i < n) {
        if (lang === LANGS.cs && text.startsWith('"""', i)) {
            const end = text.indexOf('"""', i + 3);
            const stop = end === -1 ? n : end;
            found.push([text.slice(i + 3, stop), i + 3]);
            i = end === -1 ? n : end + 3;
            continue;
        }

        if (lang === LANGS.cs && (text.startsWith('@"', i) || text.startsWith('$@"', i)
            || text.startsWith('@$"', i))) {
            const start = text.indexOf('"', i) + 1;
            let k = start;
            while (k < n) {
                if (text[k] === '"') {
                    if (text[k + 1] === '"') { k += 2; continue; }
                    break;
                }
                k++;
            }
            found.push([text.slice(start, k), start]);
            i = k + 1;
            continue;
        }

        const c = text[i];

        if (c === '"' || c === "'" || (lang === LANGS.ts && c === '`')) {
            const quote = c;
            const start = i + 1;
            let k = start;
            let closed = false;

            while (k < n) {
                if (text[k] === '\\') { k += 2; continue; }
                if (text[k] === quote) { closed = true; break; }
                if (quote !== '`' && text[k] === '\n') { break; }
                k++;
            }

            const body = quote === '`' ? blankCssComments(text.slice(start, k)) : text.slice(start, k);
            found.push([body, start]);
            i = closed ? k + 1 : k;
            continue;
        }

        if (text.startsWith('//', i)) {
            const end = text.indexOf('\n', i);
            i = end === -1 ? n : end;
            continue;
        }

        if (text.startsWith('/*', i)) {
            const end = text.indexOf('*/', i + 2);
            i = end === -1 ? n : end + 2;
            continue;
        }

        i++;
    }

    return found;
}

/** A styled template's own comments, blanked in place so offsets survive. */
function blankCssComments(body) {
    return body.replace(/\/\*[\s\S]*?\*\//g, run => run.replace(/[^\n]/g, ' '));
}

/**
 * Every non-ASCII character sitting in a string literal.
 *
 * @param text the whole file.
 * @param options.lang `cs` or `ts`.
 * @returns offences in file order, each with the character, its index and the line it is on.
 */
function stringOffences(text, { lang }) {
    const offences = [];
    const scanned = withoutComments(text, lang);

    for (const [body, offset] of literals(scanned, lang)) {
        for (let k = 0; k < body.length; k++) {
            const ch = body[k];

            if (ch.codePointAt(0) <= 126 || ch === DIVIDER) {
                continue;
            }

            // A surrogate pair is one character to a reader, so report it as one.
            const point = body.codePointAt(k);
            const char = String.fromCodePoint(point);
            k += char.length - 1;

            offences.push({
                char,
                index: offset + k,
                line: text.slice(0, offset + k).split('\n').length,
                suggestion: SUGGESTIONS.get(char) ?? null,
            });
        }
    }

    return offences;
}

module.exports = { stringOffences, literals, withoutComments, LANGS, DIVIDER, SUGGESTIONS };
