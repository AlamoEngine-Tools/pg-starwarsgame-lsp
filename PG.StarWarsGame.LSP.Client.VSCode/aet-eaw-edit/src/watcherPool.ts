// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Watchers over directories outside the workspace folders, shared between servers (#229).
//
// Each project's server reports where its dependencies live, and a dependency referenced by two
// open projects is reported by both. One OS watcher per directory and glob serves every server that
// asked for it, and is disposed when the last of them lets go. Kept free of `vscode`: the extension
// supplies the factory that creates the real watcher.

/** One on-disk change; `type` is the LSP FileChangeType (1 created, 2 changed, 3 deleted). */
export interface FileEvent {
    path: string;
    type: 1 | 2 | 3;
}

export interface Disposable {
    dispose(): void;
}

/** Creates one watcher over `glob` under `directory` that calls `emit` for every change. */
export type WatcherFactory = (
    directory: string,
    glob: string,
    emit: (event: FileEvent) => void,
) => Disposable;

interface Shared {
    watcher: Disposable;
    listeners: Set<(event: FileEvent) => void>;
}

export class WatcherPool {
    private readonly _shared = new Map<string, Shared>();

    constructor(private readonly _create: WatcherFactory) {}

    /** Watches `globs` under `directory` for one server; disposing the result lets go. */
    acquire(
        directory: string,
        globs: readonly string[],
        listener: (event: FileEvent) => void,
    ): Disposable {
        const keys = globs.map((glob) => this.add(directory, glob, listener));
        return {
            dispose: () => {
                for (const key of keys) {
                    this.release(key, listener);
                }
            },
        };
    }

    private add(
        directory: string,
        glob: string,
        listener: (event: FileEvent) => void,
    ): string {
        const key = `${directory.replace(/\\/g, "/").toLowerCase()}|${glob}`;
        let shared = this._shared.get(key);
        if (shared === undefined) {
            const listeners = new Set<(event: FileEvent) => void>();
            const watcher = this._create(directory, glob, (event) => {
                for (const notify of [...listeners]) {
                    notify(event);
                }
            });
            shared = { watcher, listeners };
            this._shared.set(key, shared);
        }
        shared.listeners.add(listener);
        return key;
    }

    private release(key: string, listener: (event: FileEvent) => void): void {
        const shared = this._shared.get(key);
        if (shared === undefined) {
            return;
        }
        shared.listeners.delete(listener);
        if (shared.listeners.size === 0) {
            shared.watcher.dispose();
            this._shared.delete(key);
        }
    }
}
