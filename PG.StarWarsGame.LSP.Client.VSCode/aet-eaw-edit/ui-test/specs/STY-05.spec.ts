// STY-05: a Campaign_Set is renamed from its node in the Campaign Editor - every member in the project
// rewritten in the set's value and in nothing else, written to disk, and one undo takes it back in all of
// them. The bucket of campaigns with no set shows the entry disabled, with the reason (#181). One member
// is written by hand: a comment and a tag no schema knows both name the set, and both must stay.
import fs from "node:fs";
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";

const empire = "C:\\temp\\aet-test-STY-rename\\Data\\Xml\\Campaigns_Rename_Empire.xml";
const rebel = "C:\\temp\\aet-test-STY-rename\\Data\\Xml\\Campaigns_Rename_Rebel.xml";
// The set's value in its tag, padded or not: the hand-written member pads it as the shipped files do.
const setTag = /(<Campaign_Set>\s*)Rename_Set(\s*<\/Campaign_Set>)/;

/** The Campaign Editor's top-level rows as drawn: label and description. */
async function sets(): Promise<{ label: string; description: string }[]> {
    return browser.execute(() => {
        const section = Array.from(document.querySelectorAll(".pane")).find((p) =>
            /Campaign Editor/i.test(p.querySelector(".pane-header")?.textContent ?? ""),
        );
        return Array.from(section?.querySelectorAll('.monaco-list-row[aria-level="1"]') ?? []).map((r) => ({
            label: (r.querySelector(".label-name")?.textContent ?? "").trim(),
            description: (r.querySelector(".label-description")?.textContent ?? "").trim(),
        }));
    });
}

/** Opens the context menu of a Campaign Editor row and returns the Rename Set entry it shows. */
async function renameEntryOf(label: string): Promise<{ label: string; disabled: boolean }> {
    await browser.execute((text: string) => {
        const section = Array.from(document.querySelectorAll(".pane")).find((p) =>
            /Campaign Editor/i.test(p.querySelector(".pane-header")?.textContent ?? ""),
        );
        document.querySelectorAll("[data-aet-row]").forEach((r) => r.removeAttribute("data-aet-row"));
        Array.from(section?.querySelectorAll(".monaco-list-row") ?? [])
            .find((r) => (r.querySelector(".label-name")?.textContent ?? "").trim() === text)
            ?.setAttribute("data-aet-row", "");
    }, label);
    await (await browser.$("[data-aet-row]")).click({ button: "right" });
    let entry: { label: string; disabled: boolean } | null = null;
    await browser.waitUntil(
        async () => {
            entry = await browser.execute(() => {
                document.querySelectorAll("[data-aet-menu]").forEach((e) => e.removeAttribute("data-aet-menu"));
                const item = Array.from(document.querySelectorAll(".context-view .action-item")).find((i) =>
                    (i.querySelector(".action-label")?.textContent ?? "").trim().startsWith("Rename Set"),
                );
                if (!item) return null;
                item.querySelector(".action-menu-item")?.setAttribute("data-aet-menu", "");
                return {
                    label: (item.querySelector(".action-label")?.textContent ?? "").trim(),
                    disabled: item.classList.contains("disabled"),
                };
            });
            return entry !== null;
        },
        { timeout: 15_000, timeoutMsg: `The menu of '${label}' shows no Rename Set entry` },
    );
    // A menu takes no click in the moment it opens, so that the click that opened it picks nothing.
    await browser.pause(800);
    return entry!;
}

/** What the window holds for each member's file while it has it loaded. */
async function buffers(): Promise<{ open: boolean; dirty: boolean; text: string }[]> {
    return browser.executeWorkbench(
        (vscode, files: string[]) =>
            files.map((file) => {
                const document = vscode.workspace.textDocuments.find(
                    (d: { uri: { fsPath: string } }) => d.uri.fsPath.toLowerCase() === file.toLowerCase(),
                );
                return { open: document !== undefined, dirty: document?.isDirty ?? false, text: document?.getText() ?? "" };
            }),
        [empire, rebel],
    );
}

