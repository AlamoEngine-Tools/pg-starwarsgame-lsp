// What every UI protocol test shares: the reads of the window that the page objects do not offer.
//
// Under the driver:
// - The clipboard needs its permissions granted; then a bug report is copied and read back as a hand
//   run does (copyBugReport).
// - A toast hides into the notification centre after a few seconds, so notifications are read and
//   pressed there.
// - A window reload ends the session; a test that reloads is written in parts (see run.mjs).
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { repo } from "./paths.js";

/** The extension's status bar item as its text is set, icon included: `$(check) EaWEdit LSP`. */
export async function lspStatus(): Promise<string | null> {
    return browser.execute(() => {
        for (const item of Array.from(document.querySelectorAll(".statusbar-item"))) {
            const text = (item.textContent ?? "").trim();
            if (!text.startsWith("EaWEdit LSP")) {
                continue;
            }
            const icon = Array.from(item.querySelector(".codicon")?.classList ?? []).find(
                (c) => c.startsWith("codicon-") && c !== "codicon-modifier-spin",
            );
            return (icon ? `$(${icon.slice("codicon-".length)}) ` : "") + text;
        }
        return null;
    });
}

/** Waits for the single project's server to report ready; the last status seen is in the error. */
export async function serverReady(timeout = 240_000): Promise<void> {
    let last: string | null = null;
    await browser.waitUntil(
        async () => {
            last = await lspStatus();
            return last === "$(check) EaWEdit LSP";
        },
        { timeout, interval: 1000, timeoutMsg: `The server did not get ready; status ${last}` },
    );
}

/** Starts recording every text the EaWEdit status item shows, however briefly. */
export async function recordStatus(): Promise<void> {
    await browser.execute(() => {
        const w = window as any;
        w.__aetStatus = [];
        const read = () => {
            for (const item of Array.from(document.querySelectorAll(".statusbar-item"))) {
                const text = (item.textContent ?? "").trim();
                if (text.startsWith("EaWEdit LSP") && w.__aetStatus.at(-1) !== text) w.__aetStatus.push(text);
            }
        };
        w.__aetStatusObserver?.disconnect();
        w.__aetStatusObserver = new MutationObserver(read);
        w.__aetStatusObserver.observe(document.body, { subtree: true, childList: true, characterData: true });
        read();
    });
}

/** The status texts recorded since recordStatus, in order. */
export async function statusHistory(): Promise<string[]> {
    return (await browser.execute(() => [...((window as any).__aetStatus ?? [])])) as unknown as string[];
}

/** The extension host's log folder of this session in the test's profile. */
function hostLogs(): string {
    const logs = path.join("C:\\temp\\aet-vscode", `${process.env.AET_UI_TEST}-ui`, "settings", "logs");
    const sessions = fs.readdirSync(logs).sort();
    return path.join(logs, sessions[sessions.length - 1], "window1", "exthost");
}

/** The extension's log channels, by name: 'Alamo Engine Tools - Empire at War Edit' and any other. */
export function logChannels(): Map<string, string> {
    const dir = path.join(hostLogs(), "AlamoEngineTools.aet-eaw-edit");
    return new Map(
        (fs.existsSync(dir) ? fs.readdirSync(dir) : [])
            .filter((f) => f.endsWith(".log"))
            .map((f) => [f.slice(0, -".log".length), fs.readFileSync(path.join(dir, f), "utf8")]),
    );
}

/** The plain output channels the window created, by name: 'EaWEdit', 'EaWEdit LSP Trace'. */
export function outputChannels(): Map<string, string> {
    const channels = new Map<string, string>();
    for (const dir of fs.readdirSync(hostLogs()).filter((d) => d.startsWith("output_logging_"))) {
        for (const file of fs.readdirSync(path.join(hostLogs(), dir))) {
            // Written as <n>-<channel name>.log.
            const name = file.replace(/^\d+-/, "").replace(/\.log$/, "");
            channels.set(name, fs.readFileSync(path.join(hostLogs(), dir, file), "utf8"));
        }
    }
    return channels;
}

/** The client's output and the server's, as 'Alamo Engine Tools - Empire at War Edit' shows them. */
export function extensionLog(): string {
    return logChannels().get("Alamo Engine Tools - Empire at War Edit") ?? "";
}

export interface ShownNotification {
    message: string;
    type: "info" | "warning" | "error";
    actions: string[];
}

