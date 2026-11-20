// SET-32 (#395): Set Up Base Shader Sources does not take a mod's shader folder - one effect source of the
// test's own and two compiled effects - for the base sources, and says what the folder holds. Changing the
// folder restarts the server, and an open preview asks for its effects again: the Game renderer's note that
// no sources are reachable goes without a reload. A folder holding the base set - dummies of the test's own
// under those names - is found. Changing a game folder restarts the server too, and an open preview asks for
// its scene, its models and its textures again, since those resolve through the game folders.
import { browser, expect } from "@wdio/globals";
import { clearNotifications, command, notification, notifications, serverReady, setStartupSettings } from "../protocol.js";
import { model, openInPreview, openSceneSettings, press } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

const ROOT = "C:\\temp\\aet-test-SET-shaders";
const MOD = `${ROOT}\\mod\\Data\\Art\\Shaders`;
const BASE = `${ROOT}\\base`;
const GAME = `${ROOT}\\game`;
const OTHER_GAME = `${ROOT}\\game2`;

const FOUND = "EaWEdit: Base shader sources found in";
const MOD_NOTE = `EaWEdit: "${MOD}" holds a mod's or the game's shaders, not the base shader sources. A project's own shaders need no setting.`;
const NO_SOURCES =
    "No shader sources are reachable, so nothing can be translated. A game install ships compiled .fxo; the .fx sources are a separate Petroglyph download.";

/** A position of the Renderer switch, by its label. */
const position = (label: string): string =>
    `//*[@aria-label='Which renderer draws the model']//button[normalize-space()='${label}']`;

/** Picks a position of the Renderer switch. Run inside the preview. */
async function pick(label: string): Promise<void> {
    await press(position(label));
    await browser.waitUntil(
        async () => (await (await browser.$(position(label))).getAttribute("class"))?.includes("active") === true,
        { timeout: 5_000, timeoutMsg: `The Renderer did not switch to ${label}` },
    );
}

/** The notes the severity tag opens; none when the tag is disabled. Run inside the preview. */
async function notes(): Promise<string[]> {
    const tag = await browser.$("button.validate-btn");
    if (!(await tag.isEnabled())) {
        return [];
    }
    await press("button.validate-btn");
    await (await browser.$(".problem-list")).waitForExist({ timeout: 5_000 });
    const rows: string[] = await browser.execute(() =>
        Array.from(document.querySelectorAll(".problem-list .problem-msg")).map((m) => (m.textContent ?? "").trim()),
    );
    await press("button.validate-btn");
    return rows;
}

/** What the preview has been sent since the watch started. */
interface Received {
    scenes: number;
    models: number;
    textures: number;
}

type Watched = Window & { __set32?: Received };

/** Starts counting the scenes, models and textures the preview is sent, or empties the count. Run inside the preview. */
async function watch(): Promise<void> {
    await browser.execute(() => {
        const w = window as Watched;
        const fresh = w.__set32 === undefined;
        w.__set32 = { scenes: 0, models: 0, textures: 0 };
        if (!fresh) {
            return;
        }
        w.addEventListener("message", (event: MessageEvent) => {
            const type = (event.data as { type?: string } | null)?.type;
            const seen = w.__set32;
            if (seen === undefined) {
                return;
            }
            if (type === "scene") seen.scenes++;
            if (type === "glb") seen.models++;
            if (type === "texture") seen.textures++;
        });
    });
}

/** The count since the watch started. Run inside the preview. */
async function received(): Promise<Received> {
    return browser.execute(() => (window as Watched).__set32 ?? { scenes: 0, models: 0, textures: 0 });
}

describe("SET-32", () => {
    before(async () => {
        await serverReady();
    });

    it("does not take a mod's shader folder for the base sources, and says what it holds", async () => {
        await clearNotifications();
        await command("aet-eaw-edit.shaders.obtain");
        await notification((m) => m === MOD_NOTE || m.startsWith(FOUND), 30_000);
        const shown = (await notifications()).map((n) => n.message);
        expect(shown.filter((m) => m.startsWith(FOUND))).toEqual([]);
        expect(shown).toContain(MOD_NOTE);
        await clearNotifications();
    });

    it("restarts the server and asks for an open preview's effects again when the folder changes", async () => {
        await openInPreview(model("P_fire_med00.alo"));
        await inNewestWebview(async () => {
            await openSceneSettings();
            await (await browser.$(position("Game"))).waitForExist({ timeout: 60_000, timeoutMsg: "No Renderer switch" });
            await pick("Game");
            await browser.waitUntil(async () => (await notes()).includes(NO_SOURCES), {
                timeout: 30_000,
                timeoutMsg: "Game on a mod's shader folder lists no shader note",
            });
        });

        await setStartupSettings({ "aet-eaw-edit.shaders.directory": BASE });

        await inNewestWebview(async () => {
            await browser.waitUntil(async () => !(await notes()).includes(NO_SOURCES), {
                timeout: 60_000,
                timeoutMsg: "The open preview did not ask for its effects again",
            });
            await pick("Default");
            // The choice reaches the host as a message after the render.
            await browser.pause(2_000);
        });
    });

    it("finds the base sources in a folder holding the base set", async () => {
        await clearNotifications();
        await command("aet-eaw-edit.shaders.obtain");
        const found = await notification((m) => m.startsWith(FOUND), 30_000);
        expect(found.message).toBe(`${FOUND} "${BASE}".`);
    });

    it("restarts the server and asks for an open preview's scene, models and textures again when a game folder changes", async () => {
        // From one set folder to another: the scene reads the same either way, so only the change
        // itself can tell the preview that what it drew may now resolve elsewhere.
        await setStartupSettings({ "aet-eaw-edit.lsp.source.baseGameDirectory": GAME });
        await openInPreview(model("Ev_lambdashuttle.alo"));
        await inNewestWebview(async () => {
            await browser.waitUntil(async () => (await browser.$("canvas")).isExisting(), { timeout: 60_000 });
            await browser.waitUntil(async () => !(await (await browser.$(".load-cover")).isExisting()), {
                timeout: 120_000,
                timeoutMsg: "The preview did not finish loading",
            });
            // Past whatever the first restart's own index push still brings.
            await browser.pause(10_000);
            await watch();
        });

        await setStartupSettings({ "aet-eaw-edit.lsp.source.baseGameDirectory": OTHER_GAME });

        await inNewestWebview(async () => {
            let seen: Received = { scenes: 0, models: 0, textures: 0 };
            await browser
                .waitUntil(
                    async () => {
                        seen = await received();
                        return seen.scenes > 0 && seen.models > 0 && seen.textures > 0;
                    },
                    { timeout: 60_000 },
                )
                .catch(() => undefined);
            // Each said apart, so a failure reads what the preview kept from the old game folder.
            expect(seen.scenes).toBeGreaterThan(0);
            expect(seen.models).toBeGreaterThan(0);
            expect(seen.textures).toBeGreaterThan(0);
        });
    });
});
