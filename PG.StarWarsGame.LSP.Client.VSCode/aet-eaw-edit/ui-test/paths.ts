// Where the protocol lives and what the launcher prepared for a test. Node only: the config reads
// it before any session exists.
import fs from "node:fs";
import path from "node:path";

export const client = path.resolve(import.meta.dirname, "..");
export const repo = path.resolve(client, "..", "..");
export const protocolDir = path.join(repo, "test-protocol", "0.5.0");

/** The settings Start-ProtocolTest.ps1 -DryRun wrote for the test's profile. */
export function userSettingsFile(id: string): string {
    return path.join("C:\\temp\\aet-vscode", id, "User", "settings.json");
}

export function readJson(file: string): any {
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^\uFEFF/, ""));
}

/**
 * A test that opens its folder by hand in the protocol (File > Open Folder) is started on it here:
 * opening a folder reloads the window, which ends the session.
 */
const OPENED_BY_THE_TEST: Record<string, string> = {
    "SET-21": "C:\\temp\\aet-test-SET-21\\AdoptMod",
};

export function uiOpen(id: string, launch: { open?: string | null }): string | null {
    const open = OPENED_BY_THE_TEST[id] ?? launch.open ?? null;
    return open === null ? null : open.replace("{repo}", repo);
}