/** Every notification of the session, toast or not, as the notification centre lists them. */
export async function notifications(): Promise<ShownNotification[]> {
    await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("notifications.showList"));
    await browser.$(".notifications-center").waitForDisplayed({ timeout: 10_000 });
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".notifications-center .notification-list-item")).map((item) => {
            const icon = item.querySelector(".notification-list-item-icon")?.className ?? "";
            return {
                message: (item.querySelector(".notification-list-item-message")?.textContent ?? "").trim(),
                type: /codicon-error/.test(icon) ? "error" : /codicon-warning/.test(icon) ? "warning" : "info",
                actions: Array.from(item.querySelectorAll(".notification-list-item-buttons-container .monaco-button")).map(
                    (b) => (b.textContent ?? "").trim(),
                ),
            } as ShownNotification;
        }),
    );
}

/** Presses a button of the notification showing this message, in the notification centre. */
export async function takeNotificationAction(message: string, action: string): Promise<void> {
    await notifications();
    const pressed = await browser.execute(
        (message: string, action: string) => {
            for (const item of Array.from(document.querySelectorAll(".notifications-center .notification-list-item"))) {
                if ((item.querySelector(".notification-list-item-message")?.textContent ?? "").trim() !== message) {
                    continue;
                }
                for (const button of Array.from(item.querySelectorAll(".notification-list-item-buttons-container .monaco-button"))) {
                    if ((button.textContent ?? "").trim() === action) {
                        (button as HTMLElement).click();
                        return true;
                    }
                }
            }
            return false;
        },
        message,
        action,
    );
    if (!pressed) {
        throw new Error(`No '${action}' on a notification '${message}'`);
    }
}

/** Waits for a notification whose message passes the test; returns it. */
export async function notification(
    test: (message: string) => boolean,
    timeout = 60_000,
): Promise<ShownNotification> {
    let found: ShownNotification | undefined;
    await browser.waitUntil(
        async () => {
            found = (await notifications()).find((n) => test(n.message));
            return found !== undefined;
        },
        { timeout, interval: 1000, timeoutMsg: "No such notification" },
    );
    return found!;
}

/** Clears the notification centre, so what follows is told from what came before. */
export async function clearNotifications(): Promise<void> {
    await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("notifications.clearAll"));
}

/** The offer of a project file for a folder, as the extension words it. */
export function projectOffer(folder: string): string {
    return `EaWEdit: Mod without a project file - ${folder.replace(/^C:/, "c:")}`;
}

/** The server's error for a folder holding no project file. */
export function noProjectError(folder: string): string {
    return `No .pgproj file was found under [${folder.replace(/^C:/, "c:")}]. This folder is not a mod project, so nothing has been indexed and no XML or Lua support is available here.`;
}

/** Each server start's [initialized] line: whether it found a project file, and which - "False <none>". */
export function projectScans(): string[] {
    return extensionLog()
        .split(/\r?\n/)
        .filter((l) => l.includes("[initialized] scanRoots="))
        .map((l) => /\.pgproj present=(\w+) path=(\S+)/.exec(l)?.slice(1).join(" ") ?? l);
}

/** Presses Create project file on the offer and waits for the server to start again on the file. */
export async function createProjectFile(folder: string, file: string): Promise<Record<string, any>> {
    const before = projectScans().length;
    await takeNotificationAction(projectOffer(folder), "Create project file");
    await browser.waitUntil(async () => fs.existsSync(file), { timeout: 30_000, timeoutMsg: `${file} was not written` });
    await browser.waitUntil(async () => projectScans().length > before, {
        timeout: 60_000,
        timeoutMsg: `The server did not start again; starts ${JSON.stringify(projectScans())}`,
    });
    await serverReady();
    expect(projectScans().at(-1)!.toLowerCase()).toBe(`true ${file.toLowerCase()}`);
    return JSON.parse(fs.readFileSync(file, "utf8"));
}

/** How many times the EaWEdit channel says a server was started. */
export function serverStarts(): number {
    return (outputChannels().get("EaWEdit") ?? "").split(/\r?\n/).filter((l) => l.includes("Starting LSP server")).length;
}

/** Restarts the server and waits until it is ready again. */
export async function restartServer(): Promise<void> {
    const before = serverStarts();
    await command("aet-eaw-edit.lsp.restartServer");
    await browser.waitUntil(async () => serverStarts() > before, {
        timeout: 60_000,
        timeoutMsg: "The server was not started again",
    });
    await serverReady();
}

