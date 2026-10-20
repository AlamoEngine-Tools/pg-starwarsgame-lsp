// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Lua analyzer comparison (#278): talks to an analyzer directly - emmylua_ls or LuaLS - with
// nothing of this server in between, so what it answers is the analyzer's own reading.
//
// usage:
//   node scripts/lua-rig/compare.js hover <emmylua|luals> <exe> <script> <library dir> <line:col>[,...] [extra file;...]
//       Copies the script (and any extra files, e.g. a vanilla library) into a fresh directory with
//       the analyzer's config file, and prints the hover at each 0-based position.
//   node scripts/lua-rig/compare.js perf <emmylua|luals> <exe> <scripts dir> <library dir> <file, relative>
//       Copies the scripts, opens one file, and prints the time to the initialize answer, the time
//       to that file's first non-empty diagnostics, and the analyzer's peak working set.
//   node scripts/lua-rig/compare.js check <luals exe> <scripts dir> <library dir>
//       LuaLS's own batch check (--check) over a copy of the scripts; prints its findings per rule.
//
// The copies keep every config file out of the project. Measured with it (2026-10-21, vanilla FoC,
// 370 scripts): emmylua_ls 0.25.1 197 MB and 1.4 s to the first diagnostics, LuaLS 3.19.2 390 MB
// and 2.0 s.
'use strict';
const {spawn, execFileSync} = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const toUri = (p) => 'file:///' + p.replace(/\\/g, '/').replace(/^\/+/, '');
const slashed = (p) => p.replace(/\\/g, '/');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function writeConfig(kind, dir, library) {
    if (kind === 'luals') {
        fs.writeFileSync(path.join(dir, '.luarc.json'), JSON.stringify({
            'runtime.version': 'Lua 5.1',
            'workspace.library': [slashed(library)],
            'workspace.checkThirdParty': false,
        }));
    } else {
        fs.writeFileSync(path.join(dir, '.emmyrc.json'), JSON.stringify({
            runtime: {version: 'Lua5.1'},
            workspace: {library: [slashed(library)]},
        }));
    }
}

function workingSet(pid) {
    if (process.platform !== 'win32') {
        const status = fs.readFileSync(`/proc/${pid}/status`, 'utf8');
        return Number((status.match(/VmRSS:\s+(\d+)/) || [0, 0])[1]) * 1024;
    }
    try {
        return Number(execFileSync('powershell', ['-NoProfile', '-Command', `(Get-Process -Id ${pid}).WorkingSet64`],
            {encoding: 'utf8'}).trim());
    } catch {
        return 0;
    }
}

/** A minimal LSP client over the analyzer's stdio. */
function connect(exe, onNotification) {
    const child = spawn(exe, [], {stdio: ['pipe', 'pipe', 'ignore']});
    let nextId = 1;
    const pending = new Map();
    let buf = Buffer.alloc(0);
    const send = (msg) => {
        const json = JSON.stringify(msg);
        child.stdin.write(`Content-Length: ${Buffer.byteLength(json)}\r\n\r\n${json}`);
    };
    child.stdout.on('data', (chunk) => {
        buf = Buffer.concat([buf, chunk]);
        for (; ;) {
            const headerEnd = buf.indexOf('\r\n\r\n');
            if (headerEnd < 0) return;
            const len = Number(/Content-Length:\s*(\d+)/i.exec(buf.subarray(0, headerEnd).toString())[1]);
            if (buf.length < headerEnd + 4 + len) return;
            const msg = JSON.parse(buf.subarray(headerEnd + 4, headerEnd + 4 + len).toString());
            buf = buf.subarray(headerEnd + 4 + len);
            if (msg.id !== undefined && msg.method === undefined && pending.has(msg.id)) {
                pending.get(msg.id)(msg.result);
                pending.delete(msg.id);
            } else if (msg.id !== undefined && msg.method) {
                send({jsonrpc: '2.0', id: msg.id, result: null});
            } else if (msg.method) {
                onNotification?.(msg);
            }
        }
    });
    return {
        child,
        request: (method, params) => new Promise((resolve) => {
            const id = nextId++;
            pending.set(id, resolve);
            send({jsonrpc: '2.0', id, method, params});
        }),
        notify: (method, params) => send({jsonrpc: '2.0', method, params}),
        async close() {
            await Promise.race([this.request('shutdown', null), sleep(5000)]);
            this.notify('exit', null);
            await sleep(500);
            child.kill();
        },
    };
}

async function initialize(lsp, root) {
    return lsp.request('initialize', {
        processId: process.pid,
        rootUri: toUri(root),
        capabilities: {
            textDocument: {hover: {contentFormat: ['markdown']}, publishDiagnostics: {}},
            workspace: {configuration: true, workspaceFolders: true},
        },
        workspaceFolders: [{uri: toUri(root), name: 'compare'}],
    });
}

