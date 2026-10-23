// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Records every diagnostic a server publishes for a whole workspace, for compare.js to set against
// another run - a server build or a client configuration against another.
//
// Usage: `node measure.js <server exe> <workspace> <project file> <features.json | -> <out.json>`
//
// Starts the server over stdio with `diagnostics.workspaceOnStartup` on, so every indexed file is
// diagnosed, and the given `features` node as the client would send it (`-` sends none: the
// server's own defaults). Waits for the workspace scan, then for 20 s without a publish, and writes
// the last diagnostics of each document. Run it under `timeout 300`.
const fs = require("node:fs");
const path = require("node:path");
const {spawn} = require("node:child_process");

const [serverExe, workspaceArg, project, featuresFile, outFile] = process.argv.slice(2);
if (!serverExe || !workspaceArg || !project || !featuresFile || !outFile) {
    console.error("usage: node measure.js <server exe> <workspace> <project file> <features.json | -> <out.json>");
    process.exit(2);
}
const workspace = path.resolve(workspaceArg).replace(/\\/g, "/");
const rootUri = "file:///" + workspace.replace(/^\/+/, "");
const features = featuresFile === "-" ? undefined : JSON.parse(fs.readFileSync(featuresFile, "utf8"));

const QUIET_MS = 20000;
const LIMIT_MS = 270000;
const started = Date.now();
const published = new Map();
let lastPublish = Date.now();
let scanned = false;

const server = spawn(serverExe, [], {stdio: ["pipe", "pipe", "ignore"]});

function send(message) {
    const body = Buffer.from(JSON.stringify(message), "utf8");
    server.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
    server.stdin.write(body);
}

let buffer = Buffer.alloc(0);
server.stdout.on("data", (chunk) => {
    buffer = Buffer.concat([buffer, chunk]);
    for (; ;) {
        const end = buffer.indexOf("\r\n\r\n");
        if (end === -1) return;
        const length = Number(/content-length:\s*(\d+)/i.exec(buffer.subarray(0, end).toString())[1]);
        if (buffer.length < end + 4 + length) return;
        const message = JSON.parse(buffer.subarray(end + 4, end + 4 + length).toString("utf8"));
        buffer = buffer.subarray(end + 4 + length);
        if (message.method === "$/workspaceScanComplete") scanned = true;
        if (message.method === "textDocument/publishDiagnostics") {
            published.set(message.params.uri, message.params.diagnostics);
            lastPublish = Date.now();
        }
        if (message.method !== undefined && message.id !== undefined) {
            send({jsonrpc: "2.0", id: message.id, result: null});
        }
    }
});

function codeOf(diagnostic) {
    return String(diagnostic.code ?? diagnostic.source ?? "none");
}

function finish(reason) {
    const documents = {};
    const byCode = {};
    // Per code: how many of each severity, and the first message, to say what a count is made of.
    const detail = {};
    let total = 0;
    for (const [uri, diagnostics] of [...published].sort(([a], [b]) => a.localeCompare(b))) {
        if (diagnostics.length === 0) continue;
        documents[uri] = diagnostics.map((d) => `${codeOf(d)} ${d.range.start.line + 1}`);
        for (const d of diagnostics) {
            byCode[codeOf(d)] = (byCode[codeOf(d)] ?? 0) + 1;
            const entry = (detail[codeOf(d)] ??= {severity: {}, sample: d.message});
            entry.severity[d.severity ?? 0] = (entry.severity[d.severity ?? 0] ?? 0) + 1;
            total++;
        }
    }
    fs.writeFileSync(outFile, JSON.stringify({reason, seconds: (Date.now() - started) / 1000, total, byCode, detail, documents}, null, 1));
    console.log(`${reason}: ${total} diagnostics in ${Object.keys(documents).length} documents -> ${outFile}`);
    send({jsonrpc: "2.0", id: 9999, method: "shutdown", params: null});
    send({jsonrpc: "2.0", method: "exit", params: null});
    setTimeout(() => {
        server.kill();
        process.exit(0);
    }, 3000).unref();
}

send({
    jsonrpc: "2.0",
    id: 1,
    method: "initialize",
    params: {
        processId: process.pid,
        rootUri,
        capabilities: {},
        initializationOptions: {
            workspaceRoot: workspace,
            projectPath: `${workspace}/${project}`,
            diagnostics: {workspaceOnStartup: true},
            features,
        },
        workspaceFolders: [{uri: rootUri, name: "rig"}],
    },
});
send({jsonrpc: "2.0", method: "initialized", params: {}});

const timer = setInterval(() => {
    if (scanned && Date.now() - lastPublish > QUIET_MS) {
        clearInterval(timer);
        finish("quiet");
    } else if (Date.now() - started > LIMIT_MS) {
        clearInterval(timer);
        finish(scanned ? "limit" : "limit, scan never completed");
    }
}, 1000);
