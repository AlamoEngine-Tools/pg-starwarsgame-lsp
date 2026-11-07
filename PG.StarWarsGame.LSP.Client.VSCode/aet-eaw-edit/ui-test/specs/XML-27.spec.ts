// XML-27: Suppress across the project in a two-project window writes to the project of the file in front, and asks
// no project. Both suppression files are put back afterwards - they are the repository's workspaces.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { eaw, foc, notifications, projectsReady } from "../protocol.js";
import { applyQuickFix, diagnosticsWhen } from "./xmlEditor.js";

const file = path.join(eaw, "Data", "XML", "Surfacefx.xml");
const eawSuppressions = path.join(eaw, ".aetswg", "suppressions.json");
const focSuppressions = path.join(foc, ".aetswg", "suppressions.json");
const CODE = "aetswg-012-3003";

describe("XML-27", () => {
    const kept = { eaw: "", foc: "" };

    before(async () => {
        await projectsReady(2);
        kept.eaw = fs.readFileSync(eawSuppressions, "utf8");
        kept.foc = fs.readFileSync(focSuppressions, "utf8");
    });

    it("suppresses in the EaW project, asking no project", async () => {
        await diagnosticsWhen(file, (all) => all.some((d) => d.code === CODE && d.line === 227));
        await applyQuickFix(file, 227, "</SoundFX_name>", `Suppress ${CODE} across the project`);
        await browser.pause(2_000);
        expect(await (await browser.$(".quick-input-widget .quick-input-box input")).isDisplayed()).toBe(false);
        await diagnosticsWhen(file, (all) => !all.some((d) => d.code === CODE), 30_000, "The error stays");
        expect((await notifications()).filter((n) => /command .* not found/.test(n.message))).toEqual([]);

        const entries = (text: string): { id: string; reason: string }[] =>
            Object.values(JSON.parse(text)).find((v) => Array.isArray(v)) as { id: string; reason: string }[];
        const before = entries(kept.eaw);
        expect(entries(fs.readFileSync(eawSuppressions, "utf8"))).toEqual([
            ...before,
            { id: CODE, reason: "suppressed from the editor" },
        ]);
        expect(fs.readFileSync(focSuppressions, "utf8")).toBe(kept.foc);
    });

    after(() => {
        fs.writeFileSync(eawSuppressions, kept.eaw);
        fs.writeFileSync(focSuppressions, kept.foc);
    });
});
