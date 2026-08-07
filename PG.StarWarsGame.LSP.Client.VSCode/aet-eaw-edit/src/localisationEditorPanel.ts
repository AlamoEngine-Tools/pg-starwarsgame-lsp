// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { basename } from 'path';
import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

import { CreditsPreviewPanel } from './creditsPreviewPanel';
import { readDialogGeometry, saveDialogGeometry } from './dialogGeometryStorage';
import { StoredGeometry } from './webview/shared/modalGeometry';
import { LocalisationPanelState } from './webview/localisationPanelState';
import { KeyedCommand, mergeMembers, partitionCommands } from './webview/loc/locSetMerge';

interface LocValueDto { language: string; value: string; }
interface LocRowDto { index: number; key: string; values: LocValueDto[]; }

interface GetLanguagesResult { languages: string[] }

interface GetLocalisationRowsResult {
    rows: LocRowDto[];
    languages: string[];
    contentHash: string;
    category: string;
    ordered: boolean;
    error?: string | null;
    canAddLanguage: boolean;
    addLanguageCreatesFile: boolean;
}

interface ApplyLocalisationBatchResult {
    success: boolean;
    failedIndex?: number | null;
    error?: string | null;
    newContentHash?: string | null;
}

interface LocProblemDto {
    index?: number | null;
    language?: string | null;
    severity: string;
    message: string;
}

interface ValidateLocalisationBatchResult { problems: LocProblemDto[]; error?: string | null; }

interface GetBaselineEntriesResult {
    entries: { key: string; translations: Record<string, string> }[];
}

/**
 * The localisation grid, hosted in a real editor tab.
 *
 * One panel per file, keyed by lower-cased path: two tabs on one file would each hold their own
 * staged queue and their own content hash, so whichever saved second would be refused as stale
 * having silently lost the other's work.
 *
 * Modelled on {@link StoryGraphPanel}, including its close-time save prompt - a disposed webview
 * cannot veto its own close, so the queue is mirrored here as it changes.
 */
/**
 * One file of an open set, and what is known about it.
 *
 * A single file is a set of one, so every path below is the same path - there is no separate
 * "one file" mode to keep in step with the set one.
 */
interface Member {
    filePath: string;
    languages: string[];
    contentHash?: string;
}

export class LocalisationEditorPanel {
    private static readonly _panels = new Map<string, LocalisationEditorPanel>();

    /** The tab a title-bar command applies to. */
    private static _active: LocalisationEditorPanel | null = null;

    private readonly _panel: vscode.WebviewPanel;
    private readonly _state = new LocalisationPanelState();

    /** Every file the table is built from, in column order. */
    private _members: Member[] = [];

    /**
     * The file a file-level action applies to: convert, export to DAT, open as text.
     *
     * The first of the set. Those actions are about one file on disk, and for a set the one the
     * tree was opened on is the only defensible choice.
     */
    private get _filePath(): string {
        return this._filePaths[0];
    }

    private constructor(
        private readonly _filePaths: string[],
        /** Which language's column to show alone at first, when opened on one file of a set. */
        /**
         * Which language's column to show alone, when opened on one file of a set.
         *
         * Not readonly: the set and each of its languages are one tab, so clicking a different one
         * of them has to re-aim the tab that is already open rather than appear to do nothing.
         */
        private _focusLanguage: string | undefined,
        private readonly _label: string,
        private readonly _category: string,
        private readonly _extensionUri: vscode.Uri,
        private readonly _getLspClient: () => LanguageClient | undefined
    ) {
        const label = _label;
        const extensionUri = _extensionUri;
        // Two view types, so a menu contribution can target one kind of file. The credits editor
        // has a crawl to preview; the translation editor has nothing of the sort.
        this._panel = vscode.window.createWebviewPanel(
            isCredits(this._category) ? 'aetCreditsEditor' : 'aetTranslationEditor',
            `Loc: ${label}`, vscode.ViewColumn.Active,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                // The grid has its own filter box, but Ctrl+F in a table is muscle memory.
                enableFindWidget: true,
                localResourceRoots: [
                    vscode.Uri.joinPath(extensionUri, 'out', 'webview'),
                    vscode.Uri.joinPath(extensionUri, 'out', 'codicons'),
                ],
            });

