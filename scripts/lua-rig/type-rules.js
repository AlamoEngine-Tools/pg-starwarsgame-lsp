// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Lua analyzer rule census (#278): launches the Debug server with the Lua analyzer against one
// project, opens every script under its Data/Scripts, waits until the analyzer has stopped
// reporting, shuts down, and counts the analyzer's findings by rule from the server's debug log -
// the ones the rule policy keeps and the ones it drops alike. Writes one JSON document: per rule its
// fate, its count and the files it fires in, and an evenly spread sample of findings with the
// source line each points at, for reading by hand.
//
// usage: node scripts/lua-rig/type-rules.js <label> <project folder> <project file name>
//
// environment (all optional):
//   AET_SERVER_EXE   path to PG.StarWarsGame.LSP.Server.exe (default: the Debug build in this repo)
//   AET_EMMYLUA_LS   the analyzer (default: the one the extension ships, under its server/ folder)
//   AET_SCHEMA_DIR   local schema directory (default: <repo>/schema/eaw)
//   AET_BASELINE     local baseline file (default: <repo>/baseline/foc/aet-pg-swg-lsp-foc-baseline.aet)
//   AET_SAMPLE       findings sampled per rule (default: 10)
//   AET_BATCH        scripts open at once, each with an analyzer of its own (default: 8)
//   AET_SETTLE_MS    how long a batch must go without a report before it is closed (default: 8000)
//   AET_BATCH_MS     the longest a batch stays open (default: 120000)
//   AET_QUIET_MS     how long the census waits after the last batch (default: 5000)
'use strict';
const {spawn} = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const REPO = path.resolve(__dirname, '..', '..');
const EXE = process.env.AET_SERVER_EXE
    || path.join(REPO, 'PG.StarWarsGame.LSP.Server', 'bin', 'Debug', 'net10.0', 'PG.StarWarsGame.LSP.Server.exe');
const ANALYZER = process.env.AET_EMMYLUA_LS
    || path.join(REPO, 'PG.StarWarsGame.LSP.Client.VSCode', 'aet-eaw-edit', 'server', 'emmylua',
        process.platform === 'win32' ? 'emmylua_ls.exe' : 'emmylua_ls');
const SCHEMA = process.env.AET_SCHEMA_DIR || path.join(REPO, 'schema', 'eaw');
const BASELINE = process.env.AET_BASELINE || path.join(REPO, 'baseline', 'foc', 'aet-pg-swg-lsp-foc-baseline.aet');
const SAMPLE = Number(process.env.AET_SAMPLE || 10);
const QUIET_MS = Number(process.env.AET_QUIET_MS || 5000);
const BATCH = Number(process.env.AET_BATCH || 8);
const SETTLE_MS = Number(process.env.AET_SETTLE_MS || 8000);
const BATCH_MS = Number(process.env.AET_BATCH_MS || 120000);

const [label, folder, projectFile] = process.argv.slice(2);
if (!label || !folder || !projectFile) {
    console.error('usage: node scripts/lua-rig/type-rules.js <label> <project folder> <project file name>');
    process.exit(2);
}

const toUri = (p) => 'file:///' + p.replace(/\\/g, '/').replace(/^\/+/, '');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function scripts(dir) {
    return fs.readdirSync(dir, {withFileTypes: true}).flatMap((entry) => {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) return scripts(full);
        return entry.name.toLowerCase().endsWith('.lua') ? [full] : [];
    });
}

const scriptDir = fs.readdirSync(folder).find((n) => n.toLowerCase() === 'data');
const scriptsRoot = scriptDir && fs.readdirSync(path.join(folder, scriptDir)).find((n) => n.toLowerCase() === 'scripts');
if (!scriptsRoot) {
    console.error(`[rig] no Data/Scripts under ${folder}`);
    process.exit(2);
}
const files = scripts(path.join(folder, scriptDir, scriptsRoot));

const logDir = fs.mkdtempSync(path.join(os.tmpdir(), 'aet-lua-rules-'));
const child = spawn(EXE, [`--log-dir=${logDir}`], {
    stdio: ['pipe', 'pipe', 'pipe'],
    env: {...process.env, AET_EMMYLUA_LS: ANALYZER},
});
child.stderr.on('data', (d) => process.stderr.write(`[srv] ${d}`));

