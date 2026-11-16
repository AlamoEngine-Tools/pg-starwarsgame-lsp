// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The What's New page's messages (#208): WhatsNewPanel and whatsNew.tsx. The payload used to be declared
// twice, once on each side; it is declared here once.

/** One version's section of the changelog. */
export interface ReleaseSection {
    /** The heading text: a version such as `0.5.0`, or `Unreleased` in a development build. */
    readonly version: string;
    /**
     * The `### Highlights` block, heading removed and trimmed: a release's lead, with images.
     * Undefined when the section has none.
     */
    readonly highlights?: string;
    /**
     * The `### Migration` block, heading removed and trimmed: what to change after the update.
     * Undefined when the section has none.
     */
    readonly migration?: string;
    /** The rest of the section as written: Features, Fixes, Diagnostic IDs, Breaking changes. */
    readonly body: string;
}

/** The page is listening; the notes can go now. */
export interface WhatsNewReady {
    type: "ready";
}

export type WhatsNewToHost = WhatsNewReady;

/**
 * What the webview draws. Markdown throughout; the page renders it. The page's links are not
 * here: it builds them itself from constants, so that no URL reaches an `href` through a message.
 */
export interface WhatsNewPayload {
    readonly type: "notes";
    /** The installed version, for the title and the release link. */
    readonly version: string;
    /** The manifest's preview flag, the one the Marketplace badges the extension with. */
    readonly preview: boolean;
    /** The section the page opens on, images inlined. Undefined when the changelog has none. */
    readonly current?: ReleaseSection;
    /**
     * The sections after the current one in file order, which is newest first: the releases
     * before the installed one, images reduced to their alt text. Neither `Unreleased` nor a
     * version newer than the installed one is an earlier release.
     */
    readonly older: readonly {
        readonly version: string;
        readonly markdown: string;
    }[];
}

export type WhatsNewToWebview = WhatsNewPayload;
