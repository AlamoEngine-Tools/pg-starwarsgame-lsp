// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The What's New page's three links, built in the page from constants.
//
// Built here rather than carried in the host's message on purpose: a URL that arrives through
// postMessage is data from outside the page, and an `href` taking such a string is a redirect
// sink - the code-scanning rules js/xss and js/client-side-unvalidated-url-redirection flagged
// exactly that. A constant prefix with the version encoded after it is a URL the page vouches for.

const REPOSITORY = "https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp";

/** The GitHub release of a version: the tag is `v` and the version. */
export function releaseUrl(version: string): string {
    return `${REPOSITORY}/releases/tag/v${encodeURIComponent(version)}`;
}

export const ISSUES_URL = `${REPOSITORY}/issues`;
export const DISCUSSIONS_URL = `${REPOSITORY}/discussions`;
