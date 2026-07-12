// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

interface LocProjectInfo {
    label: string; filePath: string; resourceType: string; projectName: string; rank: number;
}
interface GetLocalisationProjectsResult { projects: LocProjectInfo[]; }
interface LocalisationEntryDto { key: string; translations: Record<string, string>; }
interface GetLocalisationEntriesResult {
    entries: LocalisationEntryDto[]; languages: string[]; contentHash: string; error?: string | null;
}
interface LocalisationWriteResult { success: boolean; error?: string | null; newContentHash?: string | null; }

export class LocalisationEditorViewProvider implements vscode.WebviewViewProvider {
    public static readonly viewId = 'aet-eaw-edit.lsp.localisationEditor';

    private _view?: vscode.WebviewView;
    private _currentFilePath?: string;
    private _currentContentHash?: string;
    private _officialLanguages: string[] = [];

    constructor(
        private readonly _extensionUri: vscode.Uri,
        private readonly _getLspClient: () => LanguageClient | undefined
    ) {}

    resolveWebviewView(
        view: vscode.WebviewView,
        _context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken
    ): void {
        this._view = view;
        // Codicons are copied into out/codicons by esbuild.js — node_modules is not part of
        // the packaged VSIX (vsce --no-dependencies), so it must never be referenced here.
        view.webview.options = {
            enableScripts: true,
            localResourceRoots: [
                vscode.Uri.joinPath(this._extensionUri, 'out', 'codicons')
            ]
        };
        const codiconUri = view.webview.asWebviewUri(
            vscode.Uri.joinPath(this._extensionUri, 'out', 'codicons', 'codicon.css')
        );
        view.webview.html = this._buildHtml(codiconUri, view.webview.cspSource);

        view.webview.onDidReceiveMessage(async (msg: { type: string; [key: string]: unknown }) => {
            switch (msg.type) {
                case 'ready':         await this._handleReady();                                 break;
                case 'selectProject': await this._handleSelectProject(msg.filePath as string);  break;
                case 'setEntry':
                    await this._handleSetEntry(
                        msg.key as string, msg.translations as Record<string, string>);
                    break;
                case 'deleteEntry':  await this._handleDeleteEntry(msg.key as string);            break;
                case 'resetEntry':
                    await this._handleResetEntry(
                        msg.key as string, msg.translations as Record<string, string>);
                    break;
                case 'addLanguage':  await this._handleAddLanguage(msg.language as string);       break;
                case 'requestBaseline':  await this._handleRequestBaseline();                    break;
                case 'requestLanguages': await this._handleRequestLanguages();                   break;
                case 'initProject': {
                    const choice = await vscode.window.showQuickPick(
                        [
                            {
                                label: 'Initialise from Baseline',
                                description: 'Create a fresh file seeded with the shipped game text',
                                command: 'aet-eaw-edit.lsp.initLocalisationProject',
                            },
                            {
                                label: 'Import Existing Files',
                                description: 'Adopt a directory of translation files you already have',
                                command: 'aet-eaw-edit.lsp.importLocalisationProject',
                            },
                        ],
                        { title: 'New Localisation Project', placeHolder: 'How do you want to set this up?' }
                    );
                    if (!choice) { break; }
                    await vscode.commands.executeCommand(choice.command);
                    this._currentFilePath = undefined;
                    await this._handleReady();
                    break;
                }
                case 'exportToDat': {
                    const client = this._getLspClient();
                    if (!client) {
                        this._post({ type: 'exportToDatResult', writtenFiles: [], error: 'LSP server is not running.' });
                        break;
                    }
                    try {
                        const result = await client.sendRequest<{ writtenFiles: string[]; error?: string }>(
                            'aet/exportLocalisationToDat', { projectFilePath: msg.filePath as string });
                        if (result.error) {
                            void vscode.window.showErrorMessage(`Export DAT failed: ${result.error}`);
                        } else {
                            void vscode.window.showInformationMessage(`Exported ${result.writtenFiles.length} DAT file(s).`);
                        }
                        this._post({ type: 'exportToDatResult', writtenFiles: result.writtenFiles, error: result.error ?? null });
                    } catch (err) {
                        void vscode.window.showErrorMessage(`Export DAT failed: ${err}`);
                        this._post({ type: 'exportToDatResult', writtenFiles: [], error: String(err) });
                    }
                    break;
                }
            }
        });
    }

    /** Re-fetch project list and reload the current file — called when the localisation index updates. */
    async refresh(): Promise<void> {
        if (!this._view) { return; }
        const client = this._getLspClient();
        if (!client) { return; }
        let projects: LocProjectInfo[] = [];
        try {
            const result = await client.sendRequest<GetLocalisationProjectsResult>(
                'aet/getLocalisationProjects', {});
            projects = result.projects ?? [];
        } catch { /* server not yet ready */ }
        this._post({ type: 'projects', projects });
        if (this._currentFilePath) {
            await this._loadAndSendFile(this._currentFilePath);
        } else if (projects.length) {
            await this._loadAndSendFile(projects[0].filePath);
        }
    }

