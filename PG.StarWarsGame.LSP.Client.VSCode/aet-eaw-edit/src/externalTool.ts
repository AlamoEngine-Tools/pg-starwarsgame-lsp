// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Handing a previewed file to an external editor. Kept free of `vscode` so it runs under the unit
// test harness; the panel supplies the settings and reports the outcome.

import {spawn} from 'node:child_process';

import {type PreviewSourceFile} from './protocol/modelPreview';

/** The slice of `child_process.spawn` the launcher needs, so a test can stand in for it. */
export type SpawnFn = (
    command: string,
    args: string[],
    options: { detached: boolean; stdio: 'ignore'; shell?: boolean },
) => NodeJS.EventEmitter & { unref(): void };

/**
 * The file to hand over: the one the preview was opened from, else the one the server resolved
 * the name to, else none.
 *
 * The opened file wins because the server resolves by NAME. With a mod shadowing the base game it
 * would hand over the mod's copy even when the author had opened the base game's.
 */
export function handoffFile(openedFile: string | null, source: PreviewSourceFile | null | undefined): string | null {
    return openedFile ?? source?.path ?? null;
}

/**
 * Starts `executable` on `file` as a process of its own, and settles once it has started or failed
 * to.
 *
 * Spawned directly rather than through a shell or a terminal. The earlier launch typed a PowerShell
 * call into the default terminal, which in cmd or Git Bash is not a command at all - the tool never
 * started and nothing said so. Detached and unreferenced, so closing VS Code leaves the editor open.
 */
export function launchDetached(executable: string, file: string, spawnFn: SpawnFn = spawn as unknown as SpawnFn): Promise<void> {
    return new Promise((resolve, reject) => {
        const child = spawnFn(executable, [file], {detached: true, stdio: 'ignore'});
        child.once('spawn', () => {
            child.unref();
            resolve();
        });
        child.once('error', reject);
    });
}
