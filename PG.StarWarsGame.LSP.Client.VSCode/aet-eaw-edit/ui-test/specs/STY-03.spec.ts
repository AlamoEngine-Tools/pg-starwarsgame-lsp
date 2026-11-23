// STY-03: a save through the story graph changes the thread file in the staged value and in nothing
// else (#339). The thread is written by hand - comments, a tag no schema knows, a reward before its
// trigger, odd spacing - and the save must leave all of it where it was.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { closeAll, graphLoaded, inNewestWebview, openStoryGraph } from "./storyGraph.js";

const thread = "C:\\temp\\aet-test-STY-save\\Data\\Xml\\Story_Save.xml";

/** What the window holds for the thread file, once it has loaded it. */
async function buffer(): Promise<{ open: boolean; text: string }> {
    return browser.executeWorkbench((vscode, file: string) => {
        const document = vscode.workspace.textDocuments.find(
            (d: { uri: { fsPath: string } }) => d.uri.fsPath.toLowerCase() === file.toLowerCase(),
        );
        return { open: document !== undefined, text: document?.getText() ?? "" };
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

describe("STY-03", () => {
    let original = "";
    let expected = "";

    before(async () => {
        original = fs.readFileSync(thread, "utf8");
        expected = original.replace("<Story_Dialog>OLD_DIALOG</Story_Dialog>", "<Story_Dialog>NEW_DIALOG</Story_Dialog>");
        await serverReady();
    });

    it("saves the staged value into the thread file and changes nothing else in it", async () => {
        await openStoryGraph("Save_Campaign", "Empire");
        await inNewestWebview(async () => {
            await (await browser.$("button.rotary-pos")).waitForExist({ timeout: 90_000 });
            await graphLoaded();
            await (await browser.$('button.rotary-pos[title="Edit"]')).click();
            await setField("Follow_Up", "Dialog", "NEW_DIALOG");
            const save = await browser.$('[title^="Save - "]');
            await browser.waitUntil(async () => (await save.getAttribute("title")) !== "Save - nothing is staged", {
                timeout: 15_000,
                timeoutMsg: "The edit was not staged",
            });
            await save.click();
            // A save that went through leaves Edit mode, and the Save button with it.
            await save.waitForExist({ reverse: true, timeout: 30_000, timeoutMsg: "The save left the graph in Edit mode" });
        });

        // Read from the window, which holds the thread as saved whether or not the save also reached the disk.
        await browser.waitUntil(async () => (await buffer()).open && (await buffer()).text !== original, {
            timeout: 30_000,
            timeoutMsg: "The window does not hold a changed thread",
        });
        expect((await buffer()).text).toBe(expected);
    });

    it("takes the whole save back with one undo", async () => {
        await browser.executeWorkbench(async (vscode, file: string) => {
            await vscode.window.showTextDocument(vscode.Uri.file(file), { preview: false });
            await vscode.commands.executeCommand("undo");
        }, thread);
        await browser.waitUntil(async () => (await buffer()).text !== expected, {
            timeout: 15_000,
            timeoutMsg: "Undo changed nothing",
        });
        expect((await buffer()).text).toBe(original);
        await closeAll();
    });
});
