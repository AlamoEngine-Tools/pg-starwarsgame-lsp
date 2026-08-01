// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The half of a localisation editor that is the same whatever the file holds: talking to the panel,
// holding the staged queue, and knowing whether the last Validate still describes what is staged.
//
// Generic over the command type, because that is precisely what differs between the two editors -
// translations address entries by key, credits address rows by position - and nothing else here
// cares which.

import { useCallback, useEffect, useState } from 'react';

import { LocRow } from '../loc/locRow';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

// Acquired once for the whole webview: calling it twice throws, so this module owns it and
// everything else posts through `post`.
const vscode = acquireVsCodeApi();

/** Sends a message to the panel hosting this webview. */
export function post(message: unknown): void {
    vscode.postMessage(message);
}

export interface LocProblem {
    index?: number | null;
    key?: string | null;
    language?: string | null;
    severity: string;
    message: string;
}

export type ValidationState = 'unvalidated' | 'ok' | 'error';

export interface LocPanelMessage { type: string; [key: string]: unknown }

export interface LocPanel<C> {
    rows: LocRow[];
    setRows: React.Dispatch<React.SetStateAction<LocRow[]>>;
    languages: string[];
    ordered: boolean;
    category: string;
    error: string | null;
    loaded: boolean;

    queue: C[];
    problems: LocProblem[];
    validation: ValidationState;

    /** Applies a command locally and adds it to the queue. Nothing reaches disk before Save. */
    stage: (command: C) => void;
    save: () => void;
    validate: () => void;
    post: (message: unknown) => void;
}

export function useLocPanel<C>(options: {
    /** Materialises a staged command locally, so an edit shows the moment it is made. */
    applyStaged: (rows: LocRow[], command: C) => LocRow[];
    /** Folds a queue down before it is sent. */
    coalesce: (queue: C[]) => C[];
    /** Called when a different file arrives, so the editor can drop its own per-file state. */
    onFileLoaded?: () => void;
    /** Messages this editor understands and the shared protocol does not. */
    onMessage?: (message: LocPanelMessage) => void;
}): LocPanel<C> {
    const [rows, setRows] = useState<LocRow[]>([]);
    const [languages, setLanguages] = useState<string[]>([]);
    const [ordered, setOrdered] = useState(false);
    const [category, setCategory] = useState('text');
    const [error, setError] = useState<string | null>(null);
    const [loaded, setLoaded] = useState(false);

    const [queue, setQueue] = useState<C[]>([]);
    const [problems, setProblems] = useState<LocProblem[]>([]);
    const [validation, setValidation] = useState<ValidationState>('unvalidated');

    // Held in refs so the message listener can stay mounted for the life of the webview. Re-binding
    // it on every render would drop messages that arrive mid-update.
    const { onFileLoaded, onMessage } = options;

    useEffect(() => {
        const handle = (event: MessageEvent): void => {
            const msg = event.data as LocPanelMessage;

            switch (msg.type) {
                case 'rows':
                    setRows(msg.rows as LocRow[]);
                    setLanguages(msg.languages as string[]);
                    setOrdered(msg.ordered as boolean);
                    setCategory(msg.category as string);
                    setQueue([]);
                    setProblems([]);
                    setValidation('unvalidated');
                    setError(null);
                    setLoaded(true);
                    onFileLoaded?.();
                    // Validate what just arrived, with nothing staged. A file can already have
                    // problems - a duplicate key it was saved with - and they are worth knowing
                    // about before an edit is made rather than only after one. This also means the
                    // indicator says something true from the moment the file opens, instead of
                    // sitting inert until it is clicked.
                    vscode.postMessage({ type: 'validateBatch', commands: [] });
                    break;

                case 'problems':
                    setProblems((msg.problems as LocProblem[]) ?? []);
                    setValidation(
                        ((msg.problems as LocProblem[]) ?? []).some(p => p.severity === 'error')
                            ? 'error' : 'ok');
                    break;

                case 'saveResult':
                    if (msg.success) {
                        setQueue([]);
                        setProblems([]);
                        setValidation('unvalidated');
                        vscode.postMessage({ type: 'fetch' });
                    }
                    break;

                case 'invalidate':
                    // Only re-read when nothing is staged: reloading over unsaved edits would
                    // discard work still on screen. A dirty tab keeps what it has until Save.
                    if (!msg.dirty) { vscode.postMessage({ type: 'fetch' }); }
                    break;

                case 'error':
                    setError(msg.message as string);
                    setLoaded(true);
                    break;

                default:
                    onMessage?.(msg);
                    break;
            }
        };

        window.addEventListener('message', handle);
        vscode.postMessage({ type: 'ready' });
        return () => window.removeEventListener('message', handle);
    }, [onFileLoaded, onMessage]);

    // The panel mirrors the queue so it can offer to save if the tab is closed while dirty.
    useEffect(() => {
        vscode.postMessage({ type: 'pendingSync', commands: queue });
    }, [queue]);

    const { applyStaged, coalesce } = options;

    const stage = useCallback((command: C) => {
        setRows(current => applyStaged(current, command));
        setQueue(current => [...current, command]);
        // Any edit invalidates the last Validate result - it described a different document.
        setValidation('unvalidated');
        setProblems([]);
    }, [applyStaged]);

    const save = useCallback(() => {
        vscode.postMessage({ type: 'saveBatch', commands: coalesce(queue) });
    }, [queue, coalesce]);

    const validate = useCallback(() => {
        vscode.postMessage({ type: 'validateBatch', commands: coalesce(queue) });
    }, [queue, coalesce]);

    return {
        rows, setRows, languages, ordered, category, error, loaded,
        queue, problems, validation,
        stage, save, validate,
        post: message => vscode.postMessage(message),
    };
}
