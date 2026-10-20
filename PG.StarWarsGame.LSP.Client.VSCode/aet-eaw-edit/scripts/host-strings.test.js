// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

const assert = require("node:assert/strict");
const { describe, it } = require("node:test");
const path = require("node:path");

const { rawStrings, hostFiles } = require("./host-strings");

const texts = (source) => rawStrings("fixture.ts", source).map((s) => s.text);

describe("host strings", () => {
    it("finds a notification written in place, and its buttons", () => {
        assert.deepEqual(
            texts(
                `vscode.window.showWarningMessage("No project found", "Open folder");`,
            ),
            ['"No project found"', '"Open folder"'],
        );
    });

    it("finds a template and a concatenation", () => {
        assert.deepEqual(
            texts(
                "vscode.window.showErrorMessage(`Could not read ${name}`);\n" +
                    'vscode.window.showErrorMessage("Could not read " + name);',
            ),
            ["`Could not read ${name}`", '"Could not read " + name'],
        );
    });

    it("finds the texts of quick picks, input boxes, progress and tree items", () => {
        assert.equal(
            texts(
                'vscode.window.showQuickPick(items, { placeHolder: "Pick one", title: "Model" });\n' +
                    'vscode.window.showInputBox({ prompt: "Name" });\n' +
                    'vscode.window.withProgress({ title: "Indexing" }, run);\n' +
                    'progress.report({ message: "Parsing" });\n' +
                    'const item = new vscode.TreeItem("Scripts");\n' +
                    'item.tooltip = "All scripts";\n' +
                    'vscode.window.createWebviewPanel("x.view", "Preview", column);',
            ).length,
            8,
        );
    });

    it("accepts a wrapped text and leaves ids and values alone", () => {
        assert.deepEqual(
            texts(
                'vscode.window.showWarningMessage(vscode.l10n.t("No project found"));\n' +
                    'l10n.t("Could not read {0}", name);\n' +
                    'vscode.commands.executeCommand("aet-eaw-edit.refresh");\n' +
                    'item.tooltip = "";\n' +
                    "item.description = `${file} - ${state}`;\n" +
                    'vscode.window.createWebviewPanel("x.view", vscode.l10n.t("Preview"), column);',
            ),
            [],
        );
    });

    /** Every text the extension host shows a reader comes from the l10n bundle. */
    it("finds no text written in place in the extension host", () => {
        const found = hostFiles(path.join(__dirname, "..", "src")).flatMap(
            (file) =>
                rawStrings(file).map(
                    (s) => `${path.basename(file)}:${s.line} ${s.text}`,
                ),
        );

        assert.deepEqual(found, []);
    });
});
