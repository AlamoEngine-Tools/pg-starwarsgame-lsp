// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where the Mod Browser rig finds things, and the one key both halves use for an answer.
const fs = require("node:fs");
const path = require("node:path");

const RIG = __dirname;
const CLIENT = path.resolve(RIG, "..", "..");
const REPO = path.resolve(CLIENT, "..", "..");
const OUT = path.join(RIG, "out");
const SERVER_EXE = path.join(REPO, "PG.StarWarsGame.LSP.Server", "bin", "Debug", "net10.0", "PG.StarWarsGame.LSP.Server.exe");
const BUNDLE = path.join(CLIENT, "out", "webview", "modBrowser.js");

/**
 * An answer's key: the method and its params as the webview sends them. JSON.stringify keeps the
 * webview's key order and drops undefined fields, so the page's key for a request and the
 * recorder's key for the same request read back from misses.json are one string.
 */
function answerKey(method, params) {
    return `${method} ${JSON.stringify(params ?? null)}`;
}

fs.mkdirSync(OUT, {recursive: true});

module.exports = {RIG, CLIENT, REPO, OUT, SERVER_EXE, BUNDLE, answerKey};
