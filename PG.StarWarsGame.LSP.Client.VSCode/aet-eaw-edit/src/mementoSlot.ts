// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// One value the extension remembers between sessions (#324).
//
// Every stored value follows the same three rules, and they used to be written out once per value:
// what is read back is never trusted, what is written is normalised first, and a failed write is
// not worth an error. A slot is those rules around one key.
//
// Free of `vscode`, so it runs under the node test harness: the scope is the two members of
// `vscode.Memento` a slot uses.

/**
 * Where a slot keeps its value: `globalState` for what follows the person, `workspaceState` for
 * what follows the mod.
 */
export interface MementoScope {
    get<T>(key: string): T | undefined;
    update(key: string, value: unknown): PromiseLike<void>;
}

export interface MementoSlot<T> {
    /**
     * What was stored, normalised. Never throws and never hands back junk: the stored value
     * outlives the build that wrote it.
     */
    read(): T;
    /**
     * Records a value, normalised before it is written, so nothing that failed validation on the
     * way in is handed back out next session. A failed write is swallowed - a preference that
     * forgets itself is a small annoyance, and an unhandled rejection is a worse one.
     */
    save(value: unknown): void;
}

/**
 * @param key The key the value is stored under in its scope.
 * @param normalise Turns whatever was stored, or whatever is about to be, into a usable value.
 * @param scope The memento that holds it.
 */
export function mementoSlot<T>(
    key: string,
    normalise: (stored: unknown) => T,
    scope: MementoScope,
): MementoSlot<T> {
    return {
        read: () => normalise(scope.get<unknown>(key)),
        save: (value) => {
            void Promise.resolve(scope.update(key, normalise(value))).then(
                undefined,
                () => {
                    /* a stored preference is not worth reporting a failure over */
                },
            );
        },
    };
}
