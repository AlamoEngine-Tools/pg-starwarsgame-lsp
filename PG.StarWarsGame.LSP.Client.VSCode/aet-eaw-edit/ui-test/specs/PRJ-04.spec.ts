// PRJ-04: two projects in one window - two servers, a counted status, a channel and a trace each.
import { expect } from "@wdio/globals";
import { eaw, EAW, foc, FOC, logChannels, outputChannels, projectsReady, statusTooltip } from "../protocol.js";

describe("PRJ-04", () => {
    before(async () => {
        await projectsReady(2);
    });

    it("lists both projects ready in the tooltip", async () => {
        const tooltip = await statusTooltip();
        expect(tooltip).toContain(`${EAW} - ready`);
        expect(tooltip).toContain(`${FOC} - ready`);
    });

    // With several projects the log folder is passed with forward slashes, with one with backslashes.
    it("logs the projects and a start per project into its own folder", () => {
        const log = outputChannels().get("EaWEdit") ?? "";
        expect(log).toContain(`Projects: ${EAW}, ${FOC}`);
        const starts = log.split(/\r?\n/).filter((l) => l.includes("Starting LSP server:"));
        const logDir = (name: string) =>
            starts.find((l) => l.includes(`[${name}] Starting LSP server:`))!.split("--log-dir=")[1].replace(/\//g, "\\").toLowerCase();
        expect(logDir(EAW)).toBe(eaw.toLowerCase());
        expect(logDir(FOC)).toBe(foc.toLowerCase());
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
