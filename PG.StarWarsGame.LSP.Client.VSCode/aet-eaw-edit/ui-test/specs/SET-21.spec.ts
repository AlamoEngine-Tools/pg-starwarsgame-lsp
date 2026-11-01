// SET-21: a mod opened without a .pgproj is offered one; Create project file writes it and the
// server restarts on it. The bug report's project line is read from the server's log - see
// protocol.ts.
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { browser, expect } from "@wdio/globals";
import { extensionLog, lspStatus, notifications, serverReady, takeNotificationAction } from "../protocol.js";

const mod = "C:\\temp\\aet-test-SET-21\\AdoptMod";
const offer = "EaWEdit: Mod without a project file - c:\\temp\\aet-test-SET-21\\AdoptMod";
const project = path.join(mod, "Adopt_Test_Mod.pgproj");

function hash(file: string): string {
    return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
}

/** The server's [initialized] lines: whether each start found a project file, and which. */
function projectScans(): string[] {
    return extensionLog()
        .split(/\r?\n/)
        .filter((l) => l.includes("[initialized] scanRoots="))
        .map((l) => /\.pgproj present=(\w+) path=(\S+)/.exec(l)?.slice(1).join(" ") ?? l);
}

describe("SET-21", () => {
    const modinfo = hash(path.join(mod, "modinfo.json"));

    it("offers a project file, with no missing-project error", async () => {
        await browser.waitUntil(async () => (await lspStatus()) !== null, {
            timeout: 60_000,
            timeoutMsg: "No EaWEdit LSP status item",
        });
        // The protocol watches for 10 seconds: an error would arrive after the offer, not with it.
        await browser.pause(10_000);
        const shown = await notifications();
        const offers = shown.filter((n) => n.message === offer);
        expect(offers).toHaveLength(1);
        expect(offers[0].type).toBe("info");
        expect(offers[0].actions).toEqual(["Create project file", "Not now", "Not for this folder"]);
        expect(shown.filter((n) => n.message.includes("No .pgproj file was found"))).toEqual([]);
        expect(projectScans()).toEqual(["False <none>"]);
    });

    it("writes the project file on Create and opens it", async () => {
        await takeNotificationAction(offer, "Create project file");
        await browser.waitUntil(async () => fs.existsSync(project), {
            timeout: 30_000,
            timeoutMsg: `${project} was not written`,
        });

        const text = fs.readFileSync(project, "utf8");
        const order = [
            '"_type": "aetswg.ModProject"',
            '"_typeVersion": "aetswg-1.1.0"',
            '"name": "Adopt Test Mod"',
            '"game": "foc"',
            '"directories"',
            '"projectReferences": []',
        ];
        expect(order.filter((s) => !text.includes(s))).toEqual([]);
        const at = order.map((s) => text.indexOf(s));
        expect([...at].sort((a, b) => a - b)).toEqual(at);

        const written = JSON.parse(text);
        expect(written.directories).toEqual({
            xml: ["Data/XML"],
            scripts: ["Data/Scripts"],
            storyDialog: ["Data/Scripts/Story"],
        });
        expect(written.localisation).toEqual({ type: "CSV", directory: "Data/Text" });

        const workbench = await browser.getWorkbench();
        await browser.waitUntil(
            async () => (await workbench.getEditorView().getOpenEditorTitles()).includes("Adopt_Test_Mod.pgproj"),
            { timeout: 15_000, timeoutMsg: "The project file was not opened in an editor" },
        );
    });

    it("restarts the server on the project", async () => {
        await browser.waitUntil(async () => projectScans().length === 2, {
            timeout: 60_000,
            timeoutMsg: `The server did not restart; scans ${JSON.stringify(projectScans())}`,
        });
        await serverReady();
        expect(projectScans()[1].toLowerCase()).toBe(`true ${project.toLowerCase()}`);

        // The restarted server answers once it has indexed; until then a hover is empty.
        let hover = "";
        await browser.waitUntil(
            async () => {
                hover = await browser.executeWorkbench(async (vscode, file: string) => {
                    const document = await vscode.workspace.openTextDocument(vscode.Uri.file(file));
                    await vscode.window.showTextDocument(document);
                    const offset = document.getText().indexOf("<Population_Value>") + 2;
                    const hovers = await vscode.commands.executeCommand(
                        "vscode.executeHoverProvider",
                        document.uri,
                        document.positionAt(offset),
                    );
                    return (hovers ?? [])
                        .flatMap((h: any) => h.contents.map((c: any) => (typeof c === "string" ? c : c.value)))
                        .join("\n");
                }, path.join(mod, "Data", "XML", "TestUnits.xml"));
                return hover !== "";
            },
            { timeout: 60_000, interval: 2000, timeoutMsg: "No hover on Population_Value" },
        );
        expect(hover).toContain("Population_Value");
    });

    it("leaves modinfo.json as it was and creates no art or audio folder", () => {
        expect(hash(path.join(mod, "modinfo.json"))).toBe(modinfo);
        expect(fs.existsSync(path.join(mod, "Data", "Art"))).toBe(false);
        expect(fs.existsSync(path.join(mod, "Data", "Audio"))).toBe(false);
    });
});
