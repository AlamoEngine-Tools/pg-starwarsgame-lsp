// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where the base game's shader SOURCES come from, and how the user is helped to get them.
//
// Two ways in, one seam: the user points at their own extracted copy, or the extension fetches
// Petroglyph's published archive for them. Downstream neither is distinguishable - `shaderDirectory`
// answers the same either way.
//
// The download is OPT-IN and never on the activation path. It is a third-party network fetch of an
// archive the extension is forbidden to redistribute, so it happens because someone asked for it,
// at the moment they asked.

import * as fs from 'node:fs';
import * as path from 'node:path';
import { promisify } from 'node:util';

import * as vscode from 'vscode';
import * as yauzl from 'yauzl';

import { checkArchive, entryVerdict, extractionVerdict } from './shaderArchive';
import { hasShaderSources } from './shaderLayout';

export { hasShaderSources };

/**
 * Petroglyph's own published download.
 *
 * A plain constant, deliberately not a build-injected secret. The URL is public, it is visible in
 * the shipped bundle and in any network trace regardless, and hiding it would only cost local runs,
 * fork builds and diff reviewability. The control that matters is the checksum below, which rejects
 * a swapped or tampered file - something secrecy would never have caught.
 */
export const SHADER_ARCHIVE_URL = 'https://www.petroglyphgames.com/eawmodtool/foc_shaders.zip';

/**
 * SHA-256 of that archive, verified 2026-08-15 (190933 bytes, unchanged since 2023-11-14).
 *
 * Pinned so a future re-publish becomes a clean, actionable failure rather than the silent ingestion
 * of different bytes.
 */
export const SHADER_ARCHIVE_SHA256 =
    '88b9cb03322aab9be7451968ca514e9d2c810973d83f65459fd8cb52e7f96f8d';

/** Its size, checked before hashing so a truncated or redirected download fails on the cheap test. */
export const SHADER_ARCHIVE_BYTES = 190933;

/**
 * Why the extension will never ship these files itself.
 *
 * 103 of the 122 sources are headed "Petroglyph Confidential Source Code -- Do Not Distribute", and
 * this repository is public and MIT. Petroglyph publish them for modders at the URL above, which is
 * a licence to fetch from there and emphatically not one to re-host: the extension only ever points
 * at their download.
 */
export const SHADER_LICENCE_NOTE =
    'The base game shader sources are published by Petroglyph for mod tooling. '
    + 'EaWEdit never redistributes them - it only reads a copy you supply.';

/** The managed directory, or undefined when the user has not supplied one. */
export function shaderDirectory(): string | undefined {
    const configured = vscode.workspace.getConfiguration()
        .get<string>('aet-eaw-edit.shaders.directory')?.trim();

    return configured !== undefined && configured !== '' ? configured : undefined;
}

/**
 * Offers to help the user obtain the shader sources.
 *
 * Prompt-and-open, matching what the extension already does for the server binary. Never called on
 * activation: a modder who has not asked for a shader-accurate preview should not be nagged about
 * a download they may not want.
 */
export async function offerShaderSources(
    context?: vscode.ExtensionContext,
): Promise<void> {
    const configured = shaderDirectory();

    if (hasShaderSources(configured)) {
        void vscode.window.showInformationMessage(
            `EaWEdit: base shader sources found in "${configured!}".`);
        return;
    }

    // Download first when it is available: it is the answer most people want, and the manual route
    // stays for proxies, air-gapped machines and anyone who would rather see the bytes first.
    const choices = context === undefined
        ? ['Open download page', 'Choose folder']
        : ['Download now', 'Open download page', 'Choose folder'];

    const choice = await vscode.window.showInformationMessage(
        `EaWEdit: the preview can use the base game's shader sources for a closer match. `
        + `${SHADER_LICENCE_NOTE} They are fetched from Petroglyph's own published download and `
        + 'checked against a pinned SHA-256.',
        ...choices,
    );

    if (choice === 'Download now' && context !== undefined) {
        await downloadShaderSources(context);
        return;
    }

    if (choice === 'Open download page') {
        const url = vscode.workspace.getConfiguration()
            .get<string>('aet-eaw-edit.shaders.sourceUrl')?.trim();

        await vscode.env.openExternal(vscode.Uri.parse(
            url !== undefined && url !== '' ? url : SHADER_ARCHIVE_URL));
        return;
    }

    if (choice !== 'Choose folder') {
        return;
    }

    const picked = await vscode.window.showOpenDialog({
        canSelectFiles: false,
        canSelectFolders: true,
        canSelectMany: false,
        openLabel: 'Use these shaders',
    });

    if (picked === undefined || picked.length === 0) {
        return;
    }

    const directory = picked[0].fsPath;

    if (!hasShaderSources(directory)) {
        void vscode.window.showWarningMessage(
            `EaWEdit: no .fx files found in "${directory}". `
            + 'Pick the folder the archive extracted to.');
        return;
    }

    // Machine-scoped: where someone keeps a download is a property of their machine, not of a
    // workspace that might be shared or committed.
    await vscode.workspace.getConfiguration().update(
        'aet-eaw-edit.shaders.directory', directory, vscode.ConfigurationTarget.Global);

    void vscode.window.showInformationMessage(
        'EaWEdit: base shader sources set. Reload the window to pick them up.');
}