    private async _handleReady(): Promise<void> {
        const client = this._getLspClient();
        if (!client) {
            this._post({ type: 'error', message: 'LSP server is not running.' });
            return;
        }
        let projects: LocProjectInfo[] = [];
        try {
            const result = await client.sendRequest<GetLocalisationProjectsResult>(
                'aet/getLocalisationProjects', {});
            projects = result.projects ?? [];
        } catch { /* server may not yet be ready */ }

        this._post({ type: 'projects', projects });

        if (projects.length) {
            await this._loadAndSendFile(projects[0].filePath);
        }
    }

    private async _handleSelectProject(filePath: string): Promise<void> {
        if (filePath) { await this._loadAndSendFile(filePath); }
    }

    private async _loadAndSendFile(filePath: string): Promise<void> {
        this._currentFilePath = filePath;
        this._currentContentHash = undefined;
        const client = this._getLspClient();
        if (!client) {
            this._post({ type: 'error', message: 'LSP server is not running.' });
            return;
        }
        try {
            const result = await client.sendRequest<GetLocalisationEntriesResult>(
                'aet/getLocalisationEntries', { projectFilePath: filePath });
            if (result.error) {
                this._post({ type: 'error', message: result.error });
                return;
            }
            this._currentContentHash = result.contentHash;
            this._post({
                type: 'fileEntries',
                filePath,
                entries: result.entries ?? [],
                languages: result.languages ?? [],
            });
        } catch (e) {
            this._post({ type: 'error', message: `Cannot load file: ${e}` });
        }
    }

    /**
     * Sends a structured write request. On failure, shows an error and resyncs from the server
     * (self-healing after e.g. a stale-content-hash rejection). Returns whether it succeeded.
     */
    private async _runWrite(method: string, extraParams: Record<string, unknown>): Promise<boolean> {
        if (!this._currentFilePath) { return false; }
        const filePath = this._currentFilePath;
        const client = this._getLspClient();
        if (!client) {
            this._post({ type: 'error', message: 'LSP server is not running.' });
            return false;
        }
        try {
            const result = await client.sendRequest<LocalisationWriteResult>(method, {
                projectFilePath: filePath,
                expectedContentHash: this._currentContentHash,
                ...extraParams,
            });
            if (!result.success) {
                void vscode.window.showErrorMessage(result.error ?? 'Localisation write failed.');
                this._post({ type: 'error', message: result.error ?? 'Write failed.' });
                await this._loadAndSendFile(filePath);
                return false;
            }
            this._currentContentHash = result.newContentHash ?? this._currentContentHash;
            return true;
        } catch (e) {
            void vscode.window.showErrorMessage(`Localisation write failed: ${e}`);
            this._post({ type: 'error', message: `Write failed: ${e}` });
            await this._loadAndSendFile(filePath);
            return false;
        }
    }

    private async _handleSetEntry(key: string, translations: Record<string, string>): Promise<void> {
        await this._runWrite('aet/setLocalisationEntry', { key, translations });
    }

    private async _handleDeleteEntry(key: string): Promise<void> {
        const choice = await vscode.window.showWarningMessage(
            `Delete "${key}"?`, { modal: true }, 'Delete');
        if (choice !== 'Delete') { return; }
        const filePath = this._currentFilePath;
        const ok = await this._runWrite('aet/deleteLocalisationEntry', { key });
        if (ok && filePath) { await this._loadAndSendFile(filePath); }
    }

    private async _handleResetEntry(key: string, translations: Record<string, string>): Promise<void> {
        const choice = await vscode.window.showWarningMessage(
            `Reset "${key}" to inherited value?`, { modal: true }, 'Reset');
        if (choice !== 'Reset') { return; }
        const filePath = this._currentFilePath;
        const ok = await this._runWrite('aet/setLocalisationEntry', { key, translations });
        if (ok && filePath) { await this._loadAndSendFile(filePath); }
    }

    private async _handleAddLanguage(language: string): Promise<void> {
        await this._runWrite('aet/addLocalisationLanguage', { language });
    }

    private async _handleRequestBaseline(): Promise<void> {
        // Retry up to 10 times at 1-second intervals in case the LSP client is not yet ready
        // (the webview fires this immediately on file load, which can race with server startup).
        for (let attempt = 0; attempt < 10; attempt++) {
            const client = this._getLspClient();
            if (client) {
                try {
                    const result = await client.sendRequest<{ entries: { key: string; translations: Record<string, string> }[] }>(
                        'aet/getBaselineEntries', { projectFilePath: this._currentFilePath });
                    const rows = (result.entries ?? []).map(e => ({
                        key: e.key,
                        translations: e.translations ?? {},
                    }));
                    if (rows.length > 0) {
                        this._post({ type: 'baselineRows', rows });
                        return;
                    }
                } catch { /* fall through to retry */ }
            }
            await new Promise(r => setTimeout(r, 1000));
        }
        this._post({ type: 'baselineRows', rows: [] });
    }

