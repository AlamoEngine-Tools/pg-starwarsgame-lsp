// XML-14: Suppress across the project refilters every open file at once and writes suppressions.json.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { notifications, serverReady } from "../protocol.js";
import { applyQuickFix, diagnostics, diagnosticsWhen } from "./xmlEditor.js";

const root = "C:\\temp\\aet-test-xml";
const xmlTest = path.join(root, "Data", "XML", "Xml_Test.xml");
const units = path.join(root, "Data", "XML", "Scratch_Units.xml");
const CODE = "aetswg-015-0004";
const others = (all: { code: string; line: number }[]): string[] =>
    all.filter((d) => d.code !== CODE).map((d) => `${d.code}@${d.line}`).sort();

describe("XML-14", () => {
    before(async () => {
        await serverReady();
    });

    it("suppresses across the project and refilters the file not touched", async () => {
        expect(fs.existsSync(path.join(root, ".aetswg", "suppressions.json"))).toBe(false);
        const before = {
            test: await diagnosticsWhen(xmlTest, (all) => all.some((d) => d.code === CODE && d.line === 4)),
            units: await diagnosticsWhen(units, (all) => all.some((d) => d.code === CODE && d.line === 19)),
        };

        await applyQuickFix(xmlTest, 4, "``", `Suppress ${CODE} across the project`);
        await diagnosticsWhen(xmlTest, (all) => !all.some((d) => d.code === CODE), 30_000, "Xml_Test.xml keeps the warning");
        await diagnosticsWhen(units, (all) => !all.some((d) => d.code === CODE), 30_000, "Scratch_Units.xml keeps the warning");
        expect(others(await diagnostics(xmlTest))).toEqual(others(before.test));
        expect(others(await diagnostics(units))).toEqual(others(before.units));

        expect((await notifications()).filter((n) => n.type === "error")).toEqual([]);

        const file = JSON.parse(fs.readFileSync(path.join(root, ".aetswg", "suppressions.json"), "utf8"));
        expect(file._type).toBe("aetswg.Suppressions");
        expect(file._typeVersion).toBe("aetswg-2.0.0");
        const entries = Object.values(file).find((v) => Array.isArray(v)) as { id: string; reason: string }[];
        expect(entries).toEqual([
            {
                id: "aetswg-015-0006",
                reason: "Off by default: comments inside values are common practice; delete this entry to report them",
            },
            { id: CODE, reason: "suppressed from the editor" },
        ]);
        await browser.pause(500);
    });
});