/** The repository's vanilla EaW folder. */
export const eaw = path.join(repo, "eaw");

/** The repository's vanilla FoC folder. */
export const foc = path.join(repo, "foc");

/** The two vanilla projects' names. */
export const EAW = "Empire at War - Vanilla";
export const FOC = "Forces of Corruption - Vanilla";

/** Waits for the status of a window serving several projects to read them all indexed. */
export async function projectsReady(count: number, timeout = 300_000): Promise<void> {
    let status: string | null = null;
    await browser.waitUntil(
        async () => (status = await lspStatus()) === `$(check) EaWEdit LSP: ${count} projects indexed`,
        { timeout, interval: 2000, timeoutMsg: `Status ${status}` },
    );
}

/** The status item's tooltip, as hovering it shows it. */
export async function statusTooltip(): Promise<string> {
    const item = await browser.$("//*[contains(@class,'statusbar-item')][contains(normalize-space(.),'EaWEdit LSP')]");
    await item.moveTo();
    const hover = await browser.$(".workbench-hover, .monaco-hover:not(.hidden)");
    await hover.waitForDisplayed({ timeout: 10_000, timeoutMsg: "No tooltip on the status item" });
    const text = await hover.getText();
    await browser.keys("Escape");
    return text;
}

/** The labels of every open tab, in every group. */
export async function tabLabels(): Promise<string[]> {
    return browser.executeWorkbench((vscode) =>
        vscode.window.tabGroups.all.flatMap((g: any) => g.tabs.map((t: any) => t.label)),
    );
}

/** The label of the active tab. */
export async function activeTab(): Promise<string | undefined> {
    return browser.executeWorkbench((vscode) => vscode.window.tabGroups.activeTabGroup.activeTab?.label);
}

/** The open quick pick: its title, its placeholder and its rows. */
export async function quickPick(): Promise<{ title: string; placeholder: string; rows: { label: string; description: string }[] }> {
    await browser.$(".quick-input-widget .quick-input-box input").waitForDisplayed({ timeout: 15_000 });
    await browser.waitUntil(
        async () => (await browser.execute(() => document.querySelectorAll(".quick-input-widget .monaco-list-row").length)) > 0,
        { timeout: 15_000, timeoutMsg: "The quick pick lists nothing" },
    );
    return browser.execute(() => {
        const widget = document.querySelector(".quick-input-widget")!;
        return {
            title: (widget.querySelector(".quick-input-title")?.textContent ?? "").trim(),
            placeholder: (widget.querySelector(".quick-input-box input") as HTMLInputElement).placeholder,
            rows: Array.from(widget.querySelectorAll(".monaco-list-row")).map((r) => ({
                label: (r.querySelector(".label-name")?.textContent ?? "").trim(),
                description: (r.querySelector(".label-description")?.textContent ?? "").trim(),
            })),
        };
    });
}

/** A modal question (VS Code's own dialog): its message and detail, and its buttons. */
export async function dialog(timeout = 120_000): Promise<{ message: string; detail: string; buttons: string[] }> {
    const box = await browser.$(".monaco-dialog-box");
    await box.waitForDisplayed({ timeout, timeoutMsg: "No dialog appeared" });
    return browser.execute(() => {
        const b = document.querySelector(".monaco-dialog-box")!;
        return {
            message: (b.querySelector(".dialog-message-text")?.textContent ?? "").trim(),
            detail: (b.querySelector(".dialog-message-detail")?.textContent ?? "").trim(),
            buttons: Array.from(b.querySelectorAll(".dialog-buttons .monaco-button")).map((x) => (x.textContent ?? "").trim()),
        };
    });
}

/** Presses a button of the open dialog. */
export async function pressDialogButton(label: string): Promise<void> {
    const button = await browser.$(
        `//*[contains(@class,'monaco-dialog-box')]//*[contains(@class,'monaco-button') and normalize-space(.)='${label}']`,
    );
    await button.click();
}