    private async _handleRequestLanguages(): Promise<void> {
        if (this._officialLanguages.length) {
            this._post({ type: 'availableLanguages', languages: this._officialLanguages });
            return;
        }
        const client = this._getLspClient();
        if (!client) {
            this._post({ type: 'availableLanguages', languages: [] });
            return;
        }
        try {
            const result = await client.sendRequest<{ languages: string[] }>('aet/getLanguages', {});
            this._officialLanguages = result.languages ?? [];
        } catch { /* leave empty */ }
        this._post({ type: 'availableLanguages', languages: this._officialLanguages });
    }

    private _post(msg: unknown): void {
        this._view?.webview.postMessage(msg);
    }

    private _buildHtml(codiconUri: vscode.Uri, cspSource: string): string {
        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline' ${cspSource}; font-src ${cspSource}; script-src 'unsafe-inline';">
<link rel="stylesheet" href="${codiconUri}">
<style>${CSS}</style>
</head>
<body>
${HTML_BODY}
<script>(function(){${WEBVIEW_SCRIPT}})();</script>
</body>
</html>`;
    }
}

// ── Webview resources (inlined to avoid localResourceRoots complexity) ──────

const CSS = `
* { box-sizing: border-box; margin: 0; padding: 0; }
body {
    font-family: var(--vscode-font-family);
    font-size: var(--vscode-font-size);
    color: var(--vscode-editor-foreground);
    background: var(--vscode-editor-background);
    height: 100vh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
}
.toolbar {
    padding: 4px 8px;
    display: flex;
    gap: 6px;
    align-items: center;
    background: var(--vscode-sideBar-background);
    border-bottom: 1px solid var(--vscode-panel-border);
    flex-shrink: 0;
}
select, input[type=text] {
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border, transparent);
    padding: 2px 6px;
    font-size: var(--vscode-font-size);
    font-family: var(--vscode-font-family);
    outline: none;
    min-width: 0;
}
select { flex: 1; cursor: pointer; }
input[type=text] { flex: 2; }
input[type=text]:focus { border-color: var(--vscode-focusBorder); }
button {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
    border: none;
    padding: 3px 8px;
    cursor: pointer;
    font-size: var(--vscode-font-size);
    font-family: var(--vscode-font-family);
    white-space: nowrap;
    flex-shrink: 0;
}
button:hover { background: var(--vscode-button-hoverBackground); }
label {
    display: flex;
    align-items: center;
    gap: 4px;
    white-space: nowrap;
    font-size: var(--vscode-font-size);
    flex-shrink: 0;
}
.table-wrap { flex: 1; overflow: auto; }
table { border-collapse: collapse; min-width: 100%; }
th, td {
    border-right: 1px solid var(--vscode-panel-border);
    border-bottom: 1px solid var(--vscode-panel-border);
    padding: 2px 6px;
    white-space: nowrap;
    font-size: var(--vscode-font-size);
    max-width: 320px;
    overflow: hidden;
    text-overflow: ellipsis;
}
th {
    background: var(--vscode-sideBarSectionHeader-background, var(--vscode-sideBar-background));
    position: sticky;
    top: 0;
    z-index: 2;
    font-weight: bold;
    text-align: left;
}
td:first-child, th:first-child {
    position: sticky;
    left: 0;
    z-index: 1;
    background: var(--vscode-sideBar-background);
    min-width: 160px;
    max-width: 240px;
}
th:first-child { z-index: 3; }
td[contenteditable=true] { cursor: text; }
td[contenteditable=true]:focus {
    outline: 1px solid var(--vscode-focusBorder);
    background: var(--vscode-input-background);
    white-space: normal;
    overflow: visible;
}
tr.baseline td { color: var(--vscode-disabledForeground); opacity: 0.7; }
tr:hover td { background: var(--vscode-list-hoverBackground); }
tr.baseline:hover td { background: var(--vscode-list-hoverBackground); }
.empty-msg {
    padding: 16px;
    color: var(--vscode-disabledForeground);
}
.mode-group { display: flex; flex-shrink: 0; }
.mode-group button {
    background: transparent;
    color: var(--vscode-foreground);
    border: 1px solid var(--vscode-button-background);
    padding: 2px 7px;
    cursor: pointer;
    font-size: var(--vscode-font-size);
    font-family: var(--vscode-font-family);
    white-space: nowrap;
}
.mode-group button + button { border-left: none; }
.mode-group button.active {
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
}
.mode-group button:hover:not(.active) { background: var(--vscode-list-hoverBackground); }
#scope-picker { flex: 0 0 auto; max-width: 130px; }
input.filter-error { border-color: var(--vscode-inputValidation-errorBorder, #be1100) !important; }
.del-btn {
    background: transparent;
    color: var(--vscode-disabledForeground);
    border: none;
    padding: 0 4px;
    cursor: pointer;
    font-size: 14px;
    line-height: 1;
}
.del-btn:hover { color: var(--vscode-errorForeground, #f44); background: transparent; }
.gutter-icon { opacity: 0.65; cursor: pointer; font-size: 13px; color: var(--vscode-gitDecoration-modifiedResourceForeground, #e2c08d); }
.gutter-icon:hover { opacity: 1; }
td.del-col, th.del-col { width: 20px; min-width: unset; max-width: 20px; padding: 0 2px; text-align: center; }
tr.inherited td { color: var(--vscode-disabledForeground); font-style: italic; }
`;

const HTML_BODY = `
<div class="toolbar">
  <select id="project-picker" title="Select localisation file"></select>
  <button id="btn-init" title="Initialise localisation project from baseline">+ New</button>
  <button id="btn-add-lang" title="Add a language column">+ Language</button>
  <select id="lang-picker" style="display:none"><option value="">Add language…</option></select>
</div>
<div class="toolbar">
  <input type="text" id="search" placeholder="Filter keys or translations…">
  <button id="btn-add-entry" title="Add new entry">+ Entry</button>
  <button id="btn-export-dat" title="Export to DAT files" disabled>Export DAT</button>
  <label title="Show entries whose values are identical to the inherited source"><input type="checkbox" id="show-inherited"> Inherited <span id="inherited-count" style="opacity:0.6;font-size:0.85em;"></span></label>
</div>
<div class="toolbar">
  <select id="scope-picker" title="Search in…">
    <option value="all">All fields</option>
    <option value="key">Key only</option>
  </select>
  <div class="mode-group">
    <button id="mode-text"     class="active codicon codicon-case-sensitive" title="Plain text search"                                   aria-label="Text"></button>
    <button id="mode-wildcard" class="codicon codicon-star-full"             title="Wildcard: * matches any text, ? matches one character" aria-label="Wildcard"></button>
    <button id="mode-regex"    class="codicon codicon-regex"                 title="Regular expression (case-insensitive)"                aria-label="Regex"></button>
  </div>
</div>
<div class="toolbar" id="new-entry-bar" style="display:none">
  <span style="flex-shrink:0;white-space:nowrap;">New key:</span>
  <input type="text" id="new-key-input" placeholder="Enter key name…">
  <button id="btn-confirm-entry">Add</button>
  <button id="btn-cancel-entry">Cancel</button>
</div>
<div class="table-wrap">
  <p class="empty-msg" id="empty-msg">No localisation project loaded.</p>
  <table id="grid" style="display:none">
    <thead id="grid-head"></thead>
    <tbody id="grid-body"></tbody>
  </table>
</div>
`;

const WEBVIEW_SCRIPT = `
const vscode = acquireVsCodeApi();

let allRows = [];
let languages = [];
let baselineRows = [];

let currentFilter = '';
let currentFilePath = '';
let currentExt = '';
let sortKey = null;
let sortDir = 1;
let rowByKey = new Map();
let lastHeaderKey = '';
let filterScope   = 'all';
let filterMode    = 'text';
let inheritedKeys = new Set();
let baselineKeySet = new Set();
let showInherited = false;
let filteredSortedRows = [];
let rowHeight = 24;
let rowHeightMeasured = false;
let scrollRafPending = false;

const projectPicker = document.getElementById('project-picker');
const btnInit       = document.getElementById('btn-init');
const btnAddLang    = document.getElementById('btn-add-lang');
const langPicker    = document.getElementById('lang-picker');
const searchInput   = document.getElementById('search');
const showInheritedCb   = document.getElementById('show-inherited');
const inheritedCountEl  = document.getElementById('inherited-count');
const emptyMsg      = document.getElementById('empty-msg');
const grid          = document.getElementById('grid');
const gridHead      = document.getElementById('grid-head');
const gridBody      = document.getElementById('grid-body');
const tableWrap       = document.querySelector('.table-wrap');
const scopePicker     = document.getElementById('scope-picker');
const btnAddEntry     = document.getElementById('btn-add-entry');
const btnExportDat    = document.getElementById('btn-export-dat');
const newEntryBar     = document.getElementById('new-entry-bar');
const newKeyInput     = document.getElementById('new-key-input');
const btnConfirmEntry = document.getElementById('btn-confirm-entry');
const btnCancelEntry  = document.getElementById('btn-cancel-entry');
const modeBtns      = ['text', 'wildcard', 'regex'].map(id => document.getElementById('mode-' + id));
const modePlaceholders = [
    'Filter keys or translations…',
    'Wildcard filter (* = any text, ? = one char)…',
    'Regex filter (case-insensitive)…',
];

projectPicker.addEventListener('change', () => {
    if (projectPicker.value) {
        vscode.postMessage({ type: 'selectProject', filePath: projectPicker.value });
    }
});

btnInit.addEventListener('click', () => vscode.postMessage({ type: 'initProject' }));

btnAddLang.addEventListener('click', () => {
    if (!currentFilePath || currentExt === 'properties') return;
    vscode.postMessage({ type: 'requestLanguages' });
});

langPicker.addEventListener('change', () => {
    const lang = langPicker.value;
    langPicker.style.display = 'none';
    langPicker.value = '';
    if (!lang || languages.includes(lang)) return;
    languages.push(lang);
    renderRows();
    vscode.postMessage({ type: 'addLanguage', language: lang });
});

function debounce(fn, ms) { let t; return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); }; }

searchInput.addEventListener('input', debounce(() => {
    currentFilter = searchInput.value;
    updateRegexValidity();
    renderRows();
}, 150));

scopePicker.addEventListener('change', () => {
    filterScope = scopePicker.value;
    renderRows();
});

modeBtns.forEach((btn, i) => {
    btn.addEventListener('click', () => {
        filterMode = ['text', 'wildcard', 'regex'][i];
        modeBtns.forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        searchInput.placeholder = modePlaceholders[i];
        updateRegexValidity();
        renderRows();
    });
});

btnAddEntry.addEventListener('click', () => {
    if (!currentFilePath) return;
    newEntryBar.style.display = '';
    newKeyInput.value = '';
    newKeyInput.focus();
});

btnExportDat.addEventListener('click', () => {
    if (!currentFilePath) return;
    btnExportDat.disabled = true;
    vscode.postMessage({ type: 'exportToDat', filePath: currentFilePath });
});

function confirmNewEntry() {
    const key = (newKeyInput.value || '').trim();
    newEntryBar.style.display = 'none';
    if (!key) return;
    const translations = Object.fromEntries(languages.map(l => [l, '']));
    allRows.unshift({ key, translations });
    currentFilter = '';
    searchInput.value = '';
    searchInput.classList.remove('filter-error');
    computeInheritedKeys();
    renderRows();
    vscode.postMessage({ type: 'setEntry', key, translations });
}

btnConfirmEntry.addEventListener('click', confirmNewEntry);
btnCancelEntry.addEventListener('click', () => { newEntryBar.style.display = 'none'; });
newKeyInput.addEventListener('keydown', e => {
    if (e.key === 'Enter') { e.preventDefault(); confirmNewEntry(); }
    if (e.key === 'Escape') { newEntryBar.style.display = 'none'; }
});

// Unchecked (default): hide rows identical to any inherited source (baseline / other project).
// Checked: show all rows; inherited ones are styled differently, plus baseline-only rows appear for override.
showInheritedCb.addEventListener('change', () => {
    showInherited = showInheritedCb.checked;
    if (baselineRows.length === 0) {
        vscode.postMessage({ type: 'requestBaseline' });
    } else {
        computeInheritedKeys();
        renderRows();
    }
});

// Delegated editable-cell events — attached once here, not recreated per render.
gridBody.addEventListener('focusout', e => {
    const td = e.target.closest('td[contenteditable="true"]');
    if (!td) return;
    const row = rowByKey.get(td.dataset.key);
    if (!row) return;
    const lang = td.dataset.lang;
    const newValue = td.textContent !== null ? td.textContent : '';
    if (newValue === (row.translations[lang] || '')) return;
    row.translations[lang] = newValue;
    // Update inherited styling on the row immediately without a full re-render.
    if (baselineRows.length) {
        const baseline = baselineRows.find(r => r.key === row.key);
        const langs = languages.length ? languages : Object.keys(row.translations);
        const tr = td.closest('tr');
        if (tr) {
            if (baseline && langs.every(l => (row.translations[l] ?? '') === (baseline.translations[l] ?? ''))) {
                inheritedKeys.add(row.key);
                tr.classList.add('inherited');
            } else {
                inheritedKeys.delete(row.key);
                tr.classList.remove('inherited');
            }
        }
    }
    vscode.postMessage({ type: 'setEntry', key: row.key, translations: row.translations });
});

gridBody.addEventListener('keydown', e => {
    if (e.key !== 'Enter' && e.key !== 'Escape') return;
    const td = e.target.closest('td[contenteditable="true"]');
    if (!td) return;
    if (e.key === 'Enter') { e.preventDefault(); td.blur(); return; }
    const row = rowByKey.get(td.dataset.key);
    if (row) td.textContent = row.translations[td.dataset.lang] !== undefined ? row.translations[td.dataset.lang] : '';
    td.blur();
});

gridBody.addEventListener('click', e => {
    const delBtn = e.target.closest('.del-btn[data-delete-key]');
    if (delBtn) {
        // Confirmation and the actual row removal both happen extension-side (VS Code-native
        // modal); on success the extension resends 'fileEntries', which re-renders the grid.
        vscode.postMessage({ type: 'deleteEntry', key: delBtn.dataset.deleteKey });
        return;
    }

    const gutterIcon = e.target.closest('.gutter-icon[data-reset-key]');
    if (gutterIcon) {
        const key = gutterIcon.dataset.resetKey;
        const baseline = baselineRows.find(r => r.key === key);
        if (baseline) {
            vscode.postMessage({ type: 'resetEntry', key, translations: { ...baseline.translations } });
        }
        return;
    }
});

tableWrap.addEventListener('scroll', () => {
    if (scrollRafPending) return;
    scrollRafPending = true;
    requestAnimationFrame(() => { scrollRafPending = false; renderViewport(); });
}, { passive: true });

new ResizeObserver(() => renderViewport()).observe(tableWrap);

window.addEventListener('message', event => {
    const msg = event.data;
    switch (msg.type) {
        case 'projects':
            renderProjects(msg.projects);
            if (!msg.projects || !msg.projects.length) btnExportDat.disabled = true;
            break;
        case 'fileEntries': {
            currentFilePath = msg.filePath;
            btnExportDat.disabled = false;
            currentExt = currentFilePath.split('.').pop().toLowerCase();
            // Dictionary string keys (e.g. "ENGLISH") get camelCased by OmniSharp's JSON
            // serializer (see the 'baselineRows' handler below) — normalise the same way here.
            allRows = (msg.entries || []).map(e => {
                const translations = {};
                for (const [k, v] of Object.entries(e.translations ?? {}))
                    translations[k.toUpperCase()] = v;
                return { key: e.key, translations };
            });
            languages = msg.languages || [];
            baselineRows = [];
            baselineKeySet = new Set();
            inheritedKeys = new Set();
            showInheritedCb.checked = false;
            showInherited = false;
            renderRows();
            vscode.postMessage({ type: 'requestBaseline' });
            break;
        }
        case 'baselineRows':
            // OmniSharp's JSON serializer camelCases dictionary string keys (e.g. "ENGLISH" → "eNGLISH"),
            // so normalise all translation keys to uppercase to match the CSV column convention.
            baselineRows = (msg.rows || []).map(r => {
                const translations = {};
                for (const [k, v] of Object.entries(r.translations ?? {}))
                    translations[k.toUpperCase()] = v;
                return { key: r.key, translations, _baseline: true };
            });
            baselineKeySet = new Set(baselineRows.map(r => r.key));
            computeInheritedKeys();
            renderRows();
            break;
        case 'availableLanguages': {
            const avail = (msg.languages || []).filter(l => !languages.includes(l));
            if (!avail.length) return;
            langPicker.innerHTML = '<option value="">Add language…</option>';
            for (const l of avail) {
                const opt = document.createElement('option');
                opt.value = l;
                opt.textContent = l;
                langPicker.appendChild(opt);
            }
            langPicker.style.display = '';
            break;
        }
        case 'exportToDatResult':
            btnExportDat.disabled = !currentFilePath;
            break;
        case 'error':
            showErrorMsg(msg.message);
            break;
    }
});

// ── Rendering ─────────────────────────────────────────────────────────────────

function renderProjects(projects) {
    const prevValue = projectPicker.value;
    projectPicker.innerHTML = '';
    if (!projects || !projects.length) {
        const opt = document.createElement('option');
        opt.value = '';
        opt.textContent = '(no projects — use + New)';
        projectPicker.appendChild(opt);
        return;
    }
    // Same-named files from different .pgproj layers (root vs. a dependency) are otherwise
    // indistinguishable — append the owning project's name when a label collides.
    const labelCounts = new Map();
    for (const p of projects) labelCounts.set(p.label, (labelCounts.get(p.label) || 0) + 1);
    for (const p of projects) {
        const opt = document.createElement('option');
        opt.value = p.filePath;
        opt.textContent = (labelCounts.get(p.label) > 1 && p.projectName)
            ? \`\${p.label} — \${p.projectName}\`
            : p.label;
        projectPicker.appendChild(opt);
    }
    if (prevValue && [...projectPicker.options].some(o => o.value === prevValue)) {
        projectPicker.value = prevValue;
    }
}

function addTh(row, text, colId) {
    const th = document.createElement('th');
    th.style.cursor = 'pointer';
    th.title = 'Click to sort';
    th.textContent = text + (sortKey === colId ? (sortDir === 1 ? ' ▲' : ' ▼') : '');
    th.addEventListener('click', () => {
        if (sortKey === colId) {
            if (sortDir === 1) { sortDir = -1; }
            else { sortKey = null; sortDir = 1; }
        } else {
            sortKey = colId;
            sortDir = 1;
        }
        renderRows();
    });
    row.appendChild(th);
}

function updateRegexValidity() {
    if (filterMode !== 'regex' || !currentFilter) {
        searchInput.classList.remove('filter-error');
        return;
    }
    try { new RegExp(currentFilter); searchInput.classList.remove('filter-error'); }
    catch { searchInput.classList.add('filter-error'); }
}

function buildMatcher(pattern) {
    if (!pattern) return () => true;
    if (filterMode === 'regex') {
        try { const re = new RegExp(pattern, 'i'); return s => re.test(s); }
        catch { return () => false; }
    }
    if (filterMode === 'wildcard') {
        const esc = pattern.replace(/([.+^{}$()|[\\]\\\\])/g, '\\\\$1').replace(/\\*/g, '.*').replace(/\\?/g, '.');
        const re = new RegExp(esc, 'i');
        return s => re.test(s);
    }
    const lower = pattern.toLowerCase();
    return s => s.toLowerCase().includes(lower);
}

function matchesFilter(row) {
    const match = buildMatcher(currentFilter);
    if (filterScope === 'key') return match(row.key);
    if (filterScope !== 'all') return match(row.translations[filterScope] || '');
    return match(row.key) || Object.values(row.translations).some(v => match(String(v)));
}

function updateScopeOptions(langs) {
    while (scopePicker.options.length > 2) scopePicker.remove(2);
    for (const lang of langs) {
        const opt = document.createElement('option');
        opt.value = lang;
        opt.textContent = lang;
        scopePicker.appendChild(opt);
    }
    if (filterScope !== 'all' && filterScope !== 'key' && !langs.includes(filterScope)) {
        filterScope = 'all';
    }
    scopePicker.value = filterScope; // always restore after DOM rebuild
}

function computeInheritedKeys() {
    inheritedKeys = new Set();
    if (!baselineRows.length) return;
    const baselineByKey = new Map(baselineRows.map(r => [r.key, r]));
    const langs = languages.length ? languages : null;
    for (const row of allRows) {
        const baseline = baselineByKey.get(row.key);
        if (!baseline) continue;
        const check = langs || Object.keys(row.translations);
        if (check.every(l => (row.translations[l] ?? '') === (baseline.translations[l] ?? ''))) {
            inheritedKeys.add(row.key);
        }
    }
}

const BUFFER = 25;

function renderRows() {
    rowByKey = new Map(allRows.map(r => [r.key, r]));
    // When showing inherited, append baseline-only rows (not already in workspace) for override.
    const extra = showInherited && baselineRows.length
        ? baselineRows.filter(r => !rowByKey.has(r.key)).map(r => ({ ...r, _baseline: true }))
        : [];
    let rows = [...allRows, ...extra];

    if (currentFilter) {
        rows = rows.filter(r => matchesFilter(r));
    }

    if (!showInherited && inheritedKeys.size) {
        rows = rows.filter(r => r._baseline || !inheritedKeys.has(r.key));
    }

    if (sortKey) {
        rows.sort((a, b) => {
            const av = sortKey === 'key' ? a.key : (a.translations[sortKey] || '');
            const bv = sortKey === 'key' ? b.key : (b.translations[sortKey] || '');
            return sortDir * av.localeCompare(bv);
        });
    }

    // Update inherited count badge so users can see whether baseline data loaded.
    inheritedCountEl.textContent = inheritedKeys.size ? '(' + inheritedKeys.size + ')' : '';

    // If no workspace file is loaded, derive language columns from the baseline rows.
    const effectiveLangs = languages.length ? languages
        : (showInherited && baselineRows.length
            ? [...new Set(baselineRows.flatMap(r => Object.keys(r.translations)))]
            : []);

    if (!rows.length) {
        if (!effectiveLangs.length) {
            emptyMsg.textContent = 'No localisation project loaded.';
        } else if (!showInherited && inheritedKeys.size) {
            emptyMsg.textContent = 'All entries are inherited — check “Inherited” to view them.';
        } else {
            emptyMsg.textContent = 'No entries match the current filter.';
        }
        emptyMsg.style.display = '';
        grid.style.display = 'none';
        filteredSortedRows = [];
        return;
    }

    updateScopeOptions(effectiveLangs);

    emptyMsg.style.display = 'none';
    grid.style.display = '';

    // Only rebuild the header row when the column structure actually changes.
    // On sort-only updates, just patch the sort-arrow text on existing <th> elements.
    const headerKey = effectiveLangs.join('|');
    if (headerKey !== lastHeaderKey) {
        lastHeaderKey = headerKey;
        const htr = document.createElement('tr');
        const thDel = document.createElement('th');
        thDel.className = 'del-col';
        htr.appendChild(thDel);
        addTh(htr, 'Key', 'key');
        for (const lang of effectiveLangs) addTh(htr, lang, lang);
        gridHead.replaceChildren(htr);
    } else {
        // ths[0] = button column (no sort), ths[1] = key, ths[2..] = languages
        const cols = ['key', ...effectiveLangs];
        const ths = gridHead.firstElementChild.children;
        for (let i = 0; i < cols.length; i++) {
            const label = i === 0 ? 'Key' : effectiveLangs[i - 1];
            ths[i + 1].textContent = label + (sortKey === cols[i] ? (sortDir === 1 ? ' ▲' : ' ▼') : '');
        }
    }

    filteredSortedRows = rows;
    rowHeightMeasured = false;
    tableWrap.scrollTop = 0;
    renderViewport(effectiveLangs);
}

function renderViewport(langs) {
    const effectiveLangs = langs !== undefined ? langs
        : (languages.length ? languages
            : [...new Set(filteredSortedRows.flatMap(r => Object.keys(r.translations)))]);
    const rows = filteredSortedRows;
    if (!rows.length) { gridBody.replaceChildren(); return; }

    // Flush any in-progress contenteditable edit before swapping the DOM.
    const focused = gridBody.querySelector(':focus');
    if (focused && focused.dataset && focused.dataset.key) { focused.blur(); }

    const viewportH = tableWrap.clientHeight;
    const scrollTop = tableWrap.scrollTop;
    const startIdx  = Math.max(0, Math.floor(scrollTop / rowHeight) - BUFFER);
    const endIdx    = Math.min(rows.length, Math.ceil((scrollTop + viewportH) / rowHeight) + BUFFER);

    const topPad    = startIdx * rowHeight;
    const bottomPad = Math.max(0, (rows.length - endIdx) * rowHeight);

    const frag = document.createDocumentFragment();

    // Empty spacer row maintains correct scrollbar height above the rendered window.
    const topSpacer = document.createElement('tr');
    topSpacer.style.height = topPad + 'px';
    frag.appendChild(topSpacer);

    for (const row of rows.slice(startIdx, endIdx)) {
        const tr = document.createElement('tr');
        if (row._baseline) tr.classList.add('baseline');
        if (!row._baseline && inheritedKeys.has(row.key)) tr.classList.add('inherited');

        const tdDel = document.createElement('td');
        tdDel.className = 'del-col';
        if (!row._baseline && !inheritedKeys.has(row.key)) {
            if (baselineKeySet.has(row.key)) {
                // Overrides a baseline value — gutter indicator only, no action.
                const icon = document.createElement('i');
                icon.className = 'codicon codicon-arrow-up gutter-icon';
                icon.title = 'Reset to inherited';
                icon.dataset.resetKey = row.key;
                tdDel.appendChild(icon);
            } else {
                // Novel key with no baseline — actual delete button.
                const btn = document.createElement('button');
                btn.className = 'del-btn';
                btn.title = 'Delete entry';
                btn.textContent = '×';
                btn.dataset.deleteKey = row.key;
                tdDel.appendChild(btn);
            }
        }
        tr.appendChild(tdDel);

        const td0 = document.createElement('td');
        td0.textContent = row.key;
        td0.title = row.key;
        tr.appendChild(td0);

        for (const lang of effectiveLangs) {
            const td = document.createElement('td');
            td.textContent = row.translations[lang] !== undefined ? row.translations[lang] : '';
            if (!row._baseline) {
                td.contentEditable = 'true';
                td.dataset.key  = row.key;
                td.dataset.lang = lang;
            }
            tr.appendChild(td);
        }

        frag.appendChild(tr);
    }

    // Empty spacer row below maintains correct scrollbar height.
    const bottomSpacer = document.createElement('tr');
    bottomSpacer.style.height = bottomPad + 'px';
    frag.appendChild(bottomSpacer);

    gridBody.replaceChildren(frag);

    // Measure the actual rendered row height once so subsequent scroll calculations are accurate.
    if (!rowHeightMeasured) {
        const firstDataRow = gridBody.children[1]; // children[0] is the top spacer
        if (firstDataRow && firstDataRow.offsetHeight) {
            rowHeight = firstDataRow.offsetHeight;
            rowHeightMeasured = true;
        }
    }
}

function showErrorMsg(message) {
    emptyMsg.textContent = '\\u26A0 ' + message;
    emptyMsg.style.display = '';
    grid.style.display = 'none';
}

vscode.postMessage({ type: 'ready' });
`;
