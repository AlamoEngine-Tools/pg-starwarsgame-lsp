// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

/**
 * What a navigator shows when the server comes back with nothing.
 *
 * The distinction the tree views were missing: before the workspace scan finishes, an empty answer
 * means "not indexed yet", not "this workspace has none". Both said the latter and then corrected
 * themselves a moment later when the index landed, which reads as the view being wrong twice.
 *
 * Shared so the two navigators cannot drift into answering the same question differently.
 */
export function emptyNavigatorMessage(scanned: boolean, what: string): string {
    return scanned ? `No ${what} found in this workspace.` : 'Loading...';
}
