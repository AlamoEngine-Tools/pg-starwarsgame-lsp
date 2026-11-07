// The XML editor as the XML tests drive it: diagnostics, quick fixes, formatting and code lenses, asked of the
// extension host the way the editor asks them. Lines are 1-based, as the protocol names them.
import { browser } from "@wdio/globals";

export interface Diagnostic {
    code: string;
    line: number;
    message: string;
    severity: number;
}

/** The diagnostics VS Code holds for a file, opening it first. */
export async function diagnostics(file: string): Promise<Diagnostic[]> {
    return browser.executeWorkbench(async (vscode, target: string) => {
        const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
        await vscode.window.showTextDocument(document, { preview: false });
        return vscode.languages.getDiagnostics(document.uri).map((d: any) => ({
            code: String(typeof d.code === "object" ? d.code.value : (d.code ?? "")),
            line: d.range.start.line + 1,
            message: d.message as string,
            severity: d.severity as number,
        }));
    }, file);
}

/** Waits until the file's diagnostics pass the test; returns them. */
export async function diagnosticsWhen(
    file: string,
    test: (all: Diagnostic[]) => boolean,
    timeout = 120_000,
    timeoutMsg = "The diagnostics never got there",
): Promise<Diagnostic[]> {
    let all: Diagnostic[] = [];
    await browser.waitUntil(async () => test((all = await diagnostics(file))), { timeout, interval: 1_000, timeoutMsg });
    return all;
}

/** The quick fixes offered at a line's first occurrence of a text: title and whether it is preferred. */
export async function quickFixes(file: string, line: number, at: string): Promise<{ title: string; preferred: boolean }[]> {
    return browser.executeWorkbench(
        async (vscode, target: string, line: number, at: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
            const column = document.lineAt(line - 1).text.indexOf(at);
            const range = new vscode.Range(line - 1, column, line - 1, column + at.length);
            const actions: any[] =
                (await vscode.commands.executeCommand("vscode.executeCodeActionProvider", document.uri, range)) ?? [];
            return actions.map((a) => ({ title: a.title as string, preferred: a.isPreferred === true }));
        },
        file,
        line,
        at,
    );
}

/**
 * Applies a quick fix by its title, as choosing it does: its edit, then its command - not awaited, since a
 * command may open a picker the test answers next.
 */
export async function applyQuickFix(file: string, line: number, at: string, title: string): Promise<void> {
    const applied: boolean = await browser.executeWorkbench(
        async (vscode, target: string, line: number, at: string, title: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
            await vscode.window.showTextDocument(document, { preview: false });
            const column = document.lineAt(line - 1).text.indexOf(at);
            const range = new vscode.Range(line - 1, column, line - 1, column + at.length);
            const actions: any[] =
                (await vscode.commands.executeCommand("vscode.executeCodeActionProvider", document.uri, range)) ?? [];
            const action = actions.find((a) => a.title === title);
            if (!action) return false;
            if (action.edit) await vscode.workspace.applyEdit(action.edit);
            if (action.command) void vscode.commands.executeCommand(action.command.command, ...(action.command.arguments ?? []));
            return true;
        },
        file,
        line,
        at,
        title,
    );
    if (!applied) throw new Error(`No quick fix '${title}' at '${at}' on line ${line} of ${file}`);
}

/** Runs Format Document on the file, in front, as Shift+Alt+F does. */
export async function formatDocument(file: string): Promise<void> {
    await browser.executeWorkbench(async (vscode, target: string) => {
        const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
        await vscode.window.showTextDocument(document, { preview: false });
        await vscode.commands.executeCommand("editor.action.formatDocument");
    }, file);
}

/** A file's text as the editor holds it, lines 1-based. */
export async function lineText(file: string, line: number): Promise<string> {
    return browser.executeWorkbench(async (vscode, target: string, line: number) => {
        const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
        return document.lineAt(line - 1).text as string;
    }, file, line);
}

/** Replaces one line of the file in the editor, unsaved. */
export async function replaceLine(file: string, line: number, text: string): Promise<void> {
    await browser.executeWorkbench(
        async (vscode, target: string, line: number, text: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
            await vscode.window.showTextDocument(document, { preview: false });
            const edit = new vscode.WorkspaceEdit();
            edit.replace(document.uri, document.lineAt(line - 1).range, text);
            await vscode.workspace.applyEdit(edit);
        },
        file,
        line,
        text,
    );
}

/** Saves the file. */
export async function saveFile(file: string): Promise<void> {
    await browser.executeWorkbench(async (vscode, target: string) => {
        const document = await vscode.workspace.openTextDocument(vscode.Uri.file(target));
        await document.save();
    }, file);
}

export interface Lens {
    line: number;
    title: string;
    command: string;
    locations: number;
}

/** The code lenses of a file, resolved, with how many locations a references lens carries. */
export async function codeLenses(file: string): Promise<Lens[]> {
    return browser.executeWorkbench(async (vscode, target: string) => {
        const uri = vscode.Uri.file(target);
        await vscode.workspace.openTextDocument(uri);
        const lenses: any[] = (await vscode.commands.executeCommand("vscode.executeCodeLensProvider", uri, 1000)) ?? [];
        return lenses.map((l) => ({
            line: l.range.start.line + 1,
            title: (l.command?.title ?? "") as string,
            command: (l.command?.command ?? "") as string,
            locations: Array.isArray(l.command?.arguments?.[2]) ? l.command.arguments[2].length : -1,
        }));
    }, file);
}
