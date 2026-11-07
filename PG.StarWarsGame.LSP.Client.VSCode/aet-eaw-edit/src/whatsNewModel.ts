// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The What's New page's model: the changelog split into releases, the start classified against
// the version stored last time, and the images of a section inlined for the webview. Pure
// functions over text, except `readImageAsDataUri`, which is the one file read.
//
// The changelog is the single source of the release notes: the Marketplace renders the file, the
// release workflow cuts the `## <version>` section out of it for the GitHub release, and this
// page renders the same section. Nothing here is a second copy of any text.

import fs from "node:fs";
import path from "node:path";

/** One `## <heading>` section of the changelog. */
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

const LEAD_BLOCKS = new Map<string, "highlights" | "migration">([
    ["### Highlights", "highlights"],
    ["### Migration", "migration"],
]);

/**
 * Splits the changelog at its `## ` headings. Text before the first one (the `# Changelog` title)
 * is dropped. Inside a section the Highlights and Migration blocks, each running to the next
 * `### ` heading, are lifted out; every other block stays in the body in file order.
 */
export function splitChangelog(markdown: string): ReleaseSection[] {
    const lines = markdown.replace(/\r\n?/g, "\n").split("\n");
    const sections: ReleaseSection[] = [];
    let version: string | undefined;
    let blocks: {
        kind: "highlights" | "migration" | "body";
        lines: string[];
    }[] = [];

    const flush = () => {
        if (version === undefined) {
            return;
        }
        const pick = (kind: "highlights" | "migration") => {
            const text = blocks
                .filter((b) => b.kind === kind)
                .map((b) => b.lines.join("\n").trim())
                .join("\n\n")
                .trim();
            return text.length > 0 ? text : undefined;
        };
        const body = blocks
            .filter((b) => b.kind === "body")
            .map((b) => b.lines.join("\n"))
            .join("\n")
            .trim();
        sections.push({
            version,
            highlights: pick("highlights"),
            migration: pick("migration"),
            body,
        });
    };

    for (const line of lines) {
        if (line.startsWith("## ")) {
            flush();
            version = line.slice(3).trim();
            blocks = [{ kind: "body", lines: [] }];
            continue;
        }
        if (version === undefined) {
            continue;
        }
        if (line.startsWith("### ")) {
            const kind = LEAD_BLOCKS.get(line.trim()) ?? "body";
            // A lifted block loses its heading; a body block keeps it.
            blocks.push({ kind, lines: kind === "body" ? [line] : [] });
            continue;
        }
        blocks[blocks.length - 1].lines.push(line);
    }
    flush();
    return sections;
}

/**
 * The section the page opens on: the installed version's, or the first section when no heading
 * matches, which is `Unreleased` in a development build whose manifest version is behind.
 */
export function currentSection(
    sections: readonly ReleaseSection[],
    installedVersion: string,
): ReleaseSection | undefined {
    return sections.find((s) => s.version === installedVersion) ?? sections[0];
}

/** Numeric per-part comparison of dotted versions; a missing part is zero. */
export function compareVersions(a: string, b: string): number {
    const pa = a.split(".").map(part);
    const pb = b.split(".").map(part);
    const n = Math.max(pa.length, pb.length);
    for (let i = 0; i < n; i++) {
        const d = (pa[i] ?? 0) - (pb[i] ?? 0);
        if (d !== 0) {
            return d;
        }
    }
    return 0;
}

function part(text: string): number {
    const n = parseInt(text, 10);
    return Number.isFinite(n) ? n : 0;
}

/** How this start relates to the version stored by the last one. */
export type StartKind = "first" | "updated" | "same" | "downgraded";

export function classifyStart(
    storedVersion: string | undefined,
    installedVersion: string,
): StartKind {
    if (storedVersion === undefined) {
        return "first";
    }
    const d = compareVersions(installedVersion, storedVersion);
    return d > 0 ? "updated" : d < 0 ? "downgraded" : "same";
}

/**
 * The page opens by itself on a first start and after an update. A first start shows the current
 * notes because nothing stored the version before this page existed, so every user of the first
 * release carrying it looks like a first install; it stands in for a walkthrough until one exists.
 * A downgrade shows nothing: the notes of a version the reader left are known to them.
 */
export function opensOn(kind: StartKind): boolean {
    return kind === "first" || kind === "updated";
}

/**
 * What activation does: whether to open the page, and the version to store for the next start.
 * The version is stored whenever it changed, setting on or off, so a later update is still told
 * from the one the setting silenced; a start on the same version stores nothing.
 */
