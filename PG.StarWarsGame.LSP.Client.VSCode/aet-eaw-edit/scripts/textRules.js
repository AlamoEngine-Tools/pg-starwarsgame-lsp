// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The rules every user-facing text follows, as one check over a catalogue entry. CLAUDE.md states
// them; each was a correction before it was a rule. They run over the TEXTS, wherever they are
// kept - the manifest catalogue, the l10n bundle and the server's ServerMessages.resx - so a
// translator's source and the English a reader sees are held to the same thing.
//
// Code is not text: a span in backticks, a quoted value and a `{0}` placeholder are replaced before
// the word rules run, so `yourSetting` or 'aet-eaw-edit.lsp.locale' never trips them.

/** Code spans, quoted values, placeholders and link targets, which carry no prose of their own. */
const CODE = /`[^`]*`|'[^']*'|"[^"]*"|\{\d+[^}]*\}|\]\([^)]*\)|https?:\/\/\S+/g;

/** The reader addressed directly. */
const SECOND_PERSON = /\b(you|your|yours|yourself|yourselves)\b/i;

/** A colon followed by a lower-case word that is plain prose rather than an identifier. */
const LOWER_AFTER_COLON = /: ([a-z]+)(?![\w.\-/<])/;

/** A sentence boundary before the last character: the text runs to more than one clause. */
const INNER_BOUNDARY = /[.!?;:]\s/;

/**
 * A count of the game's own data: a grouped number, or a number of things the game holds. A limit,
 * a port or a range is a number too, but it says what the author may write, not how many of
 * something shipped - which is what reaches nobody and goes stale with every patch.
 */
const COUNT =
    /(?<![\w.])(\d{1,3}(?:,\d{3})+|\d+ (?:files|objects|units|entries|scripts|models|particles|emitters|diagnostics|declarations|tags|types|events|threads|plots))\b/;

/**
 * The rules a text breaks.
 *
 * @param {string} text The text as the catalogue holds it.
 * @param {{label?: boolean}} [options] `label`: the text is a label or a one-clause tooltip, where a
 *     full stop decorates rather than separates.
 * @returns {{rule: string, detail: string}[]} One entry per broken rule.
 */
function textProblems(text, options = {}) {
    const problems = [];
    const prose = text.replace(CODE, "X");

    const foreign = [...text].find((c) => c.charCodeAt(0) > 127);
    if (foreign !== undefined) {
        problems.push({rule: "ascii", detail: `'${foreign}' is not ASCII`});
    }

    const second = SECOND_PERSON.exec(prose);
    if (second) {
        problems.push({rule: "second-person", detail: `'${second[1]}' addresses the reader`});
    }

    const lower = LOWER_AFTER_COLON.exec(prose);
    if (lower) {
        problems.push({rule: "capital-after-colon", detail: `'${lower[1]}' after ': '`});
    }

    if (
        options.label &&
        /[^.]\.$/.test(text) &&
        !INNER_BOUNDARY.test(prose.slice(0, -1))
    ) {
        problems.push({rule: "label-stop", detail: "a one-clause label ends in a full stop"});
    }

    const count = COUNT.exec(prose);
    if (count) {
        problems.push({rule: "corpus-count", detail: `'${count[1]}' counts the game's data`});
    }

    return problems;
}

module.exports = {textProblems};
