// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { useEffect, useState } from 'react';

/**
 * Follows `value`, but only after it has stopped changing for `delayMs`.
 *
 * The filter box is the reason this exists. Every keystroke re-filtered the whole document, and on
 * the 19,000-row MasterTextFile that is long enough to be felt as the editor locking up mid-word.
 * The box itself stays immediate - what it shows is never delayed - and only the *applied* pattern
 * waits, so the stall lands once at the end of a word instead of once per letter.
 */
export function useDebounced<T>(value: T, delayMs: number): T {
    const [settled, setSettled] = useState(value);

    useEffect(() => {
        const timer = window.setTimeout(() => setSettled(value), delayMs);
        return () => window.clearTimeout(timer);
    }, [value, delayMs]);

    return settled;
}

/**
 * How long the filter box waits before applying what was typed.
 *
 * Long enough that a run of keystrokes filters once, short enough not to read as lag when you stop.
 */
export const FILTER_DEBOUNCE_MS = 120;