describe("STY-05", () => {
    let originals: string[] = [];
    let expected: string[] = [];

    before(async () => {
        originals = [fs.readFileSync(empire, "utf8"), fs.readFileSync(rebel, "utf8")];
        expected = originals.map((text) => text.replace(setTag, "$1Renamed_Set$2"));
        expect(expected[1]).toContain("<Campaign_Set> Renamed_Set </Campaign_Set>");
        await serverReady();
        const workbench = await browser.getWorkbench();
        const container = await workbench.getActivityBar().getViewControl("Empire at War Edit");
        const view = await container!.openView();
        await (await view.getContent().getSection("Campaign Editor")).expand();
        await browser.waitUntil(async () => (await sets()).length > 0, {
            timeout: 120_000,
            timeoutMsg: "The Campaign Editor lists nothing",
        });
    });

    it("shows Rename Set disabled, with the reason, where there is no set to rename", async () => {
        expect(await sets()).toEqual([
            { label: "Rename_Set", description: "2 factions" },
            { label: "Ungrouped", description: "1 campaign" },
        ]);
        expect(await renameEntryOf("Ungrouped")).toEqual({
            label: "Rename Set - no Campaign_Set to rename",
            disabled: true,
        });
        await browser.keys("Escape");
    });

    it("renames the set in every member, each changed in the one value, and writes them", async () => {
        expect(await renameEntryOf("Rename_Set")).toEqual({ label: "Rename Set...", disabled: false });
        await (await browser.$("[data-aet-menu]")).click();

        const input = await browser.$(".quick-input-widget .quick-input-box input");
        await input.waitForDisplayed({ timeout: 15_000 });
        // The box opens on the name the set has.
        expect(await input.getValue()).toBe("Rename_Set");
        await browser.keys(["Control", "a"]);
        await browser.keys("Renamed_Set".split(""));
        await browser.keys("Enter");

        await browser.waitUntil(async () => fs.readFileSync(rebel, "utf8") !== originals[1], {
            timeout: 30_000,
            timeoutMsg: "The rename did not reach the members' files",
        });
        // The comment and the note that name the set stay as written: only the value is the set.
        expect(fs.readFileSync(empire, "utf8")).toBe(expected[0]);
        expect(fs.readFileSync(rebel, "utf8")).toBe(expected[1]);
        // Written, not left behind as unsaved documents. Waited for: the file is on disk a moment
        // before its document stops counting as unsaved.
        await browser.waitUntil(async () => !(await buffers()).some((b) => b.dirty), {
            timeout: 15_000,
            timeoutMsg: "A member stayed unsaved after the rename",
        });
        await browser.waitUntil(async () => (await sets())[0]?.label === "Renamed_Set", {
            timeout: 30_000,
            timeoutMsg: "The Campaign Editor still lists the old name",
        });
    });

    it("takes the rename back in every member with one undo, which leaves them unsaved", async () => {
        await browser.executeWorkbench(async (vscode, file: string) => {
            await vscode.window.showTextDocument(vscode.Uri.file(file), { preview: false });
            // Not awaited: an undo that asked first would not come back until it was answered.
            void vscode.commands.executeCommand("undo");
        }, empire);
        await browser.waitUntil(async () => (await buffers()).every((b) => b.open && b.dirty), {
            timeout: 15_000,
            timeoutMsg: "One undo did not take the rename back in both members",
        });
        // Undone in the member that was not in front, too, and without a question asked.
        expect(await buffers()).toEqual([
            { open: true, dirty: true, text: originals[0] },
            { open: true, dirty: true, text: originals[1] },
        ]);
        expect(await (await browser.$(".monaco-dialog-box")).isDisplayed()).toBe(false);
        // The undo is an edit like any other: the windows hold the old name again, the disk the new one.
        expect(fs.readFileSync(empire, "utf8")).toBe(expected[0]);
        expect(fs.readFileSync(rebel, "utf8")).toBe(expected[1]);
    });
});
