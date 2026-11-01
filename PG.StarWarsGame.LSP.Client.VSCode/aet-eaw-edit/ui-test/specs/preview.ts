// The model preview, as the PRV tests read it.
import path from "node:path";
import { browser } from "@wdio/globals";
import { eaw } from "../protocol.js";

/** A model under the EaW workspace's Data\Art\Models. */
export function model(file: string): string {
    return path.join(eaw, "Data", "Art", "Models", file);
}

/** Opens a file in its default editor, which for an .alo is the model preview. */
export async function openInPreview(file: string): Promise<void> {
    await browser.executeWorkbench((vscode, target: string) => {
        void vscode.commands.executeCommand("vscode.open", vscode.Uri.file(target));
    }, file);
}

/** Opens Scene settings unless it is open already. Run inside the preview's webview. */
export async function openSceneSettings(): Promise<void> {
    const globe = await browser.$('button[title="Scene settings"]');
    await globe.waitForClickable({ timeout: 120_000 });
    if (!((await globe.getAttribute("class")) ?? "").split(/\s+/).includes("active")) {
        await globe.click();
    }
    await browser.$(".stage-flyout").waitForExist({ timeout: 10_000 });
}

/**
 * Clicks an element inside the webview by script. A toast of the workbench's sits over the webview's
 * frame, and a pointer click at that point lands on the toast instead.
 */
export async function press(selector: string): Promise<void> {
    const target = await browser.$(selector);
    await target.waitForExist({ timeout: 30_000 });
    await browser.execute((el: HTMLElement) => el.click(), target as unknown as HTMLElement);
}

export interface FieldRow {
    label: string;
    value: string;
    disabled: boolean;
}

/** The rows of a dock section, by its name. Run inside a webview. */
export async function sectionFields(section: string): Promise<FieldRow[]> {
    return browser.execute((name: string) => {
        const block = Array.from(document.querySelectorAll(".dock-section")).find(
            (s) => (s.querySelector(".dock-section-name")?.textContent ?? "").trim() === name,
        );
        return Array.from(block?.querySelectorAll(".dock-section-body > .field") ?? []).map((field) => {
            const label = field.querySelector(".field-label");
            const own = Array.from(label?.childNodes ?? [])
                .filter((n) => n.nodeType === 3)
                .map((n) => n.textContent ?? "")
                .join("")
                .trim();
            const input = field.querySelector("input");
            return {
                label: own,
                value: (field.querySelector(".section-count")?.textContent ?? "").trim(),
                disabled: input?.disabled ?? false,
            };
        });
    }, section);
}

/** The text of a field's info mark, as its tooltip shows it. Run inside a webview. */
export async function fieldInfo(label: string): Promise<string> {
    const found: boolean = await browser.execute((name: string) => {
        const field = Array.from(document.querySelectorAll(".field")).find((f) =>
            Array.from(f.querySelector(".field-label")?.childNodes ?? []).some(
                (n) => n.nodeType === 3 && (n.textContent ?? "").trim() === name,
            ),
        );
        const badge = field?.querySelector<HTMLButtonElement>(".info-badge");
        badge?.focus();
        return badge !== undefined && badge !== null;
    }, label);
    if (!found) {
        throw new Error(`No info mark on '${label}'`);
    }
    const tip = await browser.$('[role="tooltip"]');
    await tip.waitForExist({ timeout: 5_000 });
    const text = (await tip.getText()).trim();
    await browser.execute(() => (document.activeElement as HTMLElement | null)?.blur());
    return text;
}

/** A slider's range by its field label. Run inside a webview. */
export async function sliderRange(label: string): Promise<{ min: string; max: string; step: string; value: string }> {
    return browser.execute((name: string) => {
        const field = Array.from(document.querySelectorAll(".field")).find((f) =>
            Array.from(f.querySelector(".field-label")?.childNodes ?? []).some(
                (n) => n.nodeType === 3 && (n.textContent ?? "").trim() === name,
            ),
        );
        const input = field?.querySelector<HTMLInputElement>('input[type="range"]');
        return { min: input?.min ?? "", max: input?.max ?? "", step: input?.step ?? "", value: input?.value ?? "" };
    }, label);
}

/** Sets a range input the way a drag does, so React sees the change. Run inside a webview. */
export async function setSlider(label: string, value: number): Promise<void> {
    await browser.execute(
        (name: string, to: number) => {
            const field = Array.from(document.querySelectorAll(".field")).find((f) =>
                Array.from(f.querySelector(".field-label")?.childNodes ?? []).some(
                    (n) => n.nodeType === 3 && (n.textContent ?? "").trim() === name,
                ),
            );
            const input = field?.querySelector<HTMLInputElement>('input[type="range"]');
            if (!input) {
                throw new Error(`No slider '${name}'`);
            }
            const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
            setter.call(input, String(to));
            input.dispatchEvent(new Event("input", { bubbles: true }));
        },
        label,
        value,
    );
}

/** The emitter names the dock lists for a particle file, once it lists any. Run inside a webview. */
export async function emitters(): Promise<string[]> {
    let names: string[] = [];
    await browser.waitUntil(
        async () => {
            names = await browser.execute(() => {
                const block = Array.from(document.querySelectorAll(".dock-section")).find(
                    (s) => (s.querySelector(".dock-section-name")?.textContent ?? "").trim() === "Emitters",
                );
                return Array.from(block?.querySelectorAll(".part-list label") ?? []).map((l) =>
                    (l.textContent ?? "").trim(),
                );
            });
            return names.length > 0;
        },
        { timeout: 60_000, timeoutMsg: "No emitters listed" },
    );
    return names;
}
