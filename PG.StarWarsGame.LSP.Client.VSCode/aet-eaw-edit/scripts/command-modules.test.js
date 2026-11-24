// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The extension host by area (#324).
//
// `activate` once held every command inline: 43 registrations in 1,900 lines, and a command was found
// by searching for its id. Each area's commands now live in the module named after it under
// src/commands, registered through one `register(context, deps)`, and `activate` only builds the host
// and hands it over. The stored values go through the slots of src/stores.ts and nowhere else. These
// hold that shape, so the next command lands in its area's module rather than back in `activate`.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");
const fs = require("node:fs");
const path = require("node:path");

const root = path.join(__dirname, "..");
const src = path.join(root, "src");
const commands = path.join(src, "commands");

const read = (file) => fs.readFileSync(file, "utf8");
const extension = read(path.join(src, "extension.ts"));

/** The command modules: every file under src/commands but the contract they share. */
function commandModules() {
    return fs
        .readdirSync(commands)
        .filter((name) => name.endsWith(".ts") && !name.endsWith(".test.ts") && name !== "host.ts")
        .map((name) => ({name, text: read(path.join(commands, name))}));
}

/** Every non-test source of the host: src and its folders, the webviews left out. */
function hostSources(dir = src) {
    return fs.readdirSync(dir, {withFileTypes: true}).flatMap((entry) => {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            return entry.name === "webview" ? [] : hostSources(full);
        }
        return /\.ts$/.test(entry.name) && !/\.test\.ts$/.test(entry.name) ? [{file: full, text: read(full)}] : [];
    });
}

const REGISTRATION = /registerCommand\(\s*(?:"([^"]+)"|([A-Z_]+))/g;

describe("command modules", () => {
    it("finds the command modules", () => {
        assert.ok(commandModules().length >= 8);
    });

    it("has every one export register(context, deps)", () => {
        assert.deepEqual(
            commandModules()
                .filter(
                    (module) =>
                        !/export function register\(\s*context: vscode\.ExtensionContext,\s*_?deps: HostDeps,?\s*\): void/.test(
                            module.text,
                        ),
                )
                .map((module) => module.name),
            [],
        );
    });

    it("has activate call every one of them", () => {
        assert.deepEqual(
            commandModules()
                .map((module) => module.name.replace(/\.ts$/, ""))
                .filter((area) => !extension.includes(`${area}Commands.register(context, deps);`)),
            [],
        );
    });

    it("leaves no command registered in the host itself", () => {
        assert.deepEqual([...extension.matchAll(REGISTRATION)], []);
    });

    it("keeps activate under 400 lines", () => {
        const lines = extension.split(/\r?\n/);
        const start = lines.findIndex((line) => line.startsWith("export async function activate("));
        const end = lines.findIndex((line, index) => index > start && line === "}");
        assert.ok(start >= 0 && end > start, "activate not found");
        assert.ok(end - start < 400, `activate is ${end - start} lines`);
    });

    it("registers every command of the manifest, each once", () => {
        // A constant stands for its id: the module that owns the id exports it under that name.
        const constants = new Map(
            hostSources().flatMap(({text}) =>
                [...text.matchAll(/export const ([A-Z_]+) =\s*"(aet-eaw-edit\.[\w.]+)"/g)].map((m) => [m[1], m[2]]),
            ),
        );
        const registered = commandModules().flatMap((module) =>
            [...module.text.matchAll(REGISTRATION)].map((m) => m[1] ?? constants.get(m[2]) ?? m[2]),
        );
        const declared = JSON.parse(read(path.join(root, "package.json"))).contributes.commands.map(
            (command) => command.command,
        );

        assert.deepEqual(
            registered.filter((id, index) => registered.indexOf(id) !== index),
            [],
            "registered twice",
        );
        assert.deepEqual(
            declared.filter((id) => !registered.includes(id)),
            [],
            "declared in the manifest and registered nowhere",
        );
    });
});

describe("stored values", () => {
    it("reads the two mementos in one place: where activate builds the stores", () => {
        const readers = hostSources()
            .filter(({text}) => /\b(?:globalState|workspaceState)\b/.test(text.replace(/\/\/.*|\/\*[\s\S]*?\*\//g, "")))
            .map(({file}) => path.relative(src, file).replace(/\\/g, "/"));

        assert.deepEqual(readers, ["extension.ts"]);
        assert.equal([...extension.matchAll(/\bcontext\.(?:globalState|workspaceState)\b/g)].length, 2);
        assert.match(extension, /stores = createStores\(context\.globalState, context\.workspaceState\);/);
    });

    it("keeps no memento in a module of its own", () => {
        assert.deepEqual(
            hostSources()
                .filter(({text}) => /^let \w+\s*:\s*(?:vscode\.)?Memento\b/m.test(text))
                .map(({file}) => path.relative(src, file).replace(/\\/g, "/")),
            [],
        );
    });
});
