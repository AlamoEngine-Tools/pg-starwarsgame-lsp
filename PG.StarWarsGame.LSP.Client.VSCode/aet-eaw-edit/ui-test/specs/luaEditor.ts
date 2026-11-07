// The Lua editor as the LUA tests drive it: every answer asked of the extension host the way the editor asks it.
// A position is a 1-based line and the text there (its nth occurrence, 1-based), placed on its second
// character so it sits inside the word.
import fs from "node:fs";
import path from "node:path";
import { browser } from "@wdio/globals";
import { eaw } from "../protocol.js";

/** A script under the EaW workspace's Data\Scripts. */
export function script(...parts: string[]): string {
    return path.join(eaw, "Data", "Scripts", ...parts);
}

/** A hover card's text without its markdown: bold, code spans and headings marks gone. */
export function plain(cards: string[]): string {
    return cards.join("\n").replace(/[*`#]/g, "").replace(/[ \t]+/g, " ");
}

/** Deletes protocol fixture scripts the setup wrote into the EaW workspace. */
export function removeScripts(...files: string[]): void {
    for (const file of files) fs.rmSync(file, { force: true });
}

export interface At {
    line: number;
    text: string;
    nth?: number;
    /** Offset into the text; 1 by default. */
    offset?: number;
}

/** Runs a provider command at a position and hands its answer to `shape`, all in the extension host. */
async function ask<T>(file: string, at: At, command: string, shape: string, extra: unknown[] = []): Promise<T> {
    return browser.executeWorkbench(
        async (vscode, target: string, at: At, command: string, shape: string, extra: unknown[]) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
            await vscode.window.showTextDocument(document, { preview: false });
            const lineText: string = document.lineAt(at.line - 1).text;
            let column = -1;
            for (let i = 0; i < (at.nth ?? 1); i++) column = lineText.indexOf(at.text, column + 1);
            if (column < 0) throw new Error(`'${at.text}' is not on line ${at.line}: ${lineText}`);
            const position = new vscode.Position(at.line - 1, column + (at.offset ?? 1));
            const result = await vscode.commands.executeCommand(command, document.uri, position, ...extra);
            return new Function("result", "vscode", shape)(result, vscode);
        },
        file,
        at,
        command,
        shape,
        extra,
    ) as Promise<T>;
}

/** The hover cards at a position, each as its markdown text. */
export async function hover(file: string, at: At): Promise<string[]> {
    return ask(file, at, "vscode.executeHoverProvider", `
        return (result ?? []).map((h) => h.contents.map((c) => (typeof c === "string" ? c : c.value)).join("\\n"));`);
}

/** Waits for a hover that passes the test (the analyzer answers once it has read the workspace). */
export async function hoverWhen(file: string, at: At, test: (cards: string[]) => boolean, timeout = 120_000): Promise<string[]> {
    let cards: string[] = [];
    await browser.waitUntil(async () => test((cards = await hover(file, at))), {
        timeout,
        interval: 2_000,
        timeoutMsg: `No such hover on '${at.text}' line ${at.line}: ${cards.join(" || ")}`,
    });
    return cards;
}

export interface Completion {
    label: string;
    detail: string;
}

/** The completion items at a position (the offset puts the caret, e.g. between quotes). */
export async function completions(file: string, at: At): Promise<Completion[]> {
    return ask(file, at, "vscode.executeCompletionItemProvider", `
        return (result?.items ?? []).map((i) => ({
            label: typeof i.label === "string" ? i.label : i.label.label,
            detail: i.detail ?? (typeof i.label === "string" ? "" : i.label.description ?? ""),
        }));`);
}

/** The locations a definition request answers, as 'file:line'. */
export async function definition(file: string, at: At): Promise<string[]> {
    return ask(file, at, "vscode.executeDefinitionProvider", `
        return (result ?? []).map((l) => {
            const uri = l.targetUri ?? l.uri;
            const range = l.targetSelectionRange ?? l.targetRange ?? l.range;
            return uri.fsPath + ":" + (range.start.line + 1);
        });`);
}

/** The locations a references request answers, as 'file:line'. */
export async function references(file: string, at: At): Promise<string[]> {
    return ask(file, at, "vscode.executeReferenceProvider", `
        return (result ?? []).map((l) => l.uri.fsPath + ":" + (l.range.start.line + 1));`);
}

/** The edits a rename would make, as 'file:line', or the refusal's message. */
export async function rename(file: string, at: At, to: string): Promise<{ edits: string[]; refused?: string }> {
    try {
        return await ask(file, at, "vscode.executeDocumentRenameProvider", `
            if (!result) return { edits: [] };
            return { edits: result.entries().flatMap(([uri, edits]) => edits.map((e) => uri.fsPath + ":" + (e.range.start.line + 1))) };`,
            [to]);
    } catch (e) {
        return { edits: [], refused: String(e) };
    }
}

/** Whether a rename may start at a position; the refusal's message when not. */
export async function prepareRename(file: string, at: At): Promise<string | null> {
    try {
        await ask(file, at, "vscode.prepareRename", "return result;");
        return null;
    } catch (e) {
        return String(e);
    }
}

/** The signature help at a position: its active signature's label and parameters. */
export async function signatureHelp(file: string, at: At): Promise<{ label: string; parameters: string[] } | null> {
    return ask(file, at, "vscode.executeSignatureHelpProvider", `
        const s = result?.signatures?.[result.activeSignature ?? 0];
        return s ? { label: s.label, parameters: (s.parameters ?? []).map((p) => typeof p.label === "string" ? p.label : s.label.slice(p.label[0], p.label[1])) } : null;`);
}

/** Inserts a line after a line (1-based) in the editor, unsaved; returns the new line's number. */
export async function insertLine(file: string, after: number, text: string): Promise<number> {
    await browser.executeWorkbench(
        async (vscode, target: string, after: number, text: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
            await vscode.window.showTextDocument(document, { preview: false });
            const edit = new vscode.WorkspaceEdit();
            edit.insert(document.uri, new vscode.Position(after, 0), text + "\n");
            await vscode.workspace.applyEdit(edit);
        },
        file,
        after,
        text,
    );
    return after + 1;
}

/** Reverts the file in the editor to what is on disk. */
export async function revert(file: string): Promise<void> {
    await browser.executeWorkbench(async (vscode, target: string) => {
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(vscode.Uri.file(target)), { preview: false });
        await vscode.commands.executeCommand("workbench.action.files.revert");
    }, file);
}

/** The document's whole-file answers: symbols, folding ranges, inlay hints, semantic tokens at a position. */
export async function documentSymbols(file: string): Promise<string[]> {
    return browser.executeWorkbench(async (vscode, target: string) => {
        const uri = vscode.Uri.file(target);
        await vscode.workspace.openTextDocument(uri);
        // A stack, not a named inner function: the bundler's name helper does not reach the extension host.
        const pending: any[] = [...((await vscode.commands.executeCommand("vscode.executeDocumentSymbolProvider", uri)) ?? [])];
        const flat: string[] = [];
        while (pending.length > 0) {
            const s = pending.shift();
            flat.push(s.name);
            pending.push(...(s.children ?? []));
        }
        return flat;
    }, file);
}

export async function foldingRanges(file: string): Promise<{ start: number; end: number }[]> {
    return browser.executeWorkbench(async (vscode, target: string) => {
        const uri = vscode.Uri.file(target);
        await vscode.workspace.openTextDocument(uri);
        const ranges: any[] = (await vscode.commands.executeCommand("vscode.executeFoldingRangeProvider", uri)) ?? [];
        return ranges.map((r) => ({ start: r.start + 1, end: r.end + 1 }));
    }, file);
}

export async function inlayHints(file: string, fromLine: number, toLine: number): Promise<{ line: number; label: string }[]> {
    return browser.executeWorkbench(
        async (vscode, target: string, fromLine: number, toLine: number) => {
            const uri = vscode.Uri.file(target);
            await vscode.workspace.openTextDocument(uri);
            const hints: any[] =
                (await vscode.commands.executeCommand(
                    "vscode.executeInlayHintProvider",
                    uri,
                    new vscode.Range(fromLine - 1, 0, toLine, 0),
                )) ?? [];
            return hints.map((h) => ({
                line: h.position.line + 1,
                label: typeof h.label === "string" ? h.label : h.label.map((p: any) => p.value).join(""),
            }));
        },
        file,
        fromLine,
        toLine,
    );
}

/** The semantic token type at a position, read from the provider's legend. */
export async function semanticTokenAt(file: string, at: At): Promise<string | null> {
    return browser.executeWorkbench(
        async (vscode, target: string, at: At) => {
            const uri = vscode.Uri.file(target);
            const document = await vscode.workspace.openTextDocument(uri);
            const legend: any = await vscode.commands.executeCommand("vscode.provideDocumentSemanticTokensLegend", uri);
            const tokens: any = await vscode.commands.executeCommand("vscode.provideDocumentSemanticTokens", uri);
            if (!legend || !tokens) return null;
            const column = document.lineAt(at.line - 1).text.indexOf(at.text);
            const data: number[] = Array.from(tokens.data);
            let line = 0;
            let char = 0;
            for (let i = 0; i < data.length; i += 5) {
                line += data[i];
                char = data[i] === 0 ? char + data[i + 1] : data[i + 1];
                if (line === at.line - 1 && column >= char && column < char + data[i + 2]) return legend.tokenTypes[data[i + 3]];
            }
            return null;
        },
        file,
        at,
    );
}
