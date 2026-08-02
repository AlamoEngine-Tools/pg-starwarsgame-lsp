// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

import { CreditsPreviewPanel } from './creditsPreviewPanel';
import { LocalisationPanelState } from './webview/localisationPanelState';

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
export class LocalisationEditorPanel {
    private static readonly _panels = new Map<string, LocalisationEditorPanel>();

    /** The tab a title-bar command applies to. */
    private static _active: LocalisationEditorPanel | null = null;

    private readonly _panel: vscode.WebviewPanel;
    private readonly _state = new LocalisationPanelState();

    private constructor(
        private readonly _filePath: string,
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
            LocalisationEditorPanel._panels.delete(key(_filePath));
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
        const existing = LocalisationEditorPanel._panels.get(key(filePath));
        if (existing) { existing._panel.reveal(); return; }

        LocalisationEditorPanel._panels.set(
            key(filePath),
            new LocalisationEditorPanel(filePath, label, category, extensionUri, getLspClient));
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

        const result = await client.sendRequest<GetLocalisationRowsResult>(
            'aet/getLocalisationRows', { projectFilePath: this._filePath });

        if (result.error) { this._post({ type: 'error', message: result.error }); return; }

        // An unchanged file is not re-delivered. A save reloads the server's localisation index and
        // the watcher then reports that same write, so one save announced the index had moved twice
        // over - and each announcement made every open tab hand its webview every row again, reset
        // its selection, sort and inherited toggle, and re-fetch the baseline. See shouldDeliver.
        if (!this._state.shouldDeliver(result.contentHash)) { return; }

        this._state.noteRead(result.contentHash);
        this._post({
            supportedLanguages: await this._supportedLanguagesAsync(),
            type: 'rows',
            rows: result.rows,
            languages: result.languages,
            category: result.category,
            ordered: result.ordered,
            canAddLanguage: result.canAddLanguage,
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

    private async _save(commands: Record<string, unknown>[]): Promise<void> {
        const client = this._getLspClient();
        if (!client) { return; }

        const result = await client.sendRequest<ApplyLocalisationBatchResult>(
            isCredits(this._category) ? 'aet/applyCreditsBatch' : 'aet/applyTranslationBatch',
            {
                projectFilePath: this._filePath,
                expectedContentHash: this._state.contentHash,
                commands,
            });

        if (!result.success) {
            this._state.noteSaveFailed();
            await this._reportSaveFailure(result);
            this._post({ type: 'saveResult', success: false, failedIndex: result.failedIndex ?? null });
            return;
        }

        this._state.noteSaved(result.newContentHash ?? undefined);
        this._post({ type: 'saveResult', success: true });
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
