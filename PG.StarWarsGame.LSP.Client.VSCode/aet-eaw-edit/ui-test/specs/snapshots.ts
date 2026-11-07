// PRJ-21's index snapshots, read between its parts: each part is a window of its own, so what one saw is kept in
// a file under the test's profile folder for the next.
import fs from "node:fs";
import path from "node:path";
import { localValues } from "../paths.js";

const mod = localValues<{ mod: string; core: string; other: string; snapshotPrefix: string }>("PRJ-21");
export const coreFolder = path.join(mod.mod, mod.core);
const indices = path.join(coreFolder, ".aetswg", "indices");
const kept = path.join("C:\\temp\\aet-vscode", "PRJ-21-ui", "snapshots.json");

/** The core's snapshot files and when each was last written. */
export function snapshots(): Record<string, number> {
    if (!fs.existsSync(indices)) return {};
    return Object.fromEntries(
        fs
            .readdirSync(indices)
            .filter((f) => f.startsWith(mod.snapshotPrefix + ".") && f.endsWith(".msgpack"))
            .map((f) => [f, fs.statSync(path.join(indices, f)).mtimeMs]),
    );
}

export function keep(part: string, seen: Record<string, number>): void {
    const all = fs.existsSync(kept) ? JSON.parse(fs.readFileSync(kept, "utf8")) : {};
    all[part] = seen;
    fs.writeFileSync(kept, JSON.stringify(all));
}

export function kept_(part: string): Record<string, number> {
    return JSON.parse(fs.readFileSync(kept, "utf8"))[part];
}