async function hover(kind, exe, script, library, spots, extras) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'aet-lua-compare-'));
    const target = path.join(root, path.basename(script));
    fs.copyFileSync(script, target);
    for (const extra of (extras || '').split(';').filter(Boolean)) {
        fs.copyFileSync(extra, path.join(root, path.basename(extra)));
    }
    writeConfig(kind, root, library);
    const lsp = connect(exe);
    await initialize(lsp, root);
    lsp.notify('initialized', {});
    lsp.notify('textDocument/didOpen', {
        textDocument: {uri: toUri(target), languageId: 'lua', version: 1, text: fs.readFileSync(target, 'utf8')},
    });
    await sleep(Number(process.env.AET_SETTLE_MS || 15000));
    for (const spot of spots.split(',')) {
        const [line, character] = spot.split(':').map(Number);
        const result = await lsp.request('textDocument/hover',
            {textDocument: {uri: toUri(target)}, position: {line, character}});
        const value = result?.contents?.value ?? result?.contents ?? null;
        console.log(`hover ${spot}: ${JSON.stringify(value)}`);
    }
    await lsp.close();
    fs.rmSync(root, {recursive: true, force: true});
}

async function perf(kind, exe, scripts, library, relative) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'aet-lua-compare-'));
    fs.cpSync(scripts, root, {recursive: true});
    writeConfig(kind, root, library);
    const target = path.join(root, relative);
    let first = null;
    const t0 = Date.now();
    const lsp = connect(exe, (msg) => {
        if (msg.method === 'textDocument/publishDiagnostics' && first === null
            && msg.params.uri.toLowerCase().endsWith(slashed(relative).toLowerCase()) && msg.params.diagnostics.length) {
            first = {ms: Date.now() - t0, count: msg.params.diagnostics.length};
        }
    });
    let peak = 0;
    const sampler = setInterval(() => {
        peak = Math.max(peak, workingSet(lsp.child.pid));
    }, 500);
    await initialize(lsp, root);
    const initMs = Date.now() - t0;
    lsp.notify('initialized', {});
    lsp.notify('textDocument/didOpen', {
        textDocument: {uri: toUri(target), languageId: 'lua', version: 1, text: fs.readFileSync(target, 'utf8')},
    });
    while (first === null && Date.now() - t0 < 180000) await sleep(50);
    await sleep(20000);
    clearInterval(sampler);
    const diagnostics = first ? `${(first.ms / 1000).toFixed(2)}s (${first.count} findings)` : 'none';
    console.log(`${kind}: initialize ${(initMs / 1000).toFixed(2)}s, first diagnostics ${diagnostics}, peak ${Math.round(peak / 1048576)} MB`);
    await lsp.close();
    fs.rmSync(root, {recursive: true, force: true});
}

async function check(exe, scripts, library) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'aet-lua-compare-'));
    const copy = path.join(root, 'scripts');
    fs.cpSync(scripts, copy, {recursive: true});
    const config = path.join(root, 'luarc.json');
    fs.writeFileSync(config, JSON.stringify({
        'runtime.version': 'Lua 5.1',
        'workspace.library': [slashed(library)],
        'workspace.checkThirdParty': false,
    }));
    const t0 = Date.now();
    await new Promise((resolve) => {
        const child = spawn(exe, [`--check=${copy}`, '--checklevel=Hint', `--configpath=${config}`,
            `--logpath=${root}`, '--check_format=json'], {stdio: 'ignore'});
        child.on('exit', resolve);
    });
    const results = JSON.parse(fs.readFileSync(path.join(root, 'check.json'), 'utf8'));
    const counts = new Map();
    for (const list of Object.values(results)) {
        for (const finding of list) counts.set(finding.code, (counts.get(finding.code) || 0) + 1);
    }
    console.log(`luals check: ${Object.keys(results).length} files with findings in ${((Date.now() - t0) / 1000).toFixed(1)}s`);
    for (const [code, count] of [...counts].sort((a, b) => b[1] - a[1])) {
        console.log(`${String(count).padStart(7)} ${code}`);
    }
    fs.rmSync(root, {recursive: true, force: true});
}

const [mode, ...args] = process.argv.slice(2);
const run = {hover: () => hover(...args), perf: () => perf(...args), check: () => check(...args)}[mode];
if (!run) {
    console.error('usage: node scripts/lua-rig/compare.js <hover|perf|check> ... (see the header)');
    process.exit(2);
}
run().catch((error) => {
    console.error(error);
    process.exit(1);
});
