// XML-16: Create localisation key from a quick fix runs, writes the key and clears the finding.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { answerInput, notifications, quickPick, serverReady } from "../protocol.js";
import { applyQuickFix, diagnosticsWhen, replaceLine, saveFile } from "./xmlEditor.js";

const root = "C:\\temp\\aet-test-xml";
const xmlTest = path.join(root, "Data", "XML", "Xml_Test.xml");
const csv = path.join(root, "Data", "Text", "MasterTextFile.csv");
const KEY = "TEXT_SCRATCH_MISSING";

describe("XML-16", () => {
    before(async () => {
        await serverReady();
    });

    it("creates the key from the quick fix", async () => {
        await replaceLine(xmlTest, 4, `\t\t<Text_ID>${KEY}</Text_ID>`);
        await saveFile(xmlTest);
        await diagnosticsWhen(xmlTest, (all) => all.some((d) => d.code === "aetswg-005-0001" && d.line === 4));

        await applyQuickFix(xmlTest, 4, KEY, `Create localisation key '${KEY}'`);
        const pick = await quickPick();
        expect(pick.title).toBe(`Create localisation key '${KEY}'`);
        const row = pick.rows.findIndex((r) => r.description.endsWith("MasterTextFile.csv"));
        expect(row).toBeGreaterThanOrEqual(0);
        await browser.execute(
            (n: number) => (document.querySelectorAll(".quick-input-widget .monaco-list-row")[n] as HTMLElement).click(),
            row,
        );

        const box = await browser.$(".quick-input-widget .quick-input-box input");
        await box.waitForDisplayed({ timeout: 15_000 });
        expect(await browser.execute(() => (document.querySelector(".quick-input-widget .quick-input-title")?.textContent ?? "").trim())).toBe(
            `New key: ${KEY}`,
        );
        await answerInput("Scratch missing");

        expect((await notifications()).filter((n) => /command .* not found/.test(n.message))).toEqual([]);
        await browser.waitUntil(async () => fs.readFileSync(csv, "utf8").includes(`${KEY},Scratch missing`), {
            timeout: 30_000,
            timeoutMsg: "MasterTextFile.csv gained no row",
        });
        await diagnosticsWhen(
            xmlTest,
            (all) => !all.some((d) => d.code === "aetswg-005-0001" && d.line === 4),
            120_000,
            "The missing-key finding stays after the reload",
        );
    });
});
