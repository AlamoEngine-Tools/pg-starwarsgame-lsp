// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The credits crawl preview's messages (#208): CreditsPreviewPanel and creditsPreview.tsx. Its rows come
// from the localisation editor beside it, which builds the crawl message the panel passes on.

import type { LocRowDto } from "../localisation";

/** The bundle is listening; a crawl held back can go now. */
export interface CreditsPreviewReady {
    type: "ready";
}

/** The reader closed the preview from inside it. */
export interface CreditsPreviewClose {
    type: "close";
}

export type CreditsPreviewToHost = CreditsPreviewReady | CreditsPreviewClose;

/** The editor's staged rows, played as the game's crawl in one language. */
export interface CreditsCrawl {
    type: "crawl";
    rows: LocRowDto[];
    languages: string[];
    language: string;
}

export type CreditsPreviewToWebview = CreditsCrawl;
