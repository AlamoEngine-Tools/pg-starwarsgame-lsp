// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every text the extension host shows a reader goes through `vscode.l10n.t` (or `l10n.t` from
// `@vscode/l10n` in a module that cannot import `vscode`), so it lands in the l10n bundle. This
// walks the host's syntax trees and finds literals that reach a user-facing API directly:
// notifications and their buttons, quick picks, input boxes, progress, status bar, tree items and
// panel titles. Run directly, it checks `src/` outside the webviews and fails `npm run lint`.

const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");

/** Calls whose every string argument is shown: the message and the buttons. */
const SHOWN_ARGUMENTS = new Set([
    "showInformationMessage",
    "showWarningMessage",
    "showErrorMessage",
]);

/** Properties that are shown wherever they are written: options, items, progress reports. */
const SHOWN_PROPERTIES = new Set([
    "title",
    "placeHolder",
    "prompt",
    "label",
    "description",
    "detail",
    "tooltip",
    "message",
    "text",
]);

/** Whether written text carries words - a format such as `${a} - ${b}` gives a translator nothing. */
const hasWords = (text) => /[A-Za-z]/.test(text);

/** Whether an expression is text written in place: a literal, a template, or one joined in. */
function isRaw(node) {
    if (ts.isParenthesizedExpression(node)) {
        return isRaw(node.expression);
    }
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) {
        return hasWords(node.text);
    }
    if (ts.isTemplateExpression(node)) {
        return (
            hasWords(node.head.text) ||
            node.templateSpans.some((span) => hasWords(span.literal.text))
        );
    }
    if (
        ts.isBinaryExpression(node) &&
        node.operatorToken.kind === ts.SyntaxKind.PlusToken
    ) {
        return isRaw(node.left) || isRaw(node.right);
    }
    if (ts.isConditionalExpression(node)) {
        return isRaw(node.whenTrue) || isRaw(node.whenFalse);
    }
    return false;
}

function calleeName(call) {
    const callee = call.expression;
    if (ts.isPropertyAccessExpression(callee)) {
        return callee.name.text;
    }
    return ts.isIdentifier(callee) ? callee.text : "";
}

/** The raw texts in one file, each with its line and source text. */
function rawStrings(file, source = fs.readFileSync(file, "utf8")) {
    const tree = ts.createSourceFile(
        file,
        source,
        ts.ScriptTarget.Latest,
        true,
    );
    const found = [];
    const report = (node) => {
        const { line } = tree.getLineAndCharacterOfPosition(
            node.getStart(tree),
        );
        found.push({ line: line + 1, text: node.getText(tree) });
    };

    const visit = (node) => {
        if (ts.isCallExpression(node)) {
            const name = calleeName(node);
            if (SHOWN_ARGUMENTS.has(name)) {
                node.arguments.filter(isRaw).forEach(report);
            }
            if (
                name === "createWebviewPanel" &&
                node.arguments[1] &&
                isRaw(node.arguments[1])
            ) {
                report(node.arguments[1]);
            }
            if (
                name === "showQuickPick" &&
                node.arguments[0] &&
                ts.isArrayLiteralExpression(node.arguments[0])
            ) {
                node.arguments[0].elements.filter(isRaw).forEach(report);
            }
        }
        if (
            ts.isNewExpression(node) &&
            /TreeItem$/.test(node.expression.getText(tree))
        ) {
            const label = node.arguments?.[0];
            if (label && isRaw(label)) {
                report(label);
            }
        }
        if (
            ts.isPropertyAssignment(node) &&
            SHOWN_PROPERTIES.has(node.name.getText(tree)) &&
            isRaw(node.initializer)
        ) {
            report(node.initializer);
        }
        if (
            ts.isBinaryExpression(node) &&
            node.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
            ts.isPropertyAccessExpression(node.left) &&
            SHOWN_PROPERTIES.has(node.left.name.text) &&
            isRaw(node.right)
        ) {
            report(node.right);
        }
        ts.forEachChild(node, visit);
    };

    visit(tree);
    return found;
}

/** The extension host's own sources: `src/` outside the webviews, tests excluded. */
function hostFiles(src) {
    const files = [];
    const walk = (dir) => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                if (entry.name !== "webview") {
                    walk(full);
                }
            } else if (
                /\.ts$/.test(entry.name) &&
                !/\.test\.ts$/.test(entry.name)
            ) {
                files.push(full);
            }
        }
    };
    walk(src);
    return files.sort();
}

module.exports = { rawStrings, hostFiles };

if (require.main === module) {
    const src = path.join(__dirname, "..", "src");
    let count = 0;
    for (const file of hostFiles(src)) {
        for (const s of rawStrings(file)) {
            console.error(
                `${path.relative(src, file)}:${s.line} text written in place: ${s.text}`,
            );
            count++;
        }
    }
    if (count > 0) {
        process.exit(1);
    }

    // The committed bundle is what translators work from, so it has to match the code.
    const { execFileSync } = require("node:child_process");
    const os = require("node:os");
    const fresh = fs.mkdtempSync(path.join(os.tmpdir(), "aet-l10n-"));
    execFileSync(
        process.execPath,
        [
            require.resolve("@vscode/l10n-dev/dist/cli.js"),
            "export",
            "--outDir",
            fresh,
            src,
        ],
        { stdio: "ignore" },
    );
    const bundle = (dir) =>
        fs
            .readFileSync(path.join(dir, "bundle.l10n.json"), "utf8")
            .replace(/\r/g, "");
    const stale = bundle(path.join(__dirname, "..", "l10n")) !== bundle(fresh);
    fs.rmSync(fresh, { recursive: true, force: true });
    if (stale) {
        console.error(
            "l10n/bundle.l10n.json is stale: run npm run l10n:export",
        );
        process.exit(1);
    }
    console.log("host strings: every one through l10n");
}
