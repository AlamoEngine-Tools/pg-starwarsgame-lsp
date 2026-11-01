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
 * The folder the launcher opens for a test, its placeholders filled: {repo}, {mod}, {modLeaf}, and
 * {local.<ID>.<key>} - a folder under the mod that local.json names for a test.
 */
export function uiOpen(launch: { open?: string | null }): string | null {
    const open = launch.open ?? null;
    if (open === null) {
        return null;
    }
    if (!/\{mod(Leaf)?\}|\{local\./.test(open)) {
        return open.replace("{repo}", repo);
    }
    // The multi-project mod's whereabouts, written by its owner; Start-ProtocolTest.ps1 -DryRun
    // has already refused a test that needs it when the file is missing.
    const local = readJson(path.join(protocolDir, "local.json"));
    return open
        .replace(/\{local\.([A-Z]+-\d+)\.(\w+)\}/g, (_, id: string, key: string) => path.join(local.mod, local[id][key]))
        .replace("{modLeaf}", local.modLeaf)
        .replace("{mod}", local.mod);
}

/**
 * What a mod-bound test expects of the mod - its projects' names, files, lines and counts - as the
 * owner wrote it into the git-ignored local.json under the test's id. Nothing about a third-party mod
 * is written into a spec.
 */
export function localValues<T = Record<string, any>>(id: string): T {
    const local = readJson(path.join(protocolDir, "local.json"));
    if (!local[id]) {
        throw new Error(`test-protocol/0.5.0/local.json has no "${id}" entry for the mod-bound test.`);
    }
    return { ...local[id], mod: local.mod, modLeaf: local.modLeaf } as T;
}
