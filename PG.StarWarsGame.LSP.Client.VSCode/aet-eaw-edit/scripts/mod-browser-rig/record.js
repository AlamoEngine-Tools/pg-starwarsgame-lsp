// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Records the server's answers to the Mod Browser's requests, for render.js to replay.
//
// Usage: `node record.js <workspace> <project file> <requests.json> [answers.json]`
//
// Starts the Debug server on one project over stdio, waits for the workspace scan, sends each
// `[method, params]` of the requests file and merges the answers into the answers file (default
// `out/answers.json`), keyed exactly as render.js looks them up. render.js lists the requests it
// could not answer in `out/misses.json`, which is a requests file: record those, render again.
const fs = require("node:fs");
const path = require("node:path");
const {spawn} = require("node:child_process");
const {OUT, SERVER_EXE, answerKey} = require("./paths");

const [workspaceArg, project, requestsFile, answersArg] = process.argv.slice(2);
if (!workspaceArg || !project || !requestsFile) {
    console.error("usage: node record.js <workspace> <project file> <requests.json> [answers.json]");
    process.exit(2);
}
const workspace = path.resolve(workspaceArg).replace(/\\/g, "/");
const rootUri = "file:///" + workspace.replace(/^\/+/, "");
const answersFile = answersArg ?? path.join(OUT, "answers.json");
const requests = JSON.parse(fs.readFileSync(requestsFile, "utf8"));
const answers = fs.existsSync(answersFile) ? JSON.parse(fs.readFileSync(answersFile, "utf8")) : {};

const server = spawn(SERVER_EXE, [], {stdio: ["pipe", "pipe", "ignore"]});
const pending = new Map();
let scanned = null;
const scanComplete = new Promise((resolve) => (scanned = resolve));

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
        if (message.method === "$/workspaceScanComplete") scanned();
        if (message.method !== undefined && message.id !== undefined) {
            send({jsonrpc: "2.0", id: message.id, result: null});
        } else if (message.id !== undefined && pending.has(message.id)) {
            pending.get(message.id)(message);
            pending.delete(message.id);
        }
    }
});

let nextId = 1;

function request(method, params, timeoutMs = 120000) {
    const id = nextId++;
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error(`${method} timed out`)), timeoutMs);
        pending.set(id, (message) => {
            clearTimeout(timer);
            resolve(message);
        });
        send({jsonrpc: "2.0", id, method, params});
    });
}

(async () => {
    await request("initialize", {
        processId: process.pid,
        rootUri,
        capabilities: {},
        initializationOptions: {workspaceRoot: workspace, projectPath: `${workspace}/${project}`},
        workspaceFolders: [{uri: rootUri, name: "rig"}],
    });
    send({jsonrpc: "2.0", method: "initialized", params: {}});
    await Promise.race([scanComplete, new Promise((r) => setTimeout(r, 240000))]);

    for (const [method, params] of requests) {
        const started = Date.now();
        const answer = await request(method, params);
        answers[answerKey(method, params)] = answer.result ?? null;
        console.log(`${method} ${JSON.stringify(params)} - ${Date.now() - started} ms${answer.error ? ` - ${answer.error.message}` : ""}`);
    }
    fs.writeFileSync(answersFile, JSON.stringify(answers));
    console.log(`${Object.keys(answers).length} answers in ${answersFile}`);

    await request("shutdown", null, 30000).catch(() => undefined);
    send({jsonrpc: "2.0", method: "exit", params: null});
    setTimeout(() => server.kill(), 5000).unref();
})().catch((error) => {
    console.error(error);
    server.kill();
    process.exit(1);
});
