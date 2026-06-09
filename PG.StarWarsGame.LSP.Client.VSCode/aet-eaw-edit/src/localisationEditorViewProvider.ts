// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import * as vscode from 'vscode';
import { LanguageClient } from 'vscode-languageclient/node';

interface LocProjectInfo { label: string; filePath: string; resourceType: string; }
interface GetLocalisationProjectsResult { projects: LocProjectInfo[]; }

export class LocalisationEditorViewProvider implements vscode.WebviewViewProvider {
    public static readonly viewId = 'aet-eaw-edit.lsp.localisationEditor';

    private _view?: vscode.WebviewView;
    private _currentFilePath?: string;
    private _officialLanguages: string[] = [];

    constructor(private readonly _getLspClient: () => LanguageClient | undefined) {}

    resolveWebviewView(
        view: vscode.WebviewView,
        _context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken
    ): void {
        this._view = view;
        view.webview.options = { enableScripts: true };
        view.webview.html = this._buildHtml();

        view.webview.onDidReceiveMessage(async (msg: { type: string; [key: string]: unknown }) => {
            switch (msg.type) {
                case 'ready':         await this._handleReady();                                 break;
                case 'selectProject': await this._handleSelectProject(msg.filePath as string);  break;
                case 'setFileText':   await this._handleSetFileText(msg.text as string);         break;
                case 'requestBaseline':  await this._handleRequestBaseline();                    break;
                case 'requestLanguages': await this._handleRequestLanguages();                   break;
                case 'initProject':
                    await vscode.commands.executeCommand('aet-eaw-edit.lsp.initLocalisationProject');
                    this._currentFilePath = undefined;
                    await this._handleReady();
                    break;
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
        try {
            const bytes = await vscode.workspace.fs.readFile(vscode.Uri.file(filePath));
            const text = Buffer.from(bytes).toString('utf8');
            this._post({ type: 'fileText', filePath, text });
        } catch (e) {
            this._post({ type: 'error', message: `Cannot read file: ${e}` });
        }
    }

    private async _handleSetFileText(text: string): Promise<void> {
        if (!this._currentFilePath) { return; }
        try {
            await vscode.workspace.fs.writeFile(
                vscode.Uri.file(this._currentFilePath),
                Buffer.from(text, 'utf8')
            );
        } catch (e) {
            this._post({ type: 'error', message: `Cannot write file: ${e}` });
        }
    }

    private async _handleRequestBaseline(): Promise<void> {
        const client = this._getLspClient();
        if (!client) {
            this._post({ type: 'baselineRows', rows: [] });
            return;
        }
        try {
            const result = await client.sendRequest<{ entries: { key: string; translations: Record<string, string> }[] }>(
                'aet/getBaselineEntries', {});
            const rows = (result.entries ?? []).map(e => ({
                key: e.key,
                translations: e.translations ?? {},
            }));
            this._post({ type: 'baselineRows', rows });
        } catch {
            this._post({ type: 'baselineRows', rows: [] });
        }
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

    private _buildHtml(): string {
        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline';">
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
  <label><input type="checkbox" id="show-inherited"> Inherited</label>
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
let showBaseline = false;
let currentFilter = '';
let currentFilePath = '';
let currentExt = '';
let sortKey = null;
let sortDir = 1;

const projectPicker = document.getElementById('project-picker');
const btnInit       = document.getElementById('btn-init');
const btnAddLang    = document.getElementById('btn-add-lang');
const langPicker    = document.getElementById('lang-picker');
const searchInput   = document.getElementById('search');
const showInheritedCb = document.getElementById('show-inherited');
const emptyMsg      = document.getElementById('empty-msg');
const grid          = document.getElementById('grid');
const gridHead      = document.getElementById('grid-head');
const gridBody      = document.getElementById('grid-body');

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
    vscode.postMessage({ type: 'setFileText', text: serializeFile(currentExt, allRows, languages) });
});

searchInput.addEventListener('input', () => {
    currentFilter = searchInput.value.toLowerCase();
    renderRows();
});

showInheritedCb.addEventListener('change', () => {
    showBaseline = showInheritedCb.checked;
    if (showBaseline && baselineRows.length === 0) {
        vscode.postMessage({ type: 'requestBaseline' });
    } else {
        renderRows();
    }
});

window.addEventListener('message', event => {
    const msg = event.data;
    switch (msg.type) {
        case 'projects':
            renderProjects(msg.projects);
            break;
        case 'fileText': {
            currentFilePath = msg.filePath;
            currentExt = currentFilePath.split('.').pop().toLowerCase();
            const parsed = parseFile(currentExt, msg.text);
            allRows = parsed.rows;
            languages = parsed.languages;
            baselineRows = [];
            showInheritedCb.checked = false;
            showBaseline = false;
            renderRows();
            break;
        }
        case 'baselineRows':
            baselineRows = (msg.rows || []).map(r => ({ ...r, _baseline: true }));
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
        case 'error':
            showErrorMsg(msg.message);
            break;
    }
});

// ── File parsing ─────────────────────────────────────────────────────────────

function parseFile(ext, text) {
    if (ext === 'csv')        return parseCsv(text);
    if (ext === 'properties') return parseNls(text);
    if (ext === 'xml')        return parseXml(text);
    return { rows: [], languages: [] };
}

function parseCsvRfc4180(text) {
    // Returns string[][] — one inner array per row, one string per field.
    const result = [];
    let pos = 0;
    const len = text.length;
    while (pos < len) {
        const row = [];
        while (pos < len) {
            if (text[pos] === '"') {
                pos++;
                let field = '';
                while (pos < len) {
                    if (text[pos] === '"') {
                        if (pos + 1 < len && text[pos + 1] === '"') { field += '"'; pos += 2; }
                        else { pos++; break; }
                    } else { field += text[pos++]; }
                }
                row.push(field);
            } else {
                let field = '';
                while (pos < len && text[pos] !== ',' && text[pos] !== '\\n' && text[pos] !== '\\r')
                    field += text[pos++];
                row.push(field);
            }
            if (pos < len && text[pos] === ',') pos++;
            else break;
        }
        if (pos < len && text[pos] === '\\r') pos++;
        if (pos < len && text[pos] === '\\n') pos++;
        if (row.some(f => f !== '')) result.push(row);
    }
    return result;
}

function parseCsv(text) {
    const rows = parseCsvRfc4180(text);
    if (!rows.length) return { rows: [], languages: [] };
    const header = rows[0];
    const langs = header.slice(1);
    const parsed = [];
    for (let i = 1; i < rows.length; i++) {
        const parts = rows[i];
        const key = parts[0];
        if (!key) continue;
        const translations = {};
        for (let j = 0; j < langs.length; j++)
            translations[langs[j]] = parts[j + 1] !== undefined ? parts[j + 1] : '';
        parsed.push({ key, translations });
    }
    const activeLangs = langs.filter(lang => parsed.some(r => r.translations[lang]));
    return { rows: parsed, languages: activeLangs };
}

function parseNls(text) {
    const rows = [];
    for (const line of text.split('\\n')) {
        const t = line.replace(/\\r$/, '').trim();
        if (!t || t.startsWith('#')) continue;
        const eq = t.indexOf('=');
        if (eq < 0) continue;
        rows.push({ key: t.slice(0, eq), translations: { ENGLISH: t.slice(eq + 1) } });
    }
    return { rows, languages: ['ENGLISH'] };
}

function parseXml(text) {
    const ns = 'http://www.example.org/eaw-translation/';
    let doc;
    try { doc = new DOMParser().parseFromString(text, 'text/xml'); }
    catch { return { rows: [], languages: [] }; }
    const langSet = new Set();
    const rows = [];
    for (const loc of doc.getElementsByTagNameNS(ns, 'Localisation')) {
        const key = loc.getAttribute('key');
        if (!key) continue;
        const translations = {};
        for (const t of loc.getElementsByTagNameNS(ns, 'Translation')) {
            const lang = t.getAttribute('Language');
            if (lang) { translations[lang] = t.textContent || ''; langSet.add(lang); }
        }
        rows.push({ key, translations });
    }
    const activeLangs = [...langSet].filter(lang => rows.some(r => r.translations[lang]));
    return { rows, languages: activeLangs };
}

// ── File serialization ────────────────────────────────────────────────────────

function serializeFile(ext, rows, langs) {
    if (ext === 'csv')        return serializeCsv(rows, langs);
    if (ext === 'properties') return serializeNls(rows);
    if (ext === 'xml')        return serializeXml(rows, langs);
    return '';
}

function escapeCsvField(v) {
    if (v.indexOf(',') >= 0 || v.indexOf('"') >= 0 || v.indexOf('\\n') >= 0 || v.indexOf('\\r') >= 0)
        return '"' + v.replace(/"/g, '""') + '"';
    return v;
}

function serializeCsv(rows, langs) {
    const lines = [['key', ...langs].map(escapeCsvField).join(',')];
    for (const row of rows) {
        const fields = [row.key, ...langs.map(l => row.translations[l] !== undefined ? row.translations[l] : '')];
        lines.push(fields.map(escapeCsvField).join(','));
    }
    return lines.join('\\n') + '\\n';
}

function serializeNls(rows) {
    return rows.map(r => r.key + '=' + (r.translations['ENGLISH'] || '')).join('\\n') + '\\n';
}

function serializeXml(rows, langs) {
    const ns = 'http://www.example.org/eaw-translation/';
    const doc = document.implementation.createDocument(ns, null, null);
    const root = doc.createElementNS(ns, 'LocalisationData');
    for (const row of rows) {
        const loc = doc.createElementNS(ns, 'Localisation');
        loc.setAttribute('key', row.key);
        const td = doc.createElementNS(ns, 'TranslationData');
        for (const lang of langs) {
            const t = doc.createElementNS(ns, 'Translation');
            t.setAttribute('Language', lang);
            t.textContent = row.translations[lang] !== undefined ? row.translations[lang] : '';
            td.appendChild(t);
        }
        loc.appendChild(td);
        root.appendChild(loc);
    }
    doc.appendChild(root);
    return new XMLSerializer().serializeToString(doc);
}

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
    for (const p of projects) {
        const opt = document.createElement('option');
        opt.value = p.filePath;
        opt.textContent = p.label;
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

function renderRows() {
    const extra = showBaseline ? baselineRows.map(r => ({ ...r, _baseline: true })) : [];
    let rows = [...allRows, ...extra];

    if (currentFilter) {
        rows = rows.filter(r =>
            r.key.toLowerCase().includes(currentFilter) ||
            Object.values(r.translations).some(v => String(v).toLowerCase().includes(currentFilter))
        );
    }

    if (sortKey) {
        rows.sort((a, b) => {
            const av = sortKey === 'key' ? a.key : (a.translations[sortKey] || '');
            const bv = sortKey === 'key' ? b.key : (b.translations[sortKey] || '');
            return sortDir * av.localeCompare(bv);
        });
    }

    // If no workspace file is loaded, derive language columns from the baseline rows.
    const effectiveLangs = languages.length ? languages
        : (showBaseline && baselineRows.length
            ? [...new Set(baselineRows.flatMap(r => Object.keys(r.translations)))]
            : []);

    if (!rows.length && !effectiveLangs.length) {
        emptyMsg.textContent = 'No localisation project loaded.';
        emptyMsg.style.display = '';
        grid.style.display = 'none';
        return;
    }

    emptyMsg.style.display = 'none';
    grid.style.display = '';

    gridHead.innerHTML = '';
    const htr = document.createElement('tr');
    addTh(htr, 'Key', 'key');
    for (const lang of effectiveLangs) addTh(htr, lang, lang);
    gridHead.appendChild(htr);

    gridBody.innerHTML = '';

    for (const row of rows) {
        const tr = document.createElement('tr');
        if (row._baseline) tr.classList.add('baseline');

        const td0 = document.createElement('td');
        td0.textContent = row.key;
        td0.title = row.key;
        tr.appendChild(td0);

        for (const lang of effectiveLangs) {
            const td = document.createElement('td');
            td.textContent = row.translations[lang] !== undefined ? row.translations[lang] : '';
            if (!row._baseline) {
                td.contentEditable = 'true';
                const capturedRow = row;
                const capturedLang = lang;
                td.addEventListener('blur', () => {
                    const newValue = td.textContent !== null ? td.textContent : '';
                    if (newValue === (capturedRow.translations[capturedLang] || '')) return;
                    capturedRow.translations[capturedLang] = newValue;
                    vscode.postMessage({
                        type: 'setFileText',
                        text: serializeFile(currentExt, allRows, languages)
                    });
                });
                td.addEventListener('keydown', e => {
                    if (e.key === 'Enter') { e.preventDefault(); td.blur(); }
                    if (e.key === 'Escape') {
                        td.textContent = capturedRow.translations[capturedLang] !== undefined
                            ? capturedRow.translations[capturedLang] : '';
                        td.blur();
                    }
                });
            }
            tr.appendChild(td);
        }
        gridBody.appendChild(tr);
    }
}

function showErrorMsg(message) {
    emptyMsg.textContent = '\\u26A0 ' + message;
    emptyMsg.style.display = '';
    grid.style.display = 'none';
}

vscode.postMessage({ type: 'ready' });
`;
