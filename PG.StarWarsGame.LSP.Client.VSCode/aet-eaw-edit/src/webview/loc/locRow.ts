// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The row shape both editors read, and the only thing they share about a file's contents.
//
// How a row is *addressed* is what differs - translations by key, credits by position - so that
// lives in each editor's own staging module rather than here.

// The shapes themselves are the server's, declared in ../../protocol. Named here in the grid's own
// vocabulary - a row on screen, not a row on the wire - because that is what every consumer in this
// folder is talking about, and because `index` carries a meaning below that the wire does not fix.
import { LocRowDto, LocValueDto } from '../../protocol';

export type LocValue = LocValueDto;

/**
 * One row as the server read it.
 *
 * `index` is the row's position in the file. The translation editor never addresses anything by it
 * - it edits by key - but it is still what the grid uses to identify a row in the DOM and to scroll
 * a freshly added one into view, so it stays equal to the row's position in both editors.
 */
export type LocRow = LocRowDto;
