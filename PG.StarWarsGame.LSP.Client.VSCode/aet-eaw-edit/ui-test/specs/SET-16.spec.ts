// SET-16: New Mod Project leaves an existing modinfo.json as it was, writes one where there is none,
// and refuses a second project file.
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import {
    answerFolderDialog,
    answerInput,
    clearNotifications,
    command,
    notification,
    notifications,
    serverReady,
    serverStarts,
} from "../protocol.js";

// As VS Code spells a folder it was handed: the drive letter lower-cased.
const root = "c:\\temp\\aet-test-SET-16";
const existing = path.join(root, "ExistingMod");
const fresh = path.join(root, "FreshMod");

function hash(file: string): string {
    return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
}

async function newModProject(folder: string): Promise<void> {
    await clearNotifications();
    await command("aet-eaw-edit.lsp.newModProject");
    await answerInput("Test Mod");
    await answerFolderDialog(folder, "Select mod root folder");
}

describe("SET-16", () => {
    const modinfo = hash(path.join(existing, "modinfo.json"));
    let starts = 0;

    before(async () => {
        await serverReady();
        starts = serverStarts();
    });

    it("creates the project in a folder with a modinfo.json and leaves that file alone", async () => {
        await newModProject(existing);
        const created = path.join(existing, "Test_Mod.pgproj");
        await notification((m) => m === `Mod project 'Test Mod' created - ${created}`);
        for (const dir of ["XML", "Scripts\\Story", "Scripts\\Library", "Art\\Models", "Art\\Textures", "Audio\\Music", "Audio\\SFX", "Text"])
            expect(fs.existsSync(path.join(existing, "Data", dir))).toBe(true);
        expect(hash(path.join(existing, "modinfo.json"))).toBe(modinfo);
    });

    it("writes a modinfo.json where there is none", async () => {
        await newModProject(fresh);
        await notification((m) => m === `Mod project 'Test Mod' created - ${path.join(fresh, "Test_Mod.pgproj")}`);
        expect(JSON.parse(fs.readFileSync(path.join(fresh, "modinfo.json"), "utf8"))).toEqual({
            name: "Test Mod",
            version: "1.0.0",
        });
    });

    it("refuses a second project file", async () => {
        await newModProject(existing);
        await notification(
            (m) => m === `Mod project not created: A project file already exists - ${path.join(existing, "Test_Mod.pgproj")}`,
        );
        expect((await notifications()).filter((n) => n.message.startsWith("Mod project 'Test Mod' created"))).toEqual([]);
    });

    it("does not restart the running server", async () => {
        await browser.pause(3_000);
        expect(serverStarts()).toBe(starts);
    });
});