export function whatsNewOnStart(
    storedVersion: string | undefined,
    installedVersion: string,
    openAfterUpdate: boolean,
): { open: boolean; store: string | undefined } {
    const kind = classifyStart(storedVersion, installedVersion);
    return {
        open: openAfterUpdate && opensOn(kind),
        store: kind === "same" ? undefined : installedVersion,
    };
}

const IMAGE = /!\[([^\]]*)\]\(([^)\s]+)((?:\s+"[^"]*")?)\)/g;

/**
 * Rewrites every relative image of a Markdown text through `resolve`, which returns a data URI
 * or undefined. An absolute `http(s):` or `data:` source stays as written. An image the resolver
 * cannot supply is reduced to its alt text in italics, or removed when it has none: the webview
 * guard admits nothing but a data URI, so a broken image slot is never drawn.
 */
export function inlineImages(
    markdown: string,
    resolve: (relativePath: string) => string | undefined,
): string {
    return markdown.replace(
        IMAGE,
        (whole, alt: string, src: string, title: string) => {
            if (/^(https?:|data:)/i.test(src)) {
                return whole;
            }
            const uri = resolve(src);
            if (uri !== undefined) {
                return `![${alt}](${uri}${title})`;
            }
            return alt.length > 0 ? `*${alt}*` : "";
        },
    );
}

const IMAGE_TYPES = new Map<string, string>([
    [".png", "image/png"],
    [".jpg", "image/jpeg"],
    [".jpeg", "image/jpeg"],
    [".gif", "image/gif"],
    [".webp", "image/webp"],
]);

/**
 * Reads an image under `baseDir` as a `data:image/<type>;base64,...` URI, the one form the
 * webview's image guard admits. Undefined for a path leaving the base folder, a type the guard
 * refuses (SVG can carry script), or a file that cannot be read.
 */
export function readImageAsDataUri(
    baseDir: string,
    relativePath: string,
): string | undefined {
    const base = path.resolve(baseDir);
    const file = path.resolve(base, relativePath);
    if (!file.startsWith(base + path.sep)) {
        return undefined;
    }
    const type = IMAGE_TYPES.get(path.extname(file).toLowerCase());
    if (type === undefined) {
        return undefined;
    }
    try {
        return `data:${type};base64,${fs.readFileSync(file).toString("base64")}`;
    } catch {
        return undefined;
    }
}

const REPOSITORY = "https://github.com/AlamoEngine-Tools/pg-starwarsgame-lsp";

/** What the webview draws. Markdown throughout; the page renders it. */
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
    readonly links: {
        readonly release: string;
        readonly issues: string;
        readonly discussions: string;
    };
}

/**
 * Builds the page's content from the changelog text. `imageDir` is the folder the changelog's
 * relative image paths are written against: the extension's install folder, where the images
 * ship beside the file.
 */
export function buildWhatsNewPayload(
    changelog: string,
    version: string,
    imageDir: string,
    preview = false,
): WhatsNewPayload {
    const sections = splitChangelog(changelog);
    const picked = currentSection(sections, version);
    const inline = (text: string) =>
        inlineImages(text, (p) => readImageAsDataUri(imageDir, p));
    const current =
        picked === undefined
            ? undefined
            : {
                  version: picked.version,
                  highlights:
                      picked.highlights === undefined
                          ? undefined
                          : inline(picked.highlights),
                  migration:
                      picked.migration === undefined
                          ? undefined
                          : inline(picked.migration),
                  body: inline(picked.body),
              };
    // The earlier sections stay whole, their lifted blocks put back as the headings they were:
    // in a collapsed section the lead needs no special place.
    const older = sections
        .slice(picked === undefined ? 0 : sections.indexOf(picked) + 1)
        .map((s) => ({
            version: s.version,
            markdown: inlineImages(
                [
                    s.highlights === undefined
                        ? ""
                        : `### Highlights\n\n${s.highlights}\n\n`,
                    s.migration === undefined
                        ? ""
                        : `### Migration\n\n${s.migration}\n\n`,
                    s.body,
                ].join(""),
                () => undefined,
            ),
        }));
    return {
        type: "notes",
        version,
        preview,
        current,
        older,
        links: {
            release: `${REPOSITORY}/releases/tag/v${version}`,
            issues: `${REPOSITORY}/issues`,
            discussions: `${REPOSITORY}/discussions`,
        },
    };
}
