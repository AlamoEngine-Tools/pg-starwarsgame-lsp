// What every UI protocol test shares: the reads of the window that the page objects do not offer.
//
// Two things a hand run reads are not readable here, and the tests read the same fact elsewhere:
// - The clipboard. A write from the extension host never reaches it under the driver, so a bug
//   report cannot be copied back; its project line is the server's status, which the server's own
//   log states at [initialized].
// - A toast after a few seconds. VS Code hides it into the notification centre, so notifications
//   are read and pressed there.
import fs from "node:fs";
import path from "node:path";
import { browser } from "@wdio/globals";

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
