// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The half of a localisation editor that is the same whatever the file holds: talking to the panel,
// holding the staged queue, and knowing whether the last Validate still describes what is staged.
//
// Generic over the command type, because that is precisely what differs between the two editors -
// translations address entries by key, credits address rows by position - and nothing else here
// cares which.

import { useCallback, useEffect, useState } from 'react';

import { LocProblemDto } from '../../protocol';
import { loadDialogGeometry } from '../shared/dialogGeometryStore';
import { StoredGeometry } from '../shared/modalGeometry';
import { LocRow } from '../loc/locRow';
import { useDebounced, VALIDATE_DEBOUNCE_MS } from './useDebounced';
import { worstSeverity } from './validateState';

import { initPanelLayout } from '../shared/panelLayoutBridge';

declare function acquireVsCodeApi(): { postMessage(message: unknown): void };

// Acquired once for the whole webview: calling it twice throws, so this module owns it and
// everything else posts through `post`.
const vscode = acquireVsCodeApi();

// Reads the dock and drawer sizes the host seeded into the page, and reports
// every drag back to it. Must run before anything measures itself.
initPanelLayout(vscode);

/** Sends a message to the panel hosting this webview. */
export function post(message: unknown): void {
    vscode.postMessage(message);
}

/**
 * A validation finding, in the editors' own vocabulary.
 *
 * The union of what the two validators report - see LocProblemDto in ../../protocol for why every
 * locator on it is optional. Aliased rather than re-declared so the ~15 call sites in this folder
 * keep reading in grid terms.
 */
export type LocProblem = LocProblemDto;

export type ValidationState = 'unvalidated' | 'ok' | 'info' | 'warning' | 'error';

export interface LocPanelMessage { type: string; [key: string]: unknown }

export interface LocPanel<C> {
    rows: LocRow[];
    setRows: React.Dispatch<React.SetStateAction<LocRow[]>>;
    languages: string[];
    /**
     * Adds a language column locally, alongside staging the command.
     *
     * The staged rows gain their empty cell from applyStaged, but the column list comes from the
     * server and would not catch up until the next read - so the column the user just asked for
     * would not appear until after Save, which looks exactly like the button doing nothing.
     */
    setLanguages: React.Dispatch<React.SetStateAction<string[]>>;
    ordered: boolean;
    category: string;
    /** Whether this file's format can hold more than one language. The server decides; see
     *  LocalisationDocumentEditor.SupportsMultipleLanguages. */
    canAddLanguage: boolean;
    /** Shown alone at first, when the tab was opened on one language of a set. */
    focusLanguage: string | null;
    /** Adding a language here means creating a sibling file, not a column. */
    addLanguageCreatesFile: boolean;
    /** The languages the engine officially supports - the only ones worth adding. */
    supportedLanguages: string[];
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
    /**
     * The tab was re-aimed at a different language of its set. The editor uses this to drop any
     * columns the user had hidden by hand, so the new focus decides what is shown rather than a
     * choice made about a different view.
     */
    onFocusLanguageChanged?: () => void;
    /** Messages this editor understands and the shared protocol does not. */
    onMessage?: (message: LocPanelMessage) => void;
}): LocPanel<C> {
    const [rows, setRows] = useState<LocRow[]>([]);
    const [languages, setLanguages] = useState<string[]>([]);
    const [ordered, setOrdered] = useState(false);
    const [category, setCategory] = useState('text');
    const [canAddLanguage, setCanAddLanguage] = useState(false);
    const [focusLanguage, setFocusLanguage] = useState<string | null>(null);
    const [addLanguageCreatesFile, setAddLanguageCreatesFile] = useState(false);
    const [supportedLanguages, setSupportedLanguages] = useState<string[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [loaded, setLoaded] = useState(false);

    const [queue, setQueue] = useState<C[]>([]);
    const [problems, setProblems] = useState<LocProblem[]>([]);
    const [validation, setValidation] = useState<ValidationState>('unvalidated');

    // Held in refs so the message listener can stay mounted for the life of the webview. Re-binding
    // it on every render would drop messages that arrive mid-update.
    const { onFileLoaded, onFocusLanguageChanged, onMessage } = options;

    useEffect(() => {
        const handle = (event: MessageEvent): void => {
            const msg = event.data as LocPanelMessage;

            switch (msg.type) {
                case 'rows':
                    setRows(msg.rows as LocRow[]);
                    setLanguages(msg.languages as string[]);
                    setOrdered(msg.ordered as boolean);
                    setCategory(msg.category as string);
                    setCanAddLanguage(msg.canAddLanguage === true);
                    setFocusLanguage((msg.focusLanguage as string | null) ?? null);
                    setAddLanguageCreatesFile(msg.addLanguageCreatesFile === true);
                    setSupportedLanguages((msg.supportedLanguages as string[]) ?? []);
                    // Seeded here rather than on a message of its own: it arrives with the file,
                    // before any dialog can be opened, which is the only ordering that matters.
                    loadDialogGeometry(
                        msg.dialogGeometry as Record<string, StoredGeometry> | undefined,
                        (id, geometry) =>
                            vscode.postMessage({ type: 'saveDialogGeometry', id, geometry }));
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
                    // The tag reads out the highest level reported - see worstSeverity, which is
                    // the rule the story graph editor's Validate button uses too.
                    setValidation(worstSeverity((msg.problems as LocProblem[]) ?? []));
                    break;

                case 'saveResult':
                    if (msg.success) {
                        setQueue([]);
                        setProblems([]);
                        setValidation('unvalidated');
                        vscode.postMessage({ type: 'fetch' });
                    }
                    break;

                // The tab was re-aimed at one language of its set, or back at all of them.
                case 'focusLanguage':
                    setFocusLanguage((msg.language as string | null) ?? null);
                    onFocusLanguageChanged?.();
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
    }, [onFileLoaded, onFocusLanguageChanged, onMessage]);

    // The panel mirrors the queue so it can offer to save if the tab is closed while dirty.
    useEffect(() => {
        vscode.postMessage({ type: 'pendingSync', commands: queue });
    }, [queue]);

    const { applyStaged, coalesce } = options;

    /**
     * Re-checks the file as staged edits settle.
     *
     * A batch is applied all or nothing, so a single bad change refuses the save and takes every
     * other change with it. Checking only on demand meant that was discovered at Save - by which
     * point the offending edit could be two hundred edits back, with no indication which one it
     * was. Now the problem appears against the row that caused it, while it is still the thing on
     * screen.
     */
    const settledQueue = useDebounced(queue, VALIDATE_DEBOUNCE_MS);
    useEffect(() => {
        if (settledQueue.length === 0) { return; }
        vscode.postMessage({ type: 'validateBatch', commands: coalesce(settledQueue) });
    }, [settledQueue, coalesce]);

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
        rows, setRows, languages, setLanguages, ordered, category, canAddLanguage, focusLanguage,
        addLanguageCreatesFile,
        supportedLanguages, error, loaded,
        queue, problems, validation,
        stage, save, validate,
        post: message => vscode.postMessage(message),
    };
}
