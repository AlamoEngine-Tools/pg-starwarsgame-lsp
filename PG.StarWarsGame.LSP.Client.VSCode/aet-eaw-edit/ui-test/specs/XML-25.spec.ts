// XML-25: a references lens on every value an enum file or GameConstants.xml defines, opening its uses.
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { eaw, serverReady } from "../protocol.js";
import { codeLenses, type Lens } from "./xmlEditor.js";

const REFERENCES = /^(\d+) references?$/;

async function lensesWhen(file: string, test: (all: Lens[]) => boolean): Promise<Lens[]> {
    let all: Lens[] = [];
    try {
        await browser.waitUntil(async () => test((all = await codeLenses(file))), { timeout: 120_000, interval: 2_000 });
    } catch {
        throw new Error(`The lenses of ${path.basename(file)} never got there: ${JSON.stringify(all.slice(0, 20))}`);
    }
    return all;
}

describe("XML-25", () => {
    before(async () => {
        await serverReady();
    });

    it("puts a lens on each of Movementclasstype.xml's values, counting its uses", async () => {
        const file = path.join(eaw, "Data", "XML", "Enum", "Movementclasstype.xml");
        const lenses = await lensesWhen(file, (all) => all.filter((l) => REFERENCES.test(l.title)).length >= 14);
        const values = lenses.filter((l) => REFERENCES.test(l.title));
        expect(values.map((l) => l.line)).toEqual(Array.from({ length: 14 }, (_, i) => 4 + i));
        const infantry = values.find((l) => l.line === 4)!;
        const count = Number(REFERENCES.exec(infantry.title)![1]);
        expect(count).toBeGreaterThan(0);
        expect(infantry.locations).toBe(count);
    });

    it("puts a lens on each Armor_Types value of GameConstants.xml", async () => {
        const file = path.join(eaw, "Data", "XML", "Gameconstants.xml");
        const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
        const open = lines.findIndex((l) => l.includes("<Armor_Types>"));
        const close = lines.findIndex((l, i) => i > open && l.includes("</Armor_Types>"));
        // A value line opens on its name; comments and the tags around the list are not values.
        const valueLines = lines
            .map((l, i) => ({ l, line: i + 1 }))
            .filter(({ l, line }) => line > open + 1 && line < close + 1 && /^\s*\w+\s*,?/.test(l))
            .map(({ line }) => line);
        const lenses = await lensesWhen(file, (all) => valueLines.every((line) => all.some((l) => l.line === line)));
        const armorDefault = lenses.find((l) => l.line === valueLines[0]);
        expect(lines[valueLines[0] - 1]).toContain("Armor_Default");
        expect(armorDefault?.title).toMatch(REFERENCES);
        expect(armorDefault!.locations).toBeGreaterThan(0);
    });
});
