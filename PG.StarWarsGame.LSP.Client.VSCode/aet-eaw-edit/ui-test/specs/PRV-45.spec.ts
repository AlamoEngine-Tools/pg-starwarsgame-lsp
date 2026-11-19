// PRV-45 (#436): a model and its textures reach the preview as bytes, once. A project of two byte-identical
// models has them converted into its preview cache after the first index, one entry for both; a refresh after
// an XML edit sends no model and no texture again; a texture or a model written over on disk is sent again,
// that one alone; nothing the preview is sent is base64 text for it to decode; and the command empties the
// cache. Before, every refresh sent every model and texture again as base64 text and nothing was kept.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { inNewestWebview } from "./storyGraph.js";

const root = "C:\\temp\\aet-test-PRV-load";
const units = path.join(root, "Data", "XML", "LoadUnits.xml");
const models = path.join(root, "Data", "Art", "Models");
const textures = path.join(root, "Data", "Art", "Textures");
const swap = path.join(root, "swap");
const cache = path.join(root, ".aetswg", "preview");

/** One model or texture message the preview received. */
interface Sent {
    name: string;
    /** `binary` for bytes, `text` for a long string, `none` for a message that carries neither. */
    payload: "binary" | "text" | "none";
}

/** What the preview received since the watch was started or last read. */
interface Received {
    scenes: number;
    models: Sent[];
    textures: Sent[];
    /** Calls of `atob` over a payload-sized text. */
    decoded: number;
}

type Watched = Window & { __prv45?: Received };

/** Every converted model in the project's preview cache: one `.aetpm` entry file each. */
function converted(): string[] {
    if (!fs.existsSync(cache)) {
        return [];
    }
    return (fs.readdirSync(cache, { recursive: true }) as string[]).filter((f) => f.toLowerCase().endsWith(".aetpm"));
}

/** A texture's name without its extension, in lower case: a model names a .tga the game keeps as a .dds. */
function stem(name: string): string {
    return name.toLowerCase().replace(/\.[a-z0-9]+$/, "");
}

/** Waits until the preview has taken `scenes` scenes since the watch started and its cover is gone. Run inside the preview. */
async function loaded(scenes: number): Promise<void> {
    let quiet = 0;
    await browser.waitUntil(
        async () => {
            const done: boolean = await browser.execute(
                (n: number) =>
                    ((window as Watched).__prv45?.scenes ?? 0) >= n &&
                    document.querySelector(".load-cover") === null &&
                    document.querySelector("canvas") !== null,
                scenes,
            );
            quiet = done ? quiet + 1 : 0;
            return quiet >= 3;
        },
        { timeout: 120_000, interval: 1000, timeoutMsg: "The preview did not finish loading" },
    );
}

/** Starts counting what the preview is sent, or empties the count. Run inside the preview. */
async function watch(): Promise<void> {
    await browser.execute(() => {
        const w = window as Watched;
        if (w.__prv45 !== undefined) {
            w.__prv45 = { scenes: 0, models: [], textures: [], decoded: 0 };
            return;
        }
        w.__prv45 = { scenes: 0, models: [], textures: [], decoded: 0 };
        const decode = w.atob.bind(w);
        w.atob = (text: string) => {
            if (text.length > 10_000 && w.__prv45 !== undefined) {
                w.__prv45.decoded++;
            }
            return decode(text);
        };
        w.addEventListener("message", (event: MessageEvent) => {
            const message = event.data as { type?: string; name?: unknown; partId?: unknown } | null;
            const seen = w.__prv45;
            if (message === null || typeof message !== "object" || seen === undefined) {
                return;
            }
            if (message.type === "scene") {
                seen.scenes++;
                return;
            }
            if (message.type !== "glb" && message.type !== "texture") {
                return;
            }
            let payload: "binary" | "text" | "none" = "none";
            const open: [unknown, number][] = [[message, 0]];
            while (open.length > 0) {
                const [value, depth] = open.pop() as [unknown, number];
                if (value instanceof ArrayBuffer || ArrayBuffer.isView(value)) {
                    payload = "binary";
                } else if (typeof value === "string") {
                    if (value.length > 10_000 && payload === "none") {
                        payload = "text";
                    }
                } else if (value !== null && typeof value === "object" && depth < 3) {
                    for (const key of Object.keys(value)) {
                        open.push([(value as Record<string, unknown>)[key], depth + 1]);
                    }
                }
            }
            (message.type === "glb" ? seen.models : seen.textures).push({
                name: String(message.name ?? message.partId ?? ""),
                payload,
            });
        });
    });
}

/** What the preview was sent since the watch, the messages without a payload left out. Run inside the preview. */
async function received(): Promise<Received> {
    const seen: Received = await browser.execute(() => (window as Watched).__prv45 as Received);
    return {
        ...seen,
        models: seen.models.filter((m) => m.payload !== "none"),
        textures: seen.textures.filter((t) => t.payload !== "none"),
    };
}

