// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The text of an integer field. Whole numbers only: a native number input would also take 1.5 and
// 1e3, which no integer value in the game accepts. Pure, so the unit harness covers it.

const WHOLE = /^-?\d+$/;

/** The field's text as a whole number, or null when it is not one. */
export function parseInteger(text: string): number | null {
    const trimmed = text.trim();
    return WHOLE.test(trimmed) ? Number(trimmed) : null;
}

/** The text after the decrement or increment button, stepping from zero when the field holds none. */
export function stepInteger(text: string, delta: number): string {
    return String((parseInteger(text) ?? 0) + delta);
}
