// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every text the extension host shows a reader goes through `vscode.l10n.t` (or `l10n.t` from
// `@vscode/l10n` in a module that cannot import `vscode`), so it lands in the l10n bundle. This
// walks the syntax trees and finds literals that reach a user-facing API directly: in the host,
// notifications and their buttons, quick picks, input boxes, progress, status bar, tree items and
// panel titles; in the webviews, text between tags and text attributes as well. Run directly, it
// checks all of `src/`, then that the committed bundle matches the code, and fails `npm run lint`.

const fs = require("node:fs");
const path = require("node:path");
const ts = require("typescript");

/**
 * Calls whose every string argument is shown: a notification's message and its buttons, and the
 * status setters that feed the slot a webview's reader watches - the story graph's `setStatus`
 * (the React state setter) and `reportStatus` (the module-level alias that reaches it from outside
 * the component).
 */
const SHOWN_ARGUMENTS = new Set([
    "showInformationMessage",
    "showWarningMessage",
    "showErrorMessage",
    "setStatus",
    "reportStatus",
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

/**
 * Whether an expression is text written in place: a literal, a template, or one joined in - by
 * `+`, by a conditional, or as the fallback of `??` / `||`.
 */
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
        (node.operatorToken.kind === ts.SyntaxKind.PlusToken ||
            node.operatorToken.kind === ts.SyntaxKind.QuestionQuestionToken ||
            node.operatorToken.kind === ts.SyntaxKind.BarBarToken)
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

/** JSX attributes that are shown. `className`, `value`, ids and the like are not text. */
const SHOWN_ATTRIBUTES = new Set([
    "title",
    "label",
    "placeholder",
    "aria-label",
    "alt",
    "info",
    "hint",
    "detail",
    "description",
    // A disabled control's tooltip: the story graph's Save said "nothing is staged" outside every catalogue.
    "disabledReason",
    "caption",
]);

/** The raw texts in one file, each with its line and source text. */
function rawStrings(file, source = fs.readFileSync(file, "utf8")) {
    const tree = ts.createSourceFile(
        file,
        source,
        ts.ScriptTarget.Latest,
        true,
        file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
    );
    const found = [];
    const report = (node) => {
        const {line} = tree.getLineAndCharacterOfPosition(
            node.getStart(tree),
        );
        found.push({line: line + 1, text: node.getText(tree)});
    };

    const visit = (node) => {
        // Text between tags is shown by definition; a whitespace-only run is layout.
        if (ts.isJsxText(node) && hasWords(node.text)) {
            report(node);
        }
        if (
            ts.isJsxAttribute(node) &&
            SHOWN_ATTRIBUTES.has(node.name.getText(tree)) &&
            node.initializer !== undefined
        ) {
            const value = ts.isJsxExpression(node.initializer)
                ? node.initializer.expression
                : node.initializer;
            if (value !== undefined && isRaw(value)) {
                report(value);
            }
        }
        // `{cond ? "A" : "B"}` or a template as a child of an element is shown text too.
        if (
            ts.isJsxExpression(node) &&
            node.expression !== undefined &&
            (ts.isJsxElement(node.parent) || ts.isJsxFragment(node.parent)) &&
            isRaw(node.expression)
        ) {
            report(node.expression);
        }
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

/**
 * Where a catalogue text is a LABEL rather than a message: an option's or an item's name, a
 * tooltip, a button. The text rules treat a full stop there as decoration, so this is what lets
 * them tell `Clear all filters.` from `Saved to {0}.`.
 */
const LABEL_PROPERTIES = new Set([
    "title",
    "label",
    "tooltip",
    "placeHolder",
    "description",
    "detail",
    "text",
]);

/** Climbs past what passes a value through unchanged: brackets, `a ? b : c`, `a ?? b`. */
function valueSite(node) {
    let site = node;
    for (; ;) {
        const parent = site.parent;
        if (
            ts.isParenthesizedExpression(parent) ||
            (ts.isConditionalExpression(parent) && parent.condition !== site) ||
            (ts.isBinaryExpression(parent) &&
                (parent.operatorToken.kind ===
                    ts.SyntaxKind.QuestionQuestionToken ||
                    parent.operatorToken.kind === ts.SyntaxKind.BarBarToken))
        ) {
            site = parent;
            continue;
        }
        return site;
    }
}

function isLabelSite(site, tree) {
    const parent = site.parent;
    if (ts.isPropertyAssignment(parent) && parent.initializer === site) {
        return LABEL_PROPERTIES.has(parent.name.getText(tree));
    }
    if (ts.isJsxExpression(parent) && ts.isJsxAttribute(parent.parent)) {
        return SHOWN_ATTRIBUTES.has(parent.parent.name.getText(tree));
    }
    if (ts.isCallExpression(parent)) {
        const index = parent.arguments.indexOf(site);
        const name = calleeName(parent);
        return (
            (SHOWN_ARGUMENTS.has(name) && index >= 1) ||
            (name === "createWebviewPanel" && index === 1)
        );
    }
    if (ts.isNewExpression(parent)) {
        return (
            /TreeItem$/.test(parent.expression.getText(tree)) &&
            parent.arguments?.[0] === site
        );
    }
    return false;
}

/** Every `l10n.t("...")` in one file, with whether that use is a label. */
function catalogueCalls(file, source = fs.readFileSync(file, "utf8")) {
    const tree = ts.createSourceFile(
        file,
        source,
        ts.ScriptTarget.Latest,
        true,
        file.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
    );
    const found = [];
    const visit = (node) => {
        if (
            ts.isCallExpression(node) &&
            /(^|\.)l10n\.t$/.test(node.expression.getText(tree)) &&
            node.arguments[0] &&
            (ts.isStringLiteral(node.arguments[0]) ||
                ts.isNoSubstitutionTemplateLiteral(node.arguments[0]))
        ) {
            found.push({
                text: node.arguments[0].text,
                label: isLabelSite(valueSite(node), tree),
            });
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
        for (const entry of fs.readdirSync(dir, {withFileTypes: true})) {
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

/** The webviews' own sources, tests excluded. */
function webviewFiles(src) {
    const files = [];
    const walk = (dir) => {
        for (const entry of fs.readdirSync(dir, {withFileTypes: true})) {
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                walk(full);
            } else if (
                /\.tsx?$/.test(entry.name) &&
                !/\.test\.tsx?$/.test(entry.name)
            ) {
                files.push(full);
            }
        }
    };
    walk(src);
    return files.sort();
}

module.exports = {rawStrings, catalogueCalls, hostFiles, webviewFiles};

if (require.main === module) {
    const src = path.join(__dirname, "..", "src");
    let count = 0;
    const files = [
        ...hostFiles(src),
        ...webviewFiles(path.join(src, "webview")),
    ];
    for (const file of files) {
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
    const {execFileSync} = require("node:child_process");
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
        {stdio: "ignore"},
    );
    const bundle = (dir) =>
        fs
            .readFileSync(path.join(dir, "bundle.l10n.json"), "utf8")
            .replace(/\r/g, "");
    const stale = bundle(path.join(__dirname, "..", "l10n")) !== bundle(fresh);
    fs.rmSync(fresh, {recursive: true, force: true});
    if (stale) {
        console.error(
            "l10n/bundle.l10n.json is stale: run npm run l10n:export",
        );
        process.exit(1);
    }
    console.log("host and webview strings: every one through l10n");
}