/** Sets the unit's scale in its file and saves it, without showing the file: the scene changes and the preview refreshes. */
async function rescale(from: string, to: string): Promise<void> {
    await browser.executeWorkbench(
        async (vscode, file: string, from: string, to: string) => {
            const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
            const at = document.getText().indexOf(from);
            const edit = new vscode.WorkspaceEdit();
            edit.replace(document.uri, new vscode.Range(document.positionAt(at), document.positionAt(at + from.length)), to);
            await vscode.workspace.applyEdit(edit);
            await document.save();
        },
        units,
        `<Scale_Factor>${from}</Scale_Factor>`,
        `<Scale_Factor>${to}</Scale_Factor>`,
    );
}

/** Refreshes the open preview by a scale edit and returns what it was sent for it. */
async function refreshed(
    from: string,
    to: string,
    until: (sent: Received) => boolean = () => true,
): Promise<Received> {
    await inNewestWebview(watch);
    await rescale(from, to);
    return inNewestWebview(async () => {
        await loaded(1);
        // A file written over is reported by the file watcher, which may speak after the edit's
        // own refresh has asked: the preview is then told to ask again, and what changed arrives
        // with that second pass.
        await browser.waitUntil(async () => until(await received()), {
            timeout: 60_000,
            interval: 500,
            timeoutMsg: "The preview was not sent what the test waits for",
        });
        await loaded(1);
        return received();
    });
}

/** The two files the test writes over, and the copies the setup kept of them. */
const written: [string, string][] = [
    [path.join(models, "Prv_load.alo"), path.join(swap, "Prv_load.orig.alo")],
    [path.join(textures, "Ev_tie01.dds"), path.join(swap, "Ev_tie01.orig.dds")],
];

describe("PRV-45", () => {
    before(async () => {
        await serverReady();
    });

    // Also after a failure: a fixture left written over draws the X-wing's texture on the TIE
    // fighter for whoever opens it next, which reads as a broken preview.
    after(() => {
        for (const [file, original] of written) {
            fs.copyFileSync(original, file);
        }
    });

    it("converts the project's models after the first index, two byte-identical ones once, into a git-ignored cache", async () => {
        await browser.waitUntil(async () => converted().length > 0, {
            timeout: 120_000,
            interval: 1000,
            timeoutMsg: `No converted model in ${cache}`,
        });
        // Let a second entry appear if one is going to.
        await browser.pause(5000);
        expect(converted()).toHaveLength(1);
        expect(fs.readFileSync(path.join(root, ".aetswg", ".gitignore"), "utf8")).toMatch(/^preview\/$/m);
    });

    it("sends no model and no texture again after an XML edit that changes the scene", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Load_Unit");
        await inNewestWebview(() => loaded(0));

        const sent = await refreshed("1.0", "1.1");
        expect(sent.models).toEqual([]);
        expect(sent.textures).toEqual([]);
    });

    it("sends a texture written over on disk again, that one alone, as bytes", async () => {
        fs.copyFileSync(path.join(swap, "Other.dds"), path.join(textures, "Ev_tie01.dds"));

        // The copy has the length AND the write time of the file it replaces - a copy keeps its
        // source's - so only the watcher's report tells the two apart.
        const sent = await refreshed("1.1", "1.2", (seen) => seen.textures.length > 0);
        // The changed file is on screen after the next load: true before the fix too, and stays true.
        expect(sent.textures.map((t) => stem(t.name))).toContain("ev_tie01");
        expect(sent.textures.map((t) => stem(t.name))).toEqual(["ev_tie01"]);
        expect(sent.textures.map((t) => t.payload)).toEqual(["binary"]);
        expect(sent.models).toEqual([]);
        expect(sent.decoded).toBe(0);
    });

    it("sends a model written over on disk again, as bytes, and converts it", async () => {
        fs.copyFileSync(path.join(swap, "Prv_other.alo"), path.join(models, "Prv_load.alo"));

        const sent = await refreshed("1.2", "1.3", (seen) => seen.models.length > 0);
        expect(sent.models.map((m) => m.payload)).toEqual(["binary"]);
        expect(sent.decoded).toBe(0);
        expect(converted()).toHaveLength(2);
    });

    it("empties the preview cache on the command", async () => {
        await browser.executeWorkbench((vscode) => vscode.commands.executeCommand("aet-eaw-edit.lsp.clearPreviewCache"));
        await browser.waitUntil(async () => converted().length === 0, {
            timeout: 30_000,
            timeoutMsg: `The command left converted models in ${cache}`,
        });
    });

    it("leaves the fixture as the setup wrote it", () => {
        for (const [file, original] of written) {
            fs.copyFileSync(original, file);
        }

        expect(fs.readFileSync(path.join(models, "Prv_load.alo")).equals(fs.readFileSync(path.join(models, "Prv_load_twin.alo")))).toBe(true);
        expect(fs.readFileSync(path.join(textures, "Ev_tie01.dds")).equals(fs.readFileSync(path.join(swap, "Ev_tie01.orig.dds")))).toBe(true);
        expect(fs.readFileSync(path.join(textures, "Ev_tie01.dds")).equals(fs.readFileSync(path.join(swap, "Other.dds")))).toBe(false);
    });
});