/** Inserts a line after a 0-based line of a file, unsaved, as typing it would; returns the line before it. */
export async function insertLineAfter(file: string, line: number, text: string): Promise<string> {
    return browser.executeWorkbench(
        async (vscode, file: string, line: number, text: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
            await vscode.window.showTextDocument(document);
            const edit = new vscode.WorkspaceEdit();
            edit.insert(document.uri, new vscode.Position(line + 1, 0), text + "\n");
            await vscode.workspace.applyEdit(edit);
            return document.lineAt(line).text as string;
        },
        file,
        line,
        text,
    );
}

/** Reverts and closes a file. */
export async function revertAndClose(file: string): Promise<void> {
    await browser.executeWorkbench(async (vscode, file: string) => {
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(vscode.Uri.file(file)));
        await vscode.commands.executeCommand("workbench.action.files.revert");
        await vscode.commands.executeCommand("workbench.action.closeActiveEditor");
    }, file);
}

/** Waits for a finding on a 0-based line with a code; returns it with its related rows. */
export async function findingOn(
    file: string,
    line: number,
    code: string,
    timeout = 90_000,
): Promise<Finding & { related: string[] }> {
    let found: (Finding & { related: string[] }) | undefined;
    await browser.waitUntil(
        async () => {
            const all: (Finding & { related: string[] })[] = await browser.executeWorkbench(
                (vscode, file: string) =>
                    vscode.languages.getDiagnostics(vscode.Uri.file(file)).map((d: any) => ({
                        code: String(typeof d.code === "object" ? d.code.value : (d.code ?? "")),
                        message: d.message,
                        source: d.source ?? "",
                        line: d.range.start.line,
                        severity: d.severity,
                        related: (d.relatedInformation ?? []).map((r: any) => r.message),
                    })),
                file,
            );
            const here = all.filter((d) => d.line === line && d.code === code);
            found = here[0];
            return here.length > 0;
        },
        { timeout, interval: 1000, timeoutMsg: `No ${code} on line ${line + 1} of ${file}` },
    );
    return found!;
}

/** The labels a section of the 'Empire at War Edit' view container shows at its top level. */
export async function viewItems(section: string): Promise<string[]> {
    const workbench = await browser.getWorkbench();
    const container = await workbench.getActivityBar().getViewControl("Empire at War Edit");
    const view = await container!.openView();
    const pane = await view.getContent().getSection(section);
    await pane.expand();
    let labels: string[] = [];
    await browser.waitUntil(
        async () => {
            labels = await Promise.all((await pane.getVisibleItems()).map((i: any) => i.getLabel()));
            return labels.length > 0;
        },
        { timeout: 60_000, interval: 1000, timeoutMsg: `'${section}' lists nothing` },
    );
    return labels;
}

/**
 * Clicks an item of a section in the Empire at War Edit container, as a reader does. Several labels are
 * a path: the parents are expanded on the way, as under a project's row when several run.
 */
export async function clickViewItem(section: string, ...labels: string[]): Promise<void> {
    const workbench = await browser.getWorkbench();
    const container = await workbench.getActivityBar().getViewControl("Empire at War Edit");
    const view = await container!.openView();
    const pane = await view.getContent().getSection(section);
    await pane.expand();
    if (labels.length > 1) {
        await browser.waitUntil(async () => (await pane.getVisibleItems()).length > 0, {
            timeout: 60_000,
            timeoutMsg: `'${section}' lists nothing`,
        });
        await (pane as any).openItem(...labels);
        return;
    }
    const [label] = labels;
    let item: any;
    await browser.waitUntil(
        async () => {
            for (const candidate of await pane.getVisibleItems()) {
                if ((await (candidate as any).getLabel()) === label) {
                    item = candidate;
                    return true;
                }
            }
            return false;
        },
        { timeout: 60_000, interval: 1000, timeoutMsg: `'${section}' has no item '${label}'` },
    );
    await item.select();
}

/** Answers an input box with text. */
export async function answerInput(text: string): Promise<void> {
    const input = await browser.$(".quick-input-widget .quick-input-box input");
    await input.waitForDisplayed({ timeout: 15_000 });
    await input.setValue(text);
    await browser.keys("Enter");
}