let nextId = 1;
const pending = new Map();
const send = (msg) => {
    const json = JSON.stringify(msg);
    child.stdin.write(`Content-Length: ${Buffer.byteLength(json)}\r\n\r\n${json}`);
};
const request = (method, params) => new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, {resolve, reject});
    send({jsonrpc: '2.0', id, method, params});
});
const notify = (method, params) => send({jsonrpc: '2.0', method, params});

let buf = Buffer.alloc(0);
let scanDone = false;
child.stdout.on('data', (chunk) => {
    buf = Buffer.concat([buf, chunk]);
    for (; ;) {
        const headerEnd = buf.indexOf('\r\n\r\n');
        if (headerEnd < 0) return;
        const len = Number(/Content-Length:\s*(\d+)/i.exec(buf.subarray(0, headerEnd).toString())[1]);
        if (buf.length < headerEnd + 4 + len) return;
        const body = buf.subarray(headerEnd + 4, headerEnd + 4 + len).toString();
        buf = buf.subarray(headerEnd + 4 + len);
        let msg;
        try {
            msg = JSON.parse(body);
        } catch {
            continue;
        }
        if (msg.id !== undefined && msg.method === undefined && pending.has(msg.id)) {
            const p = pending.get(msg.id);
            pending.delete(msg.id);
            msg.error ? p.reject(msg.error) : p.resolve(msg.result);
        } else if (msg.method === '$/workspaceScanComplete') {
            scanDone = true;
        } else if (msg.id !== undefined && msg.method) {
            send({jsonrpc: '2.0', id: msg.id, result: null});
        }
    }
});

function logText() {
    return fs.readdirSync(logDir)
        .filter((n) => n.endsWith('.log'))
        .map((n) => fs.readFileSync(path.join(logDir, n), 'utf8'))
        .join('\n');
}