        this._panel.onDidDispose(() => {
            LocalisationEditorPanel._panels.delete(setKey(_filePaths));
            if (LocalisationEditorPanel._active === this) { LocalisationEditorPanel._active = null; }
            void this._promptSaveOnClose();
        });

        // Tracked so a command contributed to the editor title bar knows which tab it belongs to -
        // a menu command carries no argument identifying the panel it was clicked on.
        this._panel.onDidChangeViewState(e => {
            if (e.webviewPanel.active) { LocalisationEditorPanel._active = this; }
        });
        LocalisationEditorPanel._active = this;

        // Two editors, not one that decides what it is looking at: a credits file is an ordered
        // list addressed by position, a translation file a lookup table addressed by key, and the
        // single grid that tried to be both put credits behaviour into text files repeatedly.
        const script = isCredits(this._category) ? 'creditsEditor.js' : 'translationEditor.js';
        const scriptUri = this._panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'webview', script));
        const codiconUri = this._panel.webview.asWebviewUri(
            vscode.Uri.joinPath(extensionUri, 'out', 'codicons', 'codicon.css'));
        this._panel.webview.html = buildHtml(scriptUri, codiconUri, this._panel.webview.cspSource);

        this._panel.webview.onDidReceiveMessage(
            async (msg: { type: string; [key: string]: unknown }) => {
                switch (msg.type) {
                    case 'ready':
                    case 'fetch':
                        await this._sendRows();
                        break;
                    case 'pendingSync':
                        this._state.syncPending(msg.commands as Record<string, unknown>[]);
                        break;
                    case 'validateBatch':
                        await this._validate(msg.commands as Record<string, unknown>[]);
                        break;
                    case 'saveBatch':
                        await this._save(msg.commands as Record<string, unknown>[]);
                        break;
                    case 'requestBaseline':
                        await this._sendBaseline();
                        break;
                    case 'crawlRows':
                        this._sendCrawlRows(msg);
                        break;
                    case 'convertFormat':
                        await this._convertFormat(msg.targetFormat as string);
                        break;
                    case 'addLanguageFile':
                        await this._addLanguageFile(msg.language as string);
                        break;
                    case 'saveDialogGeometry':
                        saveDialogGeometry(msg.id as string, msg.geometry as StoredGeometry);
                        break;
                    case 'exportDat':
                        await vscode.commands.executeCommand(
                            'aet-eaw-edit.lsp.exportLocalisationToDat',
                            { project: { filePath: this._filePath } });
                        break;
                    default:
                        break;
                }
            });
    }

    static show(
        filePath: string, label: string, category: string, extensionUri: vscode.Uri,
        getLspClient: () => LanguageClient | undefined
    ): void {
        LocalisationEditorPanel.showSet([filePath], label, category, extensionUri, getLspClient);
    }

    /**
     * Opens a set of single-language files as one table.
     *
     * Keyed on the whole set, so opening the set and then one of its files reveals the same tab
     * rather than a second one holding a competing staged queue for the same files.
     *
     * @param focusLanguage Shown alone at first - what opening one language of a set means. The
     *     other columns are hidden, not absent, so they are one click away in the column menu.
     */
    static showSet(
        filePaths: string[], label: string, category: string, extensionUri: vscode.Uri,
        getLspClient: () => LanguageClient | undefined, focusLanguage?: string
    ): void {
        const id = setKey(filePaths);
        const existing = LocalisationEditorPanel._panels.get(id);
        if (existing) {
            // The set and every language in it map to one tab - deliberately, so they cannot stage
            // competing edits against the same files. Revealing alone is therefore not enough:
            // clicking a language while the set is open has to change which columns are shown, or
            // the click does nothing at all.
            existing._focusOn(focusLanguage);
            existing._panel.reveal();
            return;
        }

        LocalisationEditorPanel._panels.set(
            id,
            new LocalisationEditorPanel(
                filePaths, focusLanguage, label, category, extensionUri, getLspClient));
    }

    /**
     * Re-aims an open tab at one language of its set, or back at all of them.
     *
     * A message rather than a re-read: the files have not changed, and re-reading would throw away
     * the staged queue along with the user's sort and selection just to hide some columns.
     */
    private _focusOn(language: string | undefined): void {
        this._focusLanguage = language;
        this._post({ type: 'focusLanguage', language: language ?? null });
    }

    /**
     * Tells every open tab its file may have changed underneath it. All of them, because
     * `aet/localisationIndexUpdated` carries no payload - it cannot say which file moved.
     *
     * The tab decides what to do: one with nothing staged re-reads, one with staged edits does not,
     * since silently reloading would discard work still visible on screen.
     */
    static invalidateAll(): void {
        for (const panel of LocalisationEditorPanel._panels.values()) {
            panel._post({ type: 'invalidate', dirty: panel._state.isDirty });
        }
    }

    /**
     * Shows the credits crawl for the active tab.
     *
     * `fullScreen` plays it over the editor itself and asks the host to fill the screen; otherwise
     * it opens in a panel beside, the way a Markdown or LaTeX preview does. Either way the editor
     * is asked for its staged rows first - the preview is of what you are about to save.
     */
    static previewCrawl(fullScreen: boolean): void {
        LocalisationEditorPanel._active?._post({ type: 'requestCrawlRows', fullScreen });
    }

    /** Forwards the editor's staged rows to the preview beside it, if one is open. */
    private _sendCrawlRows(msg: Record<string, unknown>): void {
        const payload = {
            type: 'crawl',
            rows: msg.rows,
            languages: msg.languages,
            language: msg.language,
        };

        if (msg.open === true) {
            CreditsPreviewPanel.show(this._filePath, this._label, this._extensionUri, payload);
        } else {
            CreditsPreviewPanel.update(this._filePath, payload);
        }
    }

    static disposeAll(): void {
        for (const panel of [...LocalisationEditorPanel._panels.values()]) { panel._panel.dispose(); }
    }

    /**
     * The languages the engine officially supports, cached for the life of the tab.
     *
     * A property of the game rather than of the file, so it is fetched once. The editor offers only
     * these when adding a language - a made-up identifier produces a column the game never reads.
     */
    private _supportedLanguages: string[] | undefined;

    private async _supportedLanguagesAsync(): Promise<string[]> {
        if (this._supportedLanguages !== undefined) { return this._supportedLanguages; }

        const client = this._getLspClient();
        if (!client) { return []; }

        try {
            const result = await client.sendRequest<GetLanguagesResult>('aet/getLanguages', {});
            this._supportedLanguages = result.languages ?? [];
        } catch {
            // Not fatal: the editor is fully usable, it just cannot offer to add a language.
            this._supportedLanguages = [];
        }

        return this._supportedLanguages;
    }

    private async _sendRows(): Promise<void> {
        const client = this._getLspClient();
        if (!client) { this._post({ type: 'error', message: 'LSP server is not running.' }); return; }

        // Read one file at a time rather than in parallel: the server serialises these anyway, and
        // a failure part-way through is far easier to report when the order is known.
        const results: GetLocalisationRowsResult[] = [];
        for (const filePath of this._filePaths) {
            const result = await client.sendRequest<GetLocalisationRowsResult>(
                'aet/getLocalisationRows', { projectFilePath: filePath });

            if (result.error) { this._post({ type: 'error', message: result.error }); return; }
            results.push(result);
        }

        // An unchanged set is not re-delivered. A save reloads the server's localisation index and
        // the watcher then reports that same write, so one save announced the index had moved twice
        // over - and each announcement made every open tab hand its webview every row again, reset
        // its selection, sort and inherited toggle, and re-fetch the baseline. See shouldDeliver.
        // For a set the hashes are combined, so any one file moving counts as the set moving.
        const combinedHash = results.map(r => r.contentHash).join('|');
        if (!this._state.shouldDeliver(combinedHash)) { return; }

        this._members = this._filePaths.map((filePath, i) => ({
            filePath,
            languages: results[i].languages,
            contentHash: results[i].contentHash,
        }));

        const merged = mergeMembers(this._members.map((m, i) => ({
            filePath: m.filePath, languages: m.languages, rows: results[i].rows,
        })));

        this._state.noteRead(combinedHash);
        this._post({
            supportedLanguages: await this._supportedLanguagesAsync(),
            // Sent with the file so a dialog knows where it belongs on its first render - asking
            // for it when one opens would show it centred and then move it.
            dialogGeometry: readDialogGeometry(),
            type: 'rows',
            rows: merged.rows,
            languages: merged.languages,
            category: results[0].category,
            ordered: results[0].ordered,
            // A set is several single-language files, so a language is a file, not a column - the
            // grid offers "add language" as a new sibling either way.
            canAddLanguage: this._members.length === 1 && results[0].canAddLanguage,
            addLanguageCreatesFile: results[0].addLanguageCreatesFile,
            focusLanguage: this._focusLanguage ?? null,
        });
    }

    /**
     * Rewrites this file in another format, keeping the original.
     *
     * Delegated to the command rather than requested here, so the palette entry and this button are
     * the same code path - including how the outcome is reported.
     */
    private async _convertFormat(targetFormat: string): Promise<void> {
        await vscode.commands.executeCommand(
            'aet-eaw-edit.lsp.convertLocalisationFormat',
            { filePath: this._filePath, targetFormat, category: this._category });
    }

    /**
     * Adds a language to a single-language project by creating the sibling file that holds it.
     *
     * Not part of the staged batch: the batch composes new text for THIS file, and this writes a
     * different one. It lands on disk immediately, like every other localisation write, so the
     * result is offered for opening rather than left for the user to find in the tree.
     */
    private async _addLanguageFile(language: string): Promise<void> {
        const client = this._getLspClient();
        if (!client) { vscode.window.showWarningMessage('EaWEdit LSP: server is not running.'); return; }

        const result = await client.sendRequest<{ writtenPath?: string | null; error?: string | null }>(
            'aet/createLocalisationLanguageFile',
            { projectFilePath: this._filePath, language });

        if (result.error || !result.writtenPath) {
            vscode.window.showErrorMessage(`EaWEdit: ${result.error ?? 'could not create the file.'}`);
            return;
        }

        const writtenPath = result.writtenPath;
        const choice = await vscode.window.showInformationMessage(
            `EaWEdit: created ${writtenPath} with every key from this file, ready to translate.`,
            'Open');

        if (choice === 'Open') {
            LocalisationEditorPanel.show(
                writtenPath, basename(writtenPath), this._category, this._extensionUri,
                this._getLspClient);
        }
    }

    /**
     * Fetches what the layers below this file already say, so the grid can hide the rows that only
     * repeat it.
     *
     * A failure is answered with an empty baseline rather than an error: the grid is fully usable
     * without one, and the only consequence is that nothing is hidden. Turning that into a modal
     * would interrupt editing over a convenience.
     */
    private async _sendBaseline(): Promise<void> {
        const client = this._getLspClient();
        if (!client) { this._post({ type: 'baselineRows', entries: [] }); return; }

        try {
            const result = await client.sendRequest<GetBaselineEntriesResult>(
                'aet/getBaselineEntries', { projectFilePath: this._filePath });
            this._post({ type: 'baselineRows', entries: result.entries ?? [] });
        } catch {
            this._post({ type: 'baselineRows', entries: [] });
        }
    }

    private async _validate(commands: Record<string, unknown>[]): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }

        const result = await client.sendRequest<ValidateLocalisationBatchResult>(
            isCredits(this._category)
                ? 'aet/validateCreditsBatch'
                : 'aet/validateTranslationBatch',
            { projectFilePath: this._filePath, commands });

        this._post({ type: 'problems', problems: result.problems ?? [], error: result.error ?? null });
    }

    /**
     * Writes the staged batch.
     *
     * A set is written one file at a time, each batch atomic on its own. Not atomic across the set:
     * that would need the server to check every hash before writing any, and the failure this
     * guards against - one file having moved on disk - is per file anyway. A refused language is
     * named, and the whole queue stays staged so nothing is silently half-applied from the user's
     * point of view.
     */
    private async _save(commands: Record<string, unknown>[]): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }

        const method = isCredits(this._category) ? 'aet/applyCreditsBatch' : 'aet/applyTranslationBatch';

        // A set that has not been read yet has no members; fall back to the file it was opened on.
        const members = this._members.length > 0
            ? this._members
            : [{ filePath: this._filePath, languages: [], contentHash: this._state.contentHash }];

        const batches = members.length === 1
            ? new Map([[members[0].filePath, commands as unknown as KeyedCommand[]]])
            : partitionCommands(commands as unknown as KeyedCommand[], members.map(m => ({
                filePath: m.filePath, languages: m.languages, rows: [],
            })));

        const failures: { member: Member; result: ApplyLocalisationBatchResult }[] = [];
        let firstFailedIndex: number | null = null;

        for (const member of members) {
            const batch = batches.get(member.filePath);
            // Untouched by this batch: writing it anyway would spend its hash and make the watcher
            // announce a change to a file nobody edited.
            if (batch === undefined || batch.length === 0) { continue; }

            const result = await client.sendRequest<ApplyLocalisationBatchResult>(method, {
                projectFilePath: member.filePath,
                expectedContentHash: member.contentHash,
                commands: batch,
            });

            if (result.success) {
                member.contentHash = result.newContentHash ?? member.contentHash;
                continue;
            }

            failures.push({ member, result });
            firstFailedIndex ??= result.failedIndex ?? null;
        }

        if (failures.length > 0) {
            this._state.noteSaveFailed();
            await this._reportSetSaveFailure(failures);
            this._post({ type: 'saveResult', success: false, failedIndex: firstFailedIndex });
            return;
        }

        this._state.noteSaved(members.map(m => m.contentHash ?? '').join('|'));
        this._post({ type: 'saveResult', success: true });
    }

    /** Names the languages that would not take the write, so the user knows what is still staged. */
    private async _reportSetSaveFailure(
        failures: { member: Member; result: ApplyLocalisationBatchResult }[],
    ): Promise<void> {
        if (this._members.length <= 1) {
            await this._reportSaveFailure(failures[0].result);
            return;
        }

        const named = failures
            .map(f => `${f.member.languages.join('/') || basename(f.member.filePath)}: `
                + `${f.result.error ?? 'unknown error'}`)
            .join('; ');

        const choice = await vscode.window.showErrorMessage(
            `EaWEdit: could not save - ${named}. Your edits are still staged.`, 'Reload');

        if (choice === 'Reload') { await this._sendRows(); }
    }

    /**
     * A refused save is nearly always the file having moved on disk, and the only way forward is a
     * reload - so the message offers it rather than leaving the user to find it.
     */
    private async _reportSaveFailure(result: ApplyLocalisationBatchResult): Promise<void> {
        const failed = result.failedIndex;
        const where = failed !== null && failed !== undefined ? ` (change ${failed + 1})` : '';
        const choice = await vscode.window.showErrorMessage(
            `EaWEdit: could not save - ${result.error ?? 'unknown error'}${where}`, 'Reload');

        if (choice === 'Reload') { await this._sendRows(); }
    }

    private async _promptSaveOnClose(): Promise<void> {
        if (!this._state.shouldPromptOnClose()) { return; }

        const count = this._state.pendingCount;
        const choice = await vscode.window.showWarningMessage(
            `EaWEdit: ${count} unsaved localisation change(s) in '${this._filePath}'.`,
            { modal: true }, 'Save');

        if (choice === 'Save') { await this._save(this._state.pending); }
    }

    private _post(msg: unknown): void {
        void this._panel.webview.postMessage(msg);
    }
}

