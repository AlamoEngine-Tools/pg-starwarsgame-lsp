// PRJ-04: two projects in one window - two servers, a counted status, a channel and a trace each.
import * as path from "node:path";
import { expect } from "@wdio/globals";
import { EAW, FOC, logChannels, outputChannels, projectsReady, statusTooltip } from "../protocol.js";

describe("PRJ-04", () => {
    before(async () => {
        await projectsReady(2);
    });

    it("lists both projects ready in the tooltip", async () => {
        const tooltip = await statusTooltip();
        expect(tooltip).toContain(`${EAW} - ready`);
        expect(tooltip).toContain(`${FOC} - ready`);
    });

    // Every server logs into the workspace's one folder in the user's own store, never into a project;
    // the project's name in the file name keeps the two servers' files apart. The channel names the home
    // directory ~.
    it("logs the projects and starts each server on the workspace's log folder, under its project's name", () => {
        const log = outputChannels().get("EaWEdit") ?? "";
        expect(log).toContain(`Projects: ${EAW}, ${FOC}`);
        const starts = log.split(/\r?\n/).filter((l) => l.includes("Starting LSP server:"));
        const logArgs = (name: string) => {
            const line = starts.find((l) => l.includes(`[${name}] Starting LSP server:`))!;
            const [directory, stem] = line.split("--log-dir=")[1].split(" --log-stem=");
            return { directory: directory.toLowerCase(), stem: stem.split(" --")[0] };
        };
        const store = `~${path.sep}.aetswg${path.sep}logs${path.sep}`;
        expect(logArgs(EAW).directory.startsWith(store)).toBe(true);
        expect(logArgs(FOC).directory).toBe(logArgs(EAW).directory);
        expect(logArgs(EAW).stem).toBe(EAW);
        expect(logArgs(FOC).stem).toBe(FOC);
    });

    it("writes a channel and a trace per project", () => {
        expect([...logChannels().keys()].sort()).toEqual([
            `Alamo Engine Tools - Empire at War Edit (${EAW})`,
            `Alamo Engine Tools - Empire at War Edit (${FOC})`,
        ]);
        const traces = [...outputChannels().keys()].filter((k) => k.startsWith("EaWEdit LSP Trace"));
        expect(traces).toEqual(expect.arrayContaining([`EaWEdit LSP Trace (${EAW})`, `EaWEdit LSP Trace (${FOC})`]));
    });
});
