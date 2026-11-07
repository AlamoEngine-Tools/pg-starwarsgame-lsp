// PRJ-11: a dependency outside every folder belongs to the first project referencing it; no file is
// diagnosed twice.
import { expect } from "@wdio/globals";
import { findings, findingOn, openFile, outputChannels, projectsReady } from "../protocol.js";

const shared = "C:\\temp\\aet-test-PRJ-multi\\shared\\Data\\Xml\\SharedUnits.xml";

describe("PRJ-11", () => {
    before(async () => {
        await projectsReady(2);
    });

    it("diagnoses the shared file once, from Alpha, naming the layer", async () => {
        await openFile(shared);
        const warning = await findingOn(shared, 3, "aetswg-006-0012");
        expect(warning.message.startsWith("<Zzq_Not_A_Tag> is not a known SpaceUnit tag.")).toBe(true);
        expect(warning.source).toBe("aetswg (Alpha)");
        expect(warning.related).toContain("Layer - Shared");
        const all = await findings(shared);
        expect(all.filter((f) => f.line === 3 && f.code === "aetswg-006-0012")).toHaveLength(1);
        expect(all.filter((f) => f.source === "aetswg (Beta)")).toEqual([]);
    });

    it("watches the shared folder for both projects", () => {
        const log = outputChannels().get("EaWEdit") ?? "";
        for (const project of ["Alpha", "Beta"])
            expect(log.toLowerCase()).toMatch(
                new RegExp(`\\[${project.toLowerCase()}\\] watching 1 project director\\(y/ies\\) outside the workspace: .*aet-test-prj-multi[\\\\/]shared`),
            );
    });
});
