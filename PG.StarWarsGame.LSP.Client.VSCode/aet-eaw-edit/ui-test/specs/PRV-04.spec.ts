// PRV-04: the wind's heading in whole degrees, its speed 0 to 10, defaults 0 deg and 2.0, kept per viewer.
import { browser, expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { fieldInfo, model, openInPreview, openSceneSettings, sectionFields, setSlider, sliderRange } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

const value = async (label: string): Promise<string> =>
    (await sectionFields("Weather")).find((f) => f.label === label)?.value ?? "";

describe("PRV-04", () => {
    before(async () => {
        await serverReady();
    });

    it("resets the wind and names its ranges", async () => {
        await openInPreview(model("P_fire_med00.alo"));
        await inNewestWebview(async () => {
            await openSceneSettings();
            const reset = await browser.$('button[title^="Put the backdrop, lights, wind and draw distance back to their defaults"]');
            await reset.waitForClickable({ timeout: 10_000 });
            await reset.click();
            await browser.waitUntil(async () => (await value("Wind direction")) === "0 deg", { timeout: 5_000 });
            expect(await value("Wind speed")).toBe("2.0");

            expect(await fieldInfo("Wind direction")).toBe("Heading the wind blows towards");
            expect(await fieldInfo("Wind speed")).toBe(
                "Bends foliage and carries the emitters and weather declared affected by wind",
            );
            expect(await sliderRange("Wind direction")).toMatchObject({ min: "0", max: "360", step: "1" });
            expect(await sliderRange("Wind speed")).toMatchObject({ min: "0", max: "10", step: "0.1" });

            await setSlider("Wind direction", 137);
            await setSlider("Wind speed", 7.3);
            await browser.waitUntil(async () => (await value("Wind direction")) === "137 deg", { timeout: 5_000 });
            expect(await value("Wind speed")).toBe("7.3");
        });
    });

    it("keeps the wind on another model", async () => {
        await openInPreview(model("Ev_lambdashuttle.alo"));
        await inNewestWebview(async () => {
            await openSceneSettings();
            await browser.waitUntil(async () => (await value("Wind direction")) !== "", { timeout: 60_000 });
            expect(await value("Wind direction")).toBe("137 deg");
            expect(await value("Wind speed")).toBe("7.3");
        });
    });
});