/** Answers VS Code's own folder dialog (the simple one, a quick input) with a folder. */
export async function answerFolderDialog(folder: string, button: string): Promise<void> {
    const input = await browser.$(".quick-input-widget .quick-input-box input");
    await input.waitForDisplayed({ timeout: 15_000 });
    await input.setValue(folder.endsWith("\\") ? folder : `${folder}\\`);
    await browser.pause(1_500);
    // A folder typed in full is taken with Enter; failing that, its OK button - which carries the
    // command's own label - is clicked as a person clicks it.
    await browser.keys("Enter");
    await browser.pause(2_000);
    if (!(await input.isDisplayed())) return;
    const ok = await browser.$(`//*[contains(@class,'quick-input-widget')]//*[contains(@class,'monaco-button') and normalize-space(.)='${button}']`);
    await ok.waitForClickable({ timeout: 10_000, timeoutMsg: `No '${button}' in the folder dialog` });
    await ok.click();
}

/** Picks an item of the quick pick that is open, by its label. */
export async function pickQuickItem(label: string): Promise<void> {
    const input = await browser.$(".quick-input-widget .quick-input-box input");
    await input.waitForDisplayed({ timeout: 15_000 });
    await browser.waitUntil(
        async () =>
            browser.execute(
                (label: string) =>
                    Array.from(document.querySelectorAll(".quick-input-widget .monaco-list-row .label-name")).some(
                        (l) => (l.textContent ?? "").trim() === label,
                    ),
                label,
            ),
        { timeout: 15_000, timeoutMsg: `No quick pick item '${label}'` },
    );
    await input.setValue(label);
    await browser.keys("Enter");
}

/** EaWEdit: Copy Bug Report Info at a tier, read back from the clipboard. */
export async function copyBugReport(tier: "Basic info" | "Basic + extended stats" = "Basic info"): Promise<string> {
    for (const name of ["clipboard-read", "clipboard-write"])
        await browser.setPermissions({ name: name as PermissionName }, "granted");
    await browser.executeWorkbench((vscode) => vscode.env.clipboard.writeText(""));
    await command("aet-eaw-edit.copyBugReportInfo");
    await pickQuickItem(tier);
    let text = "";
    await browser.waitUntil(
        async () => {
            text = await browser.executeWorkbench((vscode) => vscode.env.clipboard.readText());
            return text.length > 0;
        },
        { timeout: 30_000, timeoutMsg: "No bug report reached the clipboard" },
    );
    return text;
}

/** Runs a command without waiting for it: one that asks something waits on the test's answer. */
export async function command(id: string, ...args: unknown[]): Promise<void> {
    await browser.executeWorkbench(
        (vscode, id: string, args: unknown[]) => {
            void vscode.commands.executeCommand(id, ...args);
        },
        id,
        args,
    );
}

/** Sets user settings through the API, as the settings page does; a key nothing declares cannot be. */
export async function setSettings(values: Record<string, unknown>): Promise<void> {
    await browser.executeWorkbench(async (vscode, values: Record<string, unknown>) => {
        for (const [key, value] of Object.entries(values))
            await vscode.workspace.getConfiguration().update(key, value, vscode.ConfigurationTarget.Global);
    }, values);
}

/** The test profile's user settings file, which takes keys nothing declares. */
export function userSettingsJson(): string {
    return path.join("C:\\temp\\aet-vscode", `${process.env.AET_UI_TEST}-ui`, "settings", "User", "settings.json");
}

/** Writes keys into the user settings file by hand, as a user types them; null removes one. */
export function editUserSettingsJson(values: Record<string, unknown>): void {
    const file = userSettingsJson();
    const settings = JSON.parse(fs.readFileSync(file, "utf8"));
    for (const [key, value] of Object.entries(values)) {
        if (value === null) delete settings[key];
        else settings[key] = value;
    }
    fs.writeFileSync(file, JSON.stringify(settings, null, 4));
}

/**
 * The keys of the user settings file VS Code calls 'Unknown Configuration Setting': those no extension
 * declares. Only this one declares aet-eaw-edit.* keys, and the workbench's own mark is a marker the
 * extension API does not read, so the declaration is asked directly.
 */
export function unknownSettings(): string[] {
    const declared = new Set(
        Object.keys(
            readManifest().contributes.configuration.flatMap((c: any) => Object.keys(c.properties ?? {})).reduce(
                (all: Record<string, true>, key: string) => ({ ...all, [key]: true }),
                {},
            ),
        ),
    );
    const settings = JSON.parse(fs.readFileSync(userSettingsJson(), "utf8"));
    return Object.keys(settings).filter((k) => k.startsWith("aet-eaw-edit.") && !declared.has(k));
}

