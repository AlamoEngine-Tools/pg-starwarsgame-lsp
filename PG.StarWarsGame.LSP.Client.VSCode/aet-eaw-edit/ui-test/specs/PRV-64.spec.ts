// PRV-64 (#442): once a unit is drawn, the preview holds one copy of each model. The fixture's unit is the
// base game's corvette with two hardpoints that attach the same model file again: three parts of one model.
// Before, every part was read from the model's bytes on its own, so the vertex data was held once per part,
// the bytes were kept beside it for a refresh, and a refresh read all of it again.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { command, serverReady } from "../protocol.js";
import { replaceInFile } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const units = path.join("C:\\temp\\aet-test-PRV-shared", "Data", "XML", "SharedUnits.xml");
/** The hardpoints the unit lists, which attach the hull's own model, and two that attach another. */
const LISTED = "<HardPoints>HP_Prv_Shared_A, HP_Prv_Shared_B</HardPoints>";
const SWAPPED = "<HardPoints>HP_Prv_Shared_C, HP_Prv_Shared_D</HardPoints>";

/** The vertex data one part draws from. */
interface PartData {
    id: string;
    meshes: number;
    /** The buffers behind the part's vertex positions, each by the number it was first seen under. */
    buffers: number[];
}

/** What the unit on screen draws from. */
interface Held {
    scenes: number;
    /** Model messages that carried bytes since the watch started. */
    modelsSent: number;
    parts: PartData[];
    /** Buffers seen for the first time in this reading. */
    fresh: number;
}

type Watched = Window & {
    __prv64?: { scenes: number; modelsSent: number; buffers: Map<ArrayBuffer, number> };
};

/** Starts counting the scenes and the model bytes the preview is sent. Run inside the preview. */
async function watch(): Promise<void> {
    await browser.execute(() => {
        const w = window as Watched;
        if (w.__prv64 !== undefined) {
            w.__prv64.scenes = 0;
            w.__prv64.modelsSent = 0;
            return;
        }
        const seen = { scenes: 0, modelsSent: 0, buffers: new Map<ArrayBuffer, number>() };
        w.__prv64 = seen;
        w.addEventListener("message", (event: MessageEvent) => {
            const message = event.data as { type?: string; data?: unknown } | null;
            if (message === null || typeof message !== "object") {
                return;
            }
            if (message.type === "scene") {
                seen.scenes++;
            } else if (message.type === "glb" && ArrayBuffer.isView(message.data)) {
                seen.modelsSent++;
            }
        });
    });
}

/**
 * The parts on screen and the vertex data each draws from, once `parts` of them are loaded, the cover is gone
 * and `scenes` scenes arrived since the watch started. A part's meshes are the ones under its root that belong
 * to no other part. Run inside the preview.
 */
async function held(parts: number, scenes: number): Promise<Held> {
    let quiet = 0;
    await browser.waitUntil(
        async () => {
            const done: boolean = await browser.execute(
                (wanted: number, n: number) => {
                    const v = (window as unknown as { aetDebugViewport?: { parts: Map<string, unknown> } })
                        .aetDebugViewport;
                    return (
                        v !== undefined &&
                        v.parts.size === wanted &&
                        ((window as Watched).__prv64?.scenes ?? 0) >= n &&
                        document.querySelector(".load-cover") === null
                    );
                },
                parts,
                scenes,
            );
            quiet = done ? quiet + 1 : 0;
            return quiet >= 3;
        },
        { timeout: 120_000, interval: 1000, timeoutMsg: `The preview did not finish loading ${parts} parts` },
    );
    return browser.execute(() => {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const v = (window as unknown as { aetDebugViewport: any }).aetDebugViewport;
        const seen = (window as Watched).__prv64!;
        const roots = new Set<unknown>();
        for (const part of v.parts.values()) {
            roots.add(part.root);
        }
        let fresh = 0;
        const read: PartData[] = [];
        for (const part of v.parts.values()) {
            const buffers: number[] = [];
            let meshes = 0;
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const open: any[] = [part.root];
            while (open.length > 0) {
                const node = open.pop();
                for (const child of node.children) {
                    if (!roots.has(child)) {
                        open.push(child);
                    }
                }
                const position = node.isMesh === true ? node.geometry?.attributes?.position : undefined;
                if (position === undefined || node.userData?.alamo === undefined) {
                    continue;
                }
                meshes++;
                const buffer: ArrayBuffer = (position.isInterleavedBufferAttribute === true ? position.data.array : position.array)
                    .buffer;
                if (!seen.buffers.has(buffer)) {
                    seen.buffers.set(buffer, seen.buffers.size);
                    fresh++;
                }
                buffers.push(seen.buffers.get(buffer)!);
            }
            read.push({ id: String(part.id), meshes, buffers: [...new Set(buffers)].sort((a, b) => a - b) });
        }
        return { scenes: seen.scenes, modelsSent: seen.modelsSent, parts: read, fresh };
    });
}

describe("PRV-64", () => {
    before(async () => {
        await serverReady();
        await closeAll();
    });

    it("draws every part of one model from the same vertex data", async () => {
        await command("aet-eaw-edit.lsp.previewAssembledUnit", "Prv_Shared_Unit");
        const now = await inNewestWebview(async () => {
            await watch();
            return held(3, 0);
        });
        console.log("PRV-64 loaded: " + JSON.stringify(now));

        expect(now.parts).toHaveLength(3);
        for (const part of now.parts) {
            expect(part.meshes).toBeGreaterThan(0);
            expect(part.buffers).toEqual(now.parts[0].buffers);
        }
        // Never once per part. The watch starts with the preview already open, so the one sending may
        // have gone by unseen.
        expect(now.modelsSent).toBeLessThanOrEqual(1);
    });

    it("draws the unit again after an XML edit from the model it holds", async () => {
        await inNewestWebview(watch);
        await replaceInFile(units, "<Scale_Factor>1.0</Scale_Factor>", "<Scale_Factor>1.1</Scale_Factor>");
        const now = await inNewestWebview(() => held(3, 1));
        console.log("PRV-64 refreshed: " + JSON.stringify(now));

        expect(now.modelsSent).toBe(0);
        expect(now.parts).toHaveLength(3);
        for (const part of now.parts) {
            expect(part.meshes).toBeGreaterThan(0);
            expect(part.buffers).toEqual(now.parts[0].buffers);
        }
        // The vertex data on screen is the data held before the edit, not a second reading of it.
        expect(now.fresh).toBe(0);
    });

    // Every part of a scene asks before any reply is in. Two hardpoints of a model the preview does not hold
    // yet used to be sent it once each.
    it("is sent a model that two parts name once", async () => {
        await inNewestWebview(watch);
        await replaceInFile(units, LISTED, SWAPPED);
        const now = await inNewestWebview(() => held(3, 1));
        console.log("PRV-64 swapped: " + JSON.stringify(now));

        expect(now.parts).toHaveLength(3);
        for (const part of now.parts) {
            expect(part.meshes).toBeGreaterThan(0);
        }
        // The hull's own vertex data, and one more set for both hardpoints.
        expect(new Set(now.parts.map((part) => JSON.stringify(part.buffers))).size).toBe(2);
        expect(now.modelsSent).toBe(1);
    });

    after(async () => {
        await replaceInFile(units, SWAPPED, LISTED);
        await replaceInFile(units, "<Scale_Factor>1.1</Scale_Factor>", "<Scale_Factor>1.0</Scale_Factor>");
    });
});
