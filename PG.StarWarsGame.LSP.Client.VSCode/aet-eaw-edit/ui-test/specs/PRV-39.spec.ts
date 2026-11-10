// PRV-39 (#389): a turret's rest pose composes with the pose the model authored - the way the game applies a
// turret's user transform on top of the bone it computed - rather than replacing it. The Gargantuan's front
// turret model authors its turret bone a quarter turn off its parent; the assembled unit has to show that bone
// exactly as the bare model shows it, since this hardpoint declares no rotation offset. 0.5.0 wrote the rest
// angle straight into the bone's Z rotation, which turned every such turret by its authored yaw.
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { command, foc, serverReady } from "../protocol.js";
import { openInPreview } from "./preview.js";
import { closeAll, inNewestWebview } from "./storyGraph.js";

const bone = "b_turret_front";

interface Quaternion {
    x: number;
    y: number;
    z: number;
    w: number;
}

type Handle = {
    parts: Map<string, { bones: Map<string, { quaternion: Quaternion }> }>;
};

/** The turret bone's local rotation, read off whichever loaded part carries it. */
async function turretPose(): Promise<Quaternion> {
    await browser.waitUntil(
        async () =>
            browser.execute((name: string) => {
                const v = (window as unknown as { aetDebugViewport?: Handle })
                    .aetDebugViewport;
                if (v === undefined) return false;
                for (const part of v.parts.values())
                    if (part.bones.has(name)) return true;
                return false;
            }, bone),
        { timeout: 120_000, timeoutMsg: `No loaded part carries ${bone}` },
    );
    // The rest pose is applied by the shell's effect after the part arrived; give it a moment to run.
    await browser.pause(1_500);
    return browser.execute((name: string) => {
        const v = (window as unknown as { aetDebugViewport: Handle })
            .aetDebugViewport;
        for (const part of v.parts.values()) {
            const found = part.bones.get(name);
            if (found !== undefined) {
                const q = found.quaternion;
                return { x: q.x, y: q.y, z: q.z, w: q.w };
            }
        }
        throw new Error(`${name} is no longer loaded`);
    }, bone);
}

/** The rotation from one pose to the other, in degrees. */
function angleBetween(a: Quaternion, b: Quaternion): number {
    const dot = Math.abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w);
    return (2 * Math.acos(Math.min(1, dot)) * 180) / Math.PI;
}

describe("PRV-39", () => {
    before(async () => {
        await serverReady();
    });

    it("poses the assembled unit's turret as the model authored it", async () => {
        await openInPreview(
            path.join(foc, "Data", "Art", "Models", "Hp_turret_front_00.alo"),
        );
        const authored = await inNewestWebview(turretPose);
        await closeAll();
        await browser.pause(2_000);

        await command(
            "aet-eaw-edit.lsp.previewAssembledUnit",
            "Gargantuan_Battle_Platform",
        );
        const assembled = await inNewestWebview(turretPose);
        console.log(
            `PRV-39 authored ${JSON.stringify(authored)} assembled ${JSON.stringify(assembled)}`,
        );

        // The authored pose is a quarter turn off identity; that is what makes the comparison mean anything.
        expect(
            angleBetween(authored, { x: 0, y: 0, z: 0, w: 1 }),
        ).toBeGreaterThan(45);
        // Rotation offset 0 on this hardpoint, so the rest pose IS the authored pose.
        expect(angleBetween(assembled, authored)).toBeLessThan(0.5);
    });

    after(async () => {
        await closeAll();
    });
});
