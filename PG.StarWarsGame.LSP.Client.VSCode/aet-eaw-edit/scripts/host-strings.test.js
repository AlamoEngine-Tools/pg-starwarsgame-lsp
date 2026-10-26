// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");
const path = require("node:path");

const {rawStrings, catalogueCalls, hostFiles, webviewFiles} = require("./host-strings");

const texts = (source) => rawStrings("fixture.ts", source).map((s) => s.text);
const jsx = (source) => rawStrings("fixture.tsx", source).map((s) => s.text);

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

    it("finds text between tags and in text attributes", () => {
        assert.deepEqual(
            jsx(
                'const a = <Field label="Wind speed" info={<>Bends foliage</>}>\n' +
                '    <span title="Heading">{count} hidden</span>\n' +
                "</Field>;",
            ),
            ['"Wind speed"', "Bends foliage", '"Heading"', "hidden"],
        );
    });

    it("finds text built in a JSX expression, and leaves wrapped text, classes and values alone", () => {
        assert.deepEqual(
            jsx(
                'const a = <div className="dock" title={`Open ${name}`}>\n' +
                '    {ready ? "Ready" : l10n.t("Waiting")}\n' +
                '    <option value="none">{l10n.t("None")}</option>\n' +
                "    {`${a} - ${b}`}\n" +
                "</div>;",
            ),
            ["`Open ${name}`", 'ready ? "Ready" : l10n.t("Waiting")'],
        );
    });

    /** A status setter feeds the slot a reader watches, so its text is shown like a notification's. */
    it("finds text fed to a status setter, and looks through a fallback", () => {
        assert.deepEqual(
            jsx(
                'setStatus("No events match the current filters.");\n' +
                'reportStatus("Warning: " + String(msg.message));\n' +
                "setStatus(null);\n" +
                'const a = <span>{status ?? "Arranging layout..."}</span>;\n' +
                'const b = <span>{status || "Idle"}</span>;',
            ),
            [
                '"No events match the current filters."',
                '"Warning: " + String(msg.message)',
                'status ?? "Arranging layout..."',
                'status || "Idle"',
            ],
        );
    });

    it("accepts a catalogued status text", () => {
        assert.deepEqual(
            jsx(
                'setStatus(l10n.t("No events match the current filters."));\n' +
                'reportStatus(l10n.t("Warning: {0}", String(msg.message)));\n' +
                'const a = <span>{status ?? l10n.t("Arranging layout...")}</span>;',
            ),
            [],
        );
    });

    it("tells label uses of a catalogue text from message uses", () => {
        const calls = (source) =>
            catalogueCalls("fixture.tsx", source).map((c) => `${c.text}|${c.label}`);
        assert.deepEqual(
            calls(
                'vscode.window.showWarningMessage(vscode.l10n.t("No project."), vscode.l10n.t("Open folder"));\n' +
                'const item = { label: l10n.t("Rebuild"), detail: l10n.t("Rebuilds it") };\n' +
                'const b = <button title={l10n.t("Clear all filters")}>{l10n.t("Clear")}</button>;\n' +
                'const m = l10n.t("Saved to {0}.", path);',
            ),
            [
                "No project.|false",
                "Open folder|true",
                "Rebuild|true",
                "Rebuilds it|true",
                "Clear all filters|true",
                "Clear|false",
                "Saved to {0}.|false",
            ],
        );
    });

    /** Every text the webviews show a reader comes from the same bundle. */
    it("finds no text written in place in the webviews", () => {
        const found = webviewFiles(
            path.join(__dirname, "..", "src", "webview"),
        ).flatMap((file) =>
            rawStrings(file).map(
                (s) => `${path.basename(file)}:${s.line} ${s.text}`,
            ),
        );

        assert.deepEqual(found, []);
    });
});