/**
 * Panels are keyed case-insensitively: Windows hands the same file back under different casings,
 * and two panels for one file would diverge.
 */
/**
 * Which of the two editors a file gets, and which protocol its edits travel over.
 *
 * The category comes from the tree, which got it from the loader, so the choice is made before the
 * webview is created - the script cannot be swapped afterwards.
 */
function isCredits(category: string): boolean {
    return category === 'credits';
}

function key(filePath: string): string {
    return filePath.toLowerCase();
}

/**
 * Identifies an open set. Sorted, so opening the same files in a different order reveals the tab
 * that is already open rather than a second one staging edits against the same files.
 */
function setKey(filePaths: string[]): string {
    return filePaths.map(key).sort().join('|');
}

function buildHtml(scriptUri: vscode.Uri, codiconUri: vscode.Uri, cspSource: string): string {
    // A bundled script, so script-src is the extension origin rather than 'unsafe-inline' - which
    // is what the old sidebar webview needed, having its JS inlined as a template literal.
    // style-src still needs 'unsafe-inline' for styled-components' injected style tags.
    return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline' ${cspSource}; script-src ${cspSource}; font-src ${cspSource}; img-src ${cspSource} data:;">
<link rel="stylesheet" href="${codiconUri}">
<style>
  /* The grid fills the tab exactly. Without this the editor sits inside whatever margin and
     padding the host's default stylesheet gives the body, and a full-height layout then overflows
     by that much - which pushes the footer under the table off the bottom of the window. */
  html, body { margin: 0; padding: 0; height: 100%; overflow: hidden; }
  #root { height: 100%; }
</style>
</head>
<body>
<div id="root"></div>
<script src="${scriptUri}"></script>
</body>
</html>`;
}
