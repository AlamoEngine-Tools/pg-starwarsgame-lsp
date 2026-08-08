// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Which language columns a grid is showing, and the CSS template that lays them out.
//
// Both editors had written this out identically - the same three memos, the same toggle, the same
// pair of refs behind it, and the same `260px ... 32px` template string. That last one is the
// reason it is shared rather than merely similar: if one editor ever changed the key column's
// width, the two grids would stop lining up and nothing would catch it.

import { useCallback, useMemo, useRef } from 'react';

import { defaultHiddenLanguages } from './columnVisibility';
import { LocRow } from './locRow';

export interface LocColumns {
    /** The languages hidden right now, whether by default or by hand. */
    hidden: Set<string>;
    /** The languages on screen, in declaration order. */
    shownLanguages: string[];
    /** `grid-template-columns` for the header and every row, so the two cannot drift apart. */
    columns: string;
    /** Shows or hides one language column. */
    toggleLanguage: (language: string, visible: boolean) => void;
    /**
     * Marks the current visibility as chosen by hand.
     *
     * For the caller that changes what is shown by some other route - adding a language - and needs
     * the new column to stay visible rather than be hidden again for being empty.
     */
    materialise: () => void;
}

export function useLocColumns(options: {
    rows: LocRow[];
    languages: string[];
    /**
     * Null means "the user has not chosen", in which case the languages the file says nothing in
     * are hidden. Once they touch the picker their choice is explicit and stands - including for a
     * language they have just added and not yet written anything in.
     */
    hiddenLanguages: Set<string> | null;
    setHiddenLanguages: React.Dispatch<React.SetStateAction<Set<string> | null>>;
    /**
     * The language to show alone, when the tab was opened on one language of a set. Null for the
     * ordinary case, where the default is to hide the empty columns instead.
     */
    focusLanguage?: string | null;
    /**
     * Called when a column is hidden, with the language.
     *
     * Both editors use it for the same thing: hiding the column the search is scoped to would leave
     * the grid filtered by something the user can no longer see, so the scope goes back to
     * everything.
     */
    onHidden?: (language: string) => void;
}): LocColumns {
    const {
        rows, languages, hiddenLanguages, setHiddenLanguages, focusLanguage = null, onHidden,
    } = options;

    // Read by toggleLanguage, which must not be re-created on every row change - it is passed to
    // the column menu, and a new identity there re-renders the menu on every keystroke.
    const rowsRef = useRef(rows);
    const languagesRef = useRef(languages);
    const focusRef = useRef(focusLanguage);
    rowsRef.current = rows;
    languagesRef.current = languages;
    // Read through a ref for the same reason as the two above, and it must be here rather than
    // left out: the default the first click starts from has to be the default on screen.
    focusRef.current = focusLanguage;

    /** The default both the render and the first click must agree on. */
    const currentDefault = (): string[] =>
        defaultHiddenLanguages(rowsRef.current, languagesRef.current, focusRef.current);

    const hidden = useMemo(
        () => hiddenLanguages ?? new Set(defaultHiddenLanguages(rows, languages, focusLanguage)),
        [hiddenLanguages, rows, languages, focusLanguage]);

    const shownLanguages = useMemo(
        () => languages.filter(l => !hidden.has(l)), [languages, hidden]);

    // A trailing track for the column picker, which sits at the right-hand end of the header.
    const columns = useMemo(
        () => `260px ${shownLanguages.map(() => 'minmax(160px, 1fr)').join(' ')} 32px`,
        [shownLanguages]);

    const materialise = useCallback(() => {
        // Materialises the default, so a later explicit choice does not silently reveal every
        // column the default was hiding.
        setHiddenLanguages(current => new Set(current ?? currentDefault()));
    }, [setHiddenLanguages]);

    const toggleLanguage = useCallback((language: string, visible: boolean) => {
        setHiddenLanguages(current => {
            // Seeded from what is actually on screen, so ticking a second language ADDS it rather
            // than replacing the whole selection with a different default.
            const next = new Set(current ?? currentDefault());
            if (visible) { next.delete(language); } else { next.add(language); }
            return next;
        });
        if (!visible) { onHidden?.(language); }
    }, [setHiddenLanguages, onHidden]);

    return { hidden, shownLanguages, columns, toggleLanguage, materialise };
}
