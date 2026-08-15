// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The rules for ingesting Petroglyph's published shader archive.
//
// Kept free of the filesystem, the network and vscode so the parts that decide what is SAFE can be
// tested directly. Everything here answers a question about one archive entry or one download;
// `shaderSources.ts` does the fetching and writing.

import { createHash } from 'node:crypto';
import * as path from 'node:path';

/** What an archive entry is allowed to become, or why it was refused. */
export type EntryVerdict =
    | { keep: true; relativePath: string }
    | { keep: false; reason: string };

/**
 * The extensions worth extracting.
 *
 * An allow-list rather than "everything in the zip": we are unpacking a third-party archive into a
 * directory the extension later reads, and the only thing wanted from it is shader source text.
 */
const WANTED = ['.fx', '.fxh'];

/**
 * Whether one archive entry may be written, and where.
 *
 * The important half is ZIP-SLIP. A zip entry names its own path, and nothing stops that path being
 * `../../…` or absolute - an archive can otherwise write anywhere the editor can. So the resolved
 * destination is required to stay inside the target directory, and that check is done on the
 * RESOLVED path rather than by looking for `..` in the text, because `a/../../b` and backslash
 * separators both slip a naive scan.
 */
export function entryVerdict(entryName: string, targetDirectory: string): EntryVerdict {
    // Zip paths are '/'-separated by spec, but archives in the wild carry backslashes too.
    const normalised = entryName.replace(/\\/g, '/');

    if (normalised === '' || normalised.endsWith('/')) {
        return { keep: false, reason: 'directory entry' };
    }

    if (path.isAbsolute(normalised) || /^[A-Za-z]:/.test(normalised)) {
        return { keep: false, reason: 'absolute path' };
    }

    if (!WANTED.includes(path.extname(normalised).toLowerCase())) {
        return { keep: false, reason: 'not a shader source' };
    }

    // Flattened deliberately: the archive nests everything under `Shaders/`, and the resolver looks
    // for `.fx` files directly in the configured directory. Flattening also removes every chance of
    // a crafted path escaping, because only the base name is ever used.
    const leaf = path.basename(normalised);
    const destination = path.resolve(targetDirectory, leaf);
    const root = path.resolve(targetDirectory);

    if (destination !== path.join(root, leaf)
        || !destination.startsWith(root + path.sep)) {
        return { keep: false, reason: 'escapes the target directory' };
    }

    return { keep: true, relativePath: leaf };
}

/** The SHA-256 of some bytes, lower-case hex - the form the pinned constant is written in. */
export function sha256(bytes: Uint8Array): string {
    return createHash('sha256').update(bytes).digest('hex');
}

/** What a download turned out to be. */
export type ArchiveCheck =
    | { ok: true }
    | { ok: false; problem: string };

/**
 * Whether a downloaded archive is the one we pinned.
 *
 * A mismatch is reported rather than tolerated. The URL is public and unauthenticated, so the
 * checksum is the only thing standing between the extension and whatever the network handed back -
 * and a re-publish by Petroglyph should be a clean, actionable failure rather than the silent
 * ingestion of different bytes.
 */
export function checkArchive(
    bytes: Uint8Array, expectedSha256: string, expectedBytes?: number,
): ArchiveCheck {
    if (bytes.length === 0) {
        return { ok: false, problem: 'the download was empty' };
    }

    if (expectedBytes !== undefined && bytes.length !== expectedBytes) {
        return {
            ok: false,
            problem: `expected ${expectedBytes} bytes but got ${bytes.length}`,
        };
    }

    const actual = sha256(bytes);
    if (actual.toLowerCase() !== expectedSha256.toLowerCase()) {
        return {
            ok: false,
            problem: 'the checksum did not match. The archive may have been re-published, or '
                + `something on the network replaced it. Expected ${expectedSha256}, got ${actual}`,
        };
    }

    return { ok: true };
}

/**
 * Whether an extraction produced something usable.
 *
 * A zip that unpacks cleanly and yields no shaders is a failure with a friendly face - it would
 * leave the preview reporting "no sources" with a directory configured, which is a worse place to
 * debug from than having never run the download.
 */
export function extractionVerdict(written: number): ArchiveCheck {
    return written > 0
        ? { ok: true }
        : { ok: false, problem: 'the archive held no .fx files' };
}
