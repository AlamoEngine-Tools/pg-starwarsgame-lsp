// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Reading a value back out of storage that outlives the build which wrote it.
//
// Every one of these takes `unknown` and answers something usable. Nothing throws and nothing
// refuses a whole blob over one bad field: a settings file written by an older build, or edited by
// hand, must cost the reader the field it got wrong rather than the panel. A preview that opens
// blank because of one bad string has no way back.
//
// Shared because two stores at two different TIERS read the same way - the room in `globalState`
// and the weapon bench in `workspaceState`. They were one module and one blob until the bench moved;
// keeping one copy of the rules is what stops the two drifting.

/** An object to read fields off, or an empty one. Arrays are not records. */
export function asRecord(value: unknown): Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
        ? value as Record<string, unknown>
        : {};
}

export function boolean_(value: unknown, fallback: boolean): boolean {
    return typeof value === 'boolean' ? value : fallback;
}

/** A list of names, keeping the good entries. One bad string is not a reason to unfold the lot. */
export function names(value: unknown): string[] {
    return Array.isArray(value) ? value.filter(entry => typeof entry === 'string') : [];
}

export function string_(value: unknown, fallback: string): string {
    return typeof value === 'string' ? value : fallback;
}

export function nullableString(value: unknown): string | null {
    return typeof value === 'string' ? value : null;
}

export function number_(
    value: unknown, fallback: number, range: { min: number; max: number },
): number {
    if (typeof value !== 'number' || !Number.isFinite(value)) {
        return fallback;
    }

    return Math.min(range.max, Math.max(range.min, value));
}