/** Where a fetched copy is kept: the extension's own storage, not the workspace. */
export function managedShaderDirectory(context: vscode.ExtensionContext): string {
    return path.join(context.globalStorageUri.fsPath, 'shaders');
}

/**
 * Fetches the published archive and unpacks the shader sources into extension storage.
 *
 * Reports progress and is cancellable, because this is a network fetch someone is waiting on.
 * Every failure path says what went wrong in words - an unreachable host, a proxy in the way and a
 * tampered file are three different problems with three different fixes, and "download failed"
 * serves none of them.
 */
export async function downloadShaderSources(
    context: vscode.ExtensionContext,
): Promise<boolean> {
    const configured = vscode.workspace.getConfiguration()
        .get<string>('aet-eaw-edit.shaders.sourceUrl')?.trim();
    const url = configured !== undefined && configured !== '' ? configured : SHADER_ARCHIVE_URL;

    return vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: 'EaWEdit: base shader sources',
        cancellable: true,
    }, async (progress, token) => {
        progress.report({ message: 'downloading...' });

        let bytes: Uint8Array;
        try {
            bytes = await fetchArchive(url, token);
        } catch (error) {
            if (token.isCancellationRequested) {
                return false;
            }

            // Offline, a proxy that needs configuring, DNS, TLS - all land here, and the message is
            // the only thing the reader has to go on.
            void vscode.window.showErrorMessage(
                `EaWEdit: could not download the shader sources from ${url}. `
                + `${error instanceof Error ? error.message : String(error)} `
                + 'If you are behind a proxy, set it in VS Code\'s proxy settings, or download the '
                + 'archive yourself and use "Choose folder".');
            return false;
        }

        progress.report({ message: 'verifying...' });

        const checked = checkArchive(bytes, SHADER_ARCHIVE_SHA256, SHADER_ARCHIVE_BYTES);
        if (!checked.ok) {
            void vscode.window.showErrorMessage(
                `EaWEdit: the shader archive was not what was expected - ${checked.problem}. `
                + 'Nothing has been written.');
            return false;
        }

        progress.report({ message: 'extracting...' });

        const directory = managedShaderDirectory(context);
        let written: number;
        try {
            written = await extractShaders(bytes, directory);
        } catch (error) {
            void vscode.window.showErrorMessage(
                `EaWEdit: could not unpack the shader archive. `
                + `${error instanceof Error ? error.message : String(error)}`);
            return false;
        }

        const extracted = extractionVerdict(written);
        if (!extracted.ok) {
            void vscode.window.showErrorMessage(
                `EaWEdit: ${extracted.problem}, so nothing was configured.`);
            return false;
        }

        // Machine-scoped, and written out rather than left implicit: the reader can see where the
        // files went, point somewhere else, or clear it.
        await vscode.workspace.getConfiguration().update(
            'aet-eaw-edit.shaders.directory', directory, vscode.ConfigurationTarget.Global);

        void vscode.window.showInformationMessage(
            `EaWEdit: ${written} shader sources ready. ${SHADER_LICENCE_NOTE}`);

        return true;
    });
}

/** Downloads the archive, honouring a cancel. */
async function fetchArchive(
    url: string, token: vscode.CancellationToken,
): Promise<Uint8Array> {
    const controller = new AbortController();
    const cancel = token.onCancellationRequested(() => controller.abort());

    try {
        const response = await fetch(url, { signal: controller.signal, redirect: 'follow' });

        if (!response.ok) {
            throw new Error(`The server answered ${response.status} ${response.statusText}.`);
        }

        return new Uint8Array(await response.arrayBuffer());
    } finally {
        cancel.dispose();
    }
}

/**
 * Unpacks the shader sources, and only those.
 *
 * `entryVerdict` decides what may be written; this loop does no path arithmetic of its own, so the
 * zip-slip rule has exactly one home and one set of tests.
 */
async function extractShaders(archive: Uint8Array, directory: string): Promise<number> {
    await fs.promises.mkdir(directory, { recursive: true });

    const open = promisify<Buffer, yauzl.Options, yauzl.ZipFile>(yauzl.fromBuffer);
    const zip = await open(Buffer.from(archive), { lazyEntries: true });

    return new Promise<number>((resolve, reject) => {
        let written = 0;

        zip.on('error', reject);
        zip.on('end', () => resolve(written));

        zip.on('entry', (entry: yauzl.Entry) => {
            const verdict = entryVerdict(entry.fileName, directory);
            if (!verdict.keep) {
                zip.readEntry();
                return;
            }

            zip.openReadStream(entry, (error, stream) => {
                if (error !== null && error !== undefined) {
                    reject(error);
                    return;
                }

                const out = fs.createWriteStream(path.join(directory, verdict.relativePath));
                stream.pipe(out);

                out.on('error', reject);
                out.on('close', () => {
                    written++;
                    zip.readEntry();
                });
            });
        });

        zip.readEntry();
    });
}
