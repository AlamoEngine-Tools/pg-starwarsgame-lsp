// STY-03: a save through the story graph writes the thread file, changed in the staged value and in
// nothing else; one undo takes the save back; and the next save writes the thread as the window holds
// it, unsaved edits included (#339). The thread is written by hand - comments, a tag no schema knows, a
// reward before its trigger, odd spacing - and a save must leave all of it where it was.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { closeAll, graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const thread = "C:\\temp\\aet-test-STY-save\\Data\\Xml\\Story_Save.xml";

/** What the window holds for the thread file while it has it loaded: its text, and whether that is unsaved. */
async function buffer(): Promise<{ open: boolean; dirty: boolean; text: string }> {
    return browser.executeWorkbench((vscode, file: string) => {
        const document = vscode.workspace.textDocuments.find(
            (d: { uri: { fsPath: string } }) => d.uri.fsPath.toLowerCase() === file.toLowerCase(),
        );
        return { open: document !== undefined, dirty: document?.isDirty ?? false, text: document?.getText() ?? "" };
    }, thread);
}

/** Types a value into a field of an event's node, by the field's label, and leaves the field. */
async function setField(event: string, label: string, value: string): Promise<void> {
    await browser.waitUntil(
        async () =>
            browser.execute(
                (name: string, field: string) => {
                    const node = Array.from(document.querySelectorAll<HTMLElement>('[data-testid="node"]')).find((n) =>
                        (n.getAttribute("data-node-id") ?? "").endsWith(`#${name.toLowerCase()}`),
                    );
                    if (!node) return false;
                    const expand = node.querySelector<HTMLElement>('.section-toggle[title="Expand the general section"]');
                    if (expand) {
                        expand.click();
                        return false;
                    }
                    const row = Array.from(node.querySelectorAll(".row")).find(
                        (r) => (r.querySelector("label")?.textContent ?? "").trim() === field,
                    );
                    const input = row?.querySelector<HTMLInputElement>('input[type="text"]');
                    if (!input || input.disabled) return false;
                    document.querySelectorAll("[data-aet-target]").forEach((e) => e.removeAttribute("data-aet-target"));
                    input.setAttribute("data-aet-target", "");
                    return true;
                },
                event,
                label,
            ),
        { timeout: 60_000, timeoutMsg: `No editable '${label}' field on the event '${event}'` },
    );
    const input = await browser.$("[data-aet-target]");
    await input.click();
    await browser.keys(["Control", "a"]);
    await browser.keys(value.split(""));
    await browser.keys("Tab");
    await browser.pause(500);
}

/** Enters Edit mode, stages one field of one event, saves, and waits until the graph has left Edit mode. */
async function saveField(event: string, label: string, value: string): Promise<void> {
    await inNewestWebview(async () => {
        await (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 });
        await graphLoaded();
        await (await browser.$('button.rotary-pos[title="Edit"]')).click();
        await setField(event, label, value);
        const save = await browser.$('[title^="Save - "]');
        await browser.waitUntil(async () => (await save.getAttribute("title")) !== "Save - nothing is staged", {
            timeout: 15_000,
            timeoutMsg: "The edit was not staged",
        });
        await save.click();
        // A save that went through leaves Edit mode. Save stays in the toolbar and says so.
        await browser.waitUntil(async () => (await save.getAttribute("title")) === "Save - Edit mode only", {
            timeout: 30_000,
            timeoutMsg: "The save left the graph in Edit mode",
        });
    });
}

describe("STY-03", () => {
    let original = "";
    let expected = "";

    before(async () => {
        original = fs.readFileSync(thread, "utf8");
        expected = original.replace("<Story_Dialog>OLD_DIALOG</Story_Dialog>", "<Story_Dialog>NEW_DIALOG</Story_Dialog>");
        await serverReady();
    });

    // Discard asks first, and the editor shows no modal dialog to a test: this window has the setting
    // behind "Discard & Don't Ask Again" turned off, so Discard acts at once (#222).
    it("drops a staged value on Discard, stays in Edit mode and writes nothing", async () => {
        await openStoryGraph("Save_Campaign", "Empire");
        await inNewestWebview(async () => {
            await (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 });
            await graphLoaded();
            await (await browser.$('button.rotary-pos[title="Edit"]')).click();
            const discard = await browser.$("button.toolbar-discard");
            expect(await discard.getAttribute("title")).toBe("Discard - nothing is staged");
            await setField("Follow_Up", "Dialog", "DROPPED_DIALOG");
            const save = await browser.$('[title^="Save - "]');
            await browser.waitUntil(async () => (await save.getAttribute("title")) !== "Save - nothing is staged", {
                timeout: 15_000,
                timeoutMsg: "The edit was not staged",
            });

            await discard.click();

            // Nothing staged, and still Edit: outside it Save would say "Edit mode only".
            await browser.waitUntil(async () => (await save.getAttribute("title")) === "Save - nothing is staged", {
                timeout: 30_000,
                timeoutMsg: "Discard left the value staged",
            });
        });
        expect(fs.readFileSync(thread, "utf8")).toBe(original);
    });

    it("saves the staged value into the thread file and changes nothing else in it", async () => {
        await openStoryGraph("Save_Campaign", "Empire");
        await saveField("Follow_Up", "Dialog", "NEW_DIALOG");

        await browser.waitUntil(async () => fs.readFileSync(thread, "utf8") !== original, {
            timeout: 30_000,
            timeoutMsg: "The save did not reach the thread file",
        });
        expect(fs.readFileSync(thread, "utf8")).toBe(expected);
        // Written, not left behind as an unsaved document.
        expect((await buffer()).dirty).toBe(false);
    });

    it("takes the whole save back with one undo, which leaves the thread unsaved", async () => {
        await browser.executeWorkbench(async (vscode, file: string) => {
            await vscode.window.showTextDocument(vscode.Uri.file(file), { preview: false });
            await vscode.commands.executeCommand("undo");
        }, thread);
        await browser.waitUntil(async () => (await buffer()).open && (await buffer()).text !== expected, {
            timeout: 15_000,
            timeoutMsg: "Undo changed nothing",
        });
        // The undo is an edit like any other: the window holds the original again, the disk the save.
        expect(await buffer()).toEqual({ open: true, dirty: true, text: original });
        expect(fs.readFileSync(thread, "utf8")).toBe(expected);
    });

    it("writes the thread as the window holds it, so the next save takes the unsaved undo with it", async () => {
        // The undo left the thread's editor in front of the graph.
        await openStoryGraph("Save_Campaign", "Empire");
        await saveField("Opening", "Branch", "Act1");

        await browser.waitUntil(async () => fs.readFileSync(thread, "utf8") !== expected, {
            timeout: 30_000,
            timeoutMsg: "The second save did not reach the thread file",
        });
        const written = fs.readFileSync(thread, "utf8");
        expect(written).toContain("<Branch>Act1</Branch>");
        expect(written).toContain("<Story_Dialog>OLD_DIALOG</Story_Dialog>");
        expect(written.replace(/^[ \t]*<Branch>Act1<\/Branch>\r?\n/m, "")).toBe(original);
        expect((await buffer()).dirty).toBe(false);
        await closeAll();
    });
});
