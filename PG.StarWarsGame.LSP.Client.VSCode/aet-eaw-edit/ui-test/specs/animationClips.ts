// A model's animation clips as the PRV tests read them: the clips the preview's model came with, and the
// notes that speak of animation files. Run inside the preview's webview.
import { browser } from "@wdio/globals";
import { type Note } from "./preview.js";

export const MODEL =
    "C:\\temp\\aet-test-PRV-anim\\Data\\Art\\Models\\EV_AT-AT.ALO";

/** Waits for the model's geometry and the load cover to come off. Run inside the preview. */
export async function loaded(): Promise<void> {
    await browser.waitUntil(
        async () =>
            browser.execute(() => {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                const v = (window as any).aetDebugViewport;
                return (
                    v !== undefined &&
                    v.parts.size > 0 &&
                    document.querySelector(".load-cover") === null
                );
            }),
        { timeout: 120_000, timeoutMsg: "The model did not load" },
    );
    await browser.pause(3_000);
}

/** The clips the model came with. Run inside the preview. */
export async function clips(): Promise<string[]> {
    return browser.execute(
        () =>
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            (window as any).aetDebugViewport.animationNames as string[],
    );
}

/** The notes that speak of animation files. */
export const aboutClips = (notes: Note[]): Note[] =>
    notes.filter((n) => /animation file/i.test(n.text));