function readManifest(): any {
    return JSON.parse(fs.readFileSync(path.join(repo, "PG.StarWarsGame.LSP.Client.VSCode", "aet-eaw-edit", "package.json"), "utf8"));
}

/** Opens a file in an editor. */
export async function openFile(file: string): Promise<void> {
    await browser.executeWorkbench(async (vscode, file: string) => {
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(vscode.Uri.file(file)));
    }, file);
}

/** The hover at a position as Markdown; empty when nothing answers. Opens the file first. */
export async function hoverAt(file: string, line: number, character: number): Promise<string> {
    return browser.executeWorkbench(
        async (vscode, file: string, line: number, character: number) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
            await vscode.window.showTextDocument(document);
            const hovers = await vscode.commands.executeCommand(
                "vscode.executeHoverProvider",
                document.uri,
                new vscode.Position(line, character),
            );
            return (hovers ?? [])
                .flatMap((h: any) => h.contents.map((c: any) => (typeof c === "string" ? c : c.value)))
                .join("\n\n---\n\n");
        },
        file,
        line,
        character,
    );
}

/** Waits for a hover that is not empty, while a server starts or indexes. */
export async function hoverWhenReady(file: string, line: number, character: number): Promise<string> {
    let text = "";
    await browser.waitUntil(
        async () => {
            text = await hoverAt(file, line, character);
            return text.length > 0;
        },
        { timeout: 90_000, interval: 2000, timeoutMsg: `No hover at ${file}:${line + 1}:${character + 1}` },
    );
    return text;
}

export interface Finding {
    code: string;
    message: string;
    source: string;
    line: number;
    severity: number;
}

/** The findings VS Code holds for a file. */
export async function findings(file: string): Promise<Finding[]> {
    return browser.executeWorkbench(
        (vscode, file: string) =>
            vscode.languages.getDiagnostics(vscode.Uri.file(file)).map((d: any) => ({
                code: String(typeof d.code === "object" ? d.code.value : (d.code ?? "")),
                message: d.message,
                source: d.source ?? "",
                line: d.range.start.line,
                severity: d.severity,
            })),
        file,
    );
}

/** Waits until a log or output channel holds a line matching the pattern; returns the line. */
export async function logLine(pattern: RegExp, channel = "EaWEdit", timeout = 120_000): Promise<string> {
    let found: string | undefined;
    await browser.waitUntil(
        async () => {
            const text = outputChannels().get(channel) ?? logChannels().get(channel) ?? "";
            found = text.split(/\r?\n/).find((l) => pattern.test(l));
            return found !== undefined;
        },
        { timeout, interval: 1000, timeoutMsg: `No line ${pattern} in '${channel}'` },
    );
    return found!;
}

/** The settings editor's count for a search, e.g. 10 for '10 Settings Found'. */
export async function settingsFound(query: string): Promise<number> {
    // The widget says 'No Settings Found' rather than a count of nought.
    const countOf = (text: string): number =>
        /No Settings Found/i.test(text) ? 0 : Number(/(\d+) Settings? Found/.exec(text)?.[1] ?? -1);
    // A second search in an open editor would read the count of the first one.
    await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("workbench.action.closeAllEditors"));
    await command("workbench.action.openSettings", query);
    let count = -1;
    await browser.waitUntil(
        async () => {
            const [text, search] = (await browser.execute(() => [
                document.querySelector(".settings-count-widget")?.textContent ?? "",
                (document.querySelector(".settings-editor .suggest-input-container .view-lines") as HTMLElement | null)
                    ?.innerText ?? "",
            ])) as [string, string];
            count = countOf(text);
            return count >= 0 && search.replace(/\s+/g, " ").trim() === query;
        },
        { timeout: 20_000, timeoutMsg: `No settings count for '${query}'` },
    );
    // The count follows the search after a debounce.
    await browser.pause(1_500);
    const text: string = await browser.execute(() => document.querySelector(".settings-count-widget")?.textContent ?? "");
    return countOf(text);
}

/** The labels and values the settings editor shows for its current search. */
export async function settingsShown(): Promise<{ label: string; category: string }[]> {
    return browser.execute(() =>
        Array.from(document.querySelectorAll(".settings-editor .setting-item")).map((item) => ({
            label: (item.querySelector(".setting-item-label")?.textContent ?? "").trim(),
            category: (item.querySelector(".setting-item-category")?.textContent ?? "").trim(),
        })),
    );
}
