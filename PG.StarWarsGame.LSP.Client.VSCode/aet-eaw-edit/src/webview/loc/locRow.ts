// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The row shape both editors read, and the only thing they share about a file's contents.
//
// How a row is *addressed* is what differs - translations by key, credits by position - so that
// lives in each editor's own staging module rather than here.

export interface LocValue { language: string; value: string; }

/**
 * One row as the server read it.
 *
 * `index` is the row's position in the file. The translation editor never addresses anything by it
 * - it edits by key - but it is still what the grid uses to identify a row in the DOM and to scroll
 * a freshly added one into view, so it stays equal to the row's position in both editors.
 */
export interface LocRow { index: number; key: string; values: LocValue[]; }
