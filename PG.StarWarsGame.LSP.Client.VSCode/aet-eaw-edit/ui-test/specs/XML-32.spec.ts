// XML-32 (#136): a variant's Encyclopedia_Text says what it overrides, like every other tag the variant
// redefines - the inline marker on its line in the editor, and the note above it in the effective object.
// Two shapes of the tag: the keys on one line with different text, and the keys on their own lines with
// the base's exact text. The marker has to be READABLE, not only served: the editor truncates a line's
// inlay hints past a length (editor.inlayHints.maximumLength, 43 by default), and a localisation key's
// text hint is long, so a marker served after the texts is cut off - which is what the report showed.
import { browser, expect } from "@wdio/globals";
import { answerInput, command, openFile, serverReady, tabLabels } from "../protocol.js";

const file = "C:\\temp\\aet-test-XML-ency\\Data\\XML\\EncyUnits.xml";

interface Hint {
    line: number;
    character: number;
    label: string;
}

/** Every inlay hint the editor shows for the file, with 1-based lines; opens the file first. */
async function inlayHints(target: string): Promise<Hint[]> {
    return browser.executeWorkbench(async (vscode, path: string) => {
        const document = await vscode.workspace.openTextDocument(vscode.Uri.file(path));
        await vscode.window.showTextDocument(document, { preview: false });
        const whole = new vscode.Range(0, 0, document.lineCount, 0);
        const hints = (await vscode.commands.executeCommand("vscode.executeInlayHintProvider", document.uri, whole)) ?? [];
        return hints.map((h: any) => ({
            line: h.position.line + 1,
            character: h.position.character,
            label: typeof h.label === "string" ? h.label : h.label.map((p: any) => p.value).join(""),
        }));
    }, target);
}

/** The hints on one line that say what the tag overrides. */
function overrideMarkers(hints: Hint[], line: number): string[] {
    return hints.filter((h) => h.line === line && /^(overrides|adds|merges)/.test(h.label)).map((h) => h.label);
}

/** The editor's rendered text of the first line that passes the test, inlay hints included. */
async function renderedLine(test: (text: string) => boolean): Promise<string> {
    const lines: string[] = await browser.execute(() =>
        Array.from(document.querySelectorAll(".monaco-editor .view-lines .view-line")).map((l) =>
            (l.textContent ?? "").replace(/ /g, " ").trim(),
        ),
    );
    return lines.find(test) ?? "";
}

describe("XML-32", () => {
    before(async () => {
        await serverReady();
        await openFile(file);
    });

    it("marks every tag the variant redefines, Encyclopedia_Text included", async () => {
        let hints: Hint[] = [];
        await browser.waitUntil(
            async () => {
                hints = await inlayHints(file);
                return overrideMarkers(hints, 12).length > 0;
            },
            { timeout: 120_000, timeoutMsg: "Tech_Level never got its override marker" },
        );
        expect(overrideMarkers(hints, 12)).toEqual(["overrides 1"]);
        expect(overrideMarkers(hints, 10)).toEqual(["overrides TEXT_ENCY_BASE"]);
        // The marker keeps 32 characters of the displaced value; the whole of it is in the effective object.
        expect(overrideMarkers(hints, 11)).toEqual(["overrides TEXT_ENCY_BASE_1 TEXT_ENCY_BASE_..."]);
        expect(overrideMarkers(hints, 16)).toEqual(["overrides TEXT_ENCY_BASE_1 TEXT_ENCY_BASE_..."]);
        // Served first on its line: the editor draws a line's hints in the order it holds them.
        console.log("XML-32 hints on line 11: " + JSON.stringify(hints.filter((h) => h.line === 11)));
        expect(hints.filter((h) => h.line === 11).map((h) => h.label)[0]).toMatch(/^overrides/);
    });

    it("shows the marker on the Encyclopedia_Text line, ahead of the texts", async () => {
        // The editor draws hints a moment after the file opens; the marker on a line with no text hint
        // is the sign that they are drawn.
        await openFile(file);
        await browser.waitUntil(async () => (await renderedLine((t) => t.startsWith("<Tech_Level>2"))).includes("overrides 1"), {
            timeout: 60_000,
            timeoutMsg: "The editor never drew the Tech_Level marker",
        });
        const oneLine = await renderedLine((t) => t.startsWith("<Encyclopedia_Text>TEXT_ENCY_VARIANT_1"));
        const ownLines = await renderedLine((t) => t.startsWith("<Encyclopedia_Text>") && !t.startsWith("<Encyclopedia_Text>TEXT_"));
        expect(oneLine).toContain("overrides TEXT_ENCY_BASE_1");
        expect(ownLines).toContain("overrides TEXT_ENCY_BASE_1");
    });

    it("notes the override above Encyclopedia_Text in the effective object", async () => {
        await command("aet-eaw-edit.lsp.showEffectiveObject");
        await answerInput("Ency_Variant");
        await browser.waitUntil(async () => (await tabLabels()).some((t) => t.startsWith("Ency_Variant")), {
            timeout: 30_000,
            timeoutMsg: "The effective object never opened",
        });
        const text: string = await browser.executeWorkbench(async (vscode) => {
            const doc = vscode.workspace.textDocuments.find((d: any) => d.uri.path.includes("Ency_Variant"));
            return doc ? doc.getText() : "";
        });
        const lines = text.split(/\r?\n/).map((l) => l.trim());
        const before = (tag: string) => lines[lines.findIndex((l) => l.startsWith(`<${tag}>`)) - 1];
        expect(before("Tech_Level")).toBe("<!-- overrides base - was 1 -->");
        expect(before("Encyclopedia_Text")).toBe("<!-- overrides base - was TEXT_ENCY_BASE_1 TEXT_ENCY_BASE_2 -->");
    });
});