// The scripts that have reported so far, read off the log as it grows.
const reports = {count: 0, files: new Set()};
const logOffsets = new Map();
const pathKey = (p) => decodeURIComponent(p).replace(/^file:\/\/\//i, '').replace(/\\/g, '/').toLowerCase();

function readNewReports() {
    for (const name of fs.readdirSync(logDir).filter((n) => n.endsWith('.log'))) {
        const full = path.join(logDir, name);
        const from = logOffsets.get(full) || 0;
        const size = fs.statSync(full).size;
        if (size <= from) continue;
        const fd = fs.openSync(full, 'r');
        const chunk = Buffer.alloc(size - from);
        fs.readSync(fd, chunk, 0, chunk.length, from);
        fs.closeSync(fd);
        const text = chunk.toString('utf8');
        const complete = text.lastIndexOf('\n') + 1;
        logOffsets.set(full, from + Buffer.byteLength(text.slice(0, complete)));
        for (const m of text.slice(0, complete).matchAll(/Analyzer report (\S+) - \d+ findings/g)) {
            reports.count++;
            reports.files.add(pathKey(m[1]));
        }
    }
}

const FINDING = /Analyzer finding (\S*) (kept|dropped) at (\S+):(\d+) - (.*)$/;
const REPORT = /Analyzer report (\S+) - \d+ findings$/;

(async () => {
    const t0 = Date.now();
    await request('initialize', {
        processId: process.pid,
        rootUri: toUri(folder),
        workspaceFolders: [{uri: toUri(folder), name: path.basename(folder)}],
        capabilities: {
            workspace: {workspaceFolders: true, configuration: false},
            textDocument: {synchronization: {dynamicRegistration: false}, publishDiagnostics: {}},
        },
        initializationOptions: {
            workspaceRoot: folder,
            projectPath: path.join(folder, projectFile),
            locale: 'en',
            schemaLocalPath: SCHEMA,
            baselineType: 'local',
            baselineLocalPath: BASELINE,
            diagnostics: {workspaceOnStartup: false},
        },
    });
    notify('initialized', {});
    while (!scanDone && Date.now() - t0 < 300000) await sleep(1000);
    console.error(`[rig] scan complete=${scanDone} after ${Date.now() - t0}ms; opening ${files.length} scripts`);

    // Every open script runs an analyzer of its own, so the scripts are opened BATCH at a time: a
    // batch is closed once each of its scripts has reported and no report has come for SETTLE_MS,
    // or after BATCH_MS. A script that never reports is listed.
    const opened = Date.now();
    const silent = [];
    for (let i = 0; i < files.length; i += BATCH) {
        const batch = files.slice(i, i + BATCH);
        for (const file of batch) {
            notify('textDocument/didOpen', {
                textDocument: {uri: toUri(file), languageId: 'lua', version: 1, text: fs.readFileSync(file, 'utf8')},
            });
        }
        const started = Date.now();
        let lastReport = Date.now();
        let reportCount = reports.count;
        while (Date.now() - started < BATCH_MS) {
            await sleep(500);
            readNewReports();
            if (reports.count !== reportCount) {
                reportCount = reports.count;
                lastReport = Date.now();
            }
            const all = batch.every((f) => reports.files.has(pathKey(f)));
            if (all && Date.now() - lastReport >= SETTLE_MS) break;
        }
        silent.push(...batch.filter((f) => !reports.files.has(pathKey(f))));
        for (const file of batch) notify('textDocument/didClose', {textDocument: {uri: toUri(file)}});
        console.error(`[rig] ${Math.min(i + BATCH, files.length)}/${files.length} after ${Date.now() - opened}ms`);
    }
    await sleep(QUIET_MS);
    console.error(`[rig] done after ${Date.now() - opened}ms; ${silent.length} scripts never reported`);

    await request('shutdown', null).catch(() => undefined);
    notify('exit', null);
    await sleep(2000);

    // A document is reported again whenever the analyzer re-runs it, and an early report (before the
    // library is indexed) carries findings a later one withdraws: only each document's LAST report
    // counts. A report line opens it; the finding lines that follow belong to it.
    const lastReport = new Map(); // lower-cased uri -> findings of its latest report
    for (const line of logText().split(/\r?\n/)) {
        const r = REPORT.exec(line);
        if (r) {
            lastReport.set(r[1].toLowerCase(), []);
            continue;
        }
        const m = FINDING.exec(line);
        if (!m) continue;
        const [, rule, fate, uri, lineNo, message] = m;
        const findings = lastReport.get(uri.toLowerCase());
        if (!findings) continue;
        findings.push({rule, fate, uri, line: Number(lineNo), message});
    }

    const byRule = new Map();
    for (const f of [...lastReport.values()].flat()) {
        const entry = byRule.get(f.rule) || {rule: f.rule, fate: f.fate, count: 0, files: new Set(), findings: []};
        entry.count++;
        entry.files.add(f.uri.toLowerCase());
        entry.findings.push(f);
        byRule.set(f.rule, entry);
    }

    const sourceLine = (uri, line) => {
        try {
            const file = decodeURIComponent(uri.replace(/^file:\/\/\//, ''));
            return fs.readFileSync(file, 'utf8').split(/\r?\n/)[line - 1]?.trim() ?? '';
        } catch {
            return '';
        }
    };

    const rules = [...byRule.values()]
        .sort((a, b) => b.count - a.count)
        .map((r) => {
            const sorted = r.findings.sort((a, b) => (a.uri + a.line).localeCompare(b.uri + b.line));
            const step = Math.max(1, Math.floor(sorted.length / SAMPLE));
            const sample = sorted.filter((_, i) => i % step === 0).slice(0, SAMPLE).map((f) => ({
                file: path.basename(decodeURIComponent(f.uri)),
                line: f.line,
                message: f.message,
                source: sourceLine(f.uri, f.line),
            }));
            return {rule: r.rule, fate: r.fate, count: r.count, files: r.files.size, sample};
        });

    process.stdout.write(JSON.stringify({
        label,
        project: path.join(folder, projectFile),
        scripts: files.length,
        neverReported: silent.map((f) => path.relative(folder, f)),
        findings: rules.reduce((sum, r) => sum + r.count, 0),
        rules,
    }, null, 2) + '\n');
    fs.rmSync(logDir, {recursive: true, force: true});
})().catch((error) => {
    console.error(error);
    child.kill();
    process.exit(1);
});
