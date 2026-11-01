// PRV-01: a scene without a weather emitter disables Weather intensity; ordinary emitters keep theirs.
// The fire's density is a picture and stays a hand check.
import { expect } from "@wdio/globals";
import { serverReady } from "../protocol.js";
import { emitters, fieldInfo, model, openInPreview, openSceneSettings, sectionFields } from "./preview.js";
import { inNewestWebview } from "./storyGraph.js";

describe("PRV-01", () => {
    before(async () => {
        await serverReady();
    });

    it("disables Weather intensity with no weather emitter in the scene", async () => {
        await openInPreview(model("P_fire_med00.alo"));
        await inNewestWebview(async () => {
            await openSceneSettings();
            const weather = await sectionFields("Weather");
            expect(weather.map((f) => f.label)).toEqual(["Wind direction", "Wind speed", "Weather intensity"]);
            expect(weather[2]).toEqual({ label: "Weather intensity", value: "no weather", disabled: true });
            expect(await fieldInfo("Weather intensity")).toBe("No weather emitter in this scene");
            expect(await emitters()).toEqual(["smoke", "fire", "firesoft", "sparks"]);
        });
    });
});
