// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// What a combobox's list offers. The rule that matters: opening the list over a field that already
// holds a value offers EVERY option, not just the one that matches the value - filtering starts only
// once the text is edited. A native <datalist> filters by the field's text from the start, which is
// how a set field used to offer nothing but its own value. Pure, so the unit harness covers it.

export interface ComboOption {
    value: string;
    /** Secondary text, shown as the option's tooltip. */
    detail?: string | null;
}

/** The options the list shows: all of them until the text is edited, then those containing it. */
export function visibleOptions(options: readonly ComboOption[], text: string, edited: boolean): ComboOption[] {
    const needle = text.trim().toLowerCase();
    if (!edited || !needle) {
        return [...options];
    }
    return options.filter(o => o.value.toLowerCase().includes(needle));
}

/** The prefix to ask a server for: nothing (everything) until the text is edited. */
export function queryFor(text: string, edited: boolean): string {
    return edited ? text : '';
}

/** Whether the option is the value the field holds, so the list can mark it. */
export function isCurrentOption(option: ComboOption, value: string): boolean {
    const current = value.trim().toLowerCase();
    return current.length > 0 && option.value.toLowerCase() === current;
}

/**
 * What a select-only field's typed text commits to: the option it names exactly (ignoring case),
 * else the only option it still matches, else nothing - typed text is a search, never a value.
 */
export function resolveChoice(options: readonly ComboOption[], text: string): string | null {
    const needle = text.trim().toLowerCase();
    const exact = options.find(o => o.value.toLowerCase() === needle);
    if (exact) {
        return exact.value;
    }
    const matches = visibleOptions(options, text, true);
    return needle && matches.length === 1 ? matches[0].value : null;
}

/**
 * Whether a field's list opens above it: when the room below is short of the list and the room
 * above is larger - a field at the foot of the dock would otherwise drop its list off the window.
 */
export function opensUpward(field: { top: number; bottom: number }, viewportHeight: number,
                            listHeight: number): boolean {
    const below = viewportHeight - field.bottom;
    return below < listHeight && field.top > below;
}

/** The highlighted row after an arrow key, wrapping at both ends; -1 when the list is empty. */
export function moveHighlight(index: number, delta: number, count: number): number {
    if (count <= 0) {
        return -1;
    }
    if (index < 0) {
        return delta > 0 ? 0 : count - 1;
    }
    return (index + delta + count) % count;
}
