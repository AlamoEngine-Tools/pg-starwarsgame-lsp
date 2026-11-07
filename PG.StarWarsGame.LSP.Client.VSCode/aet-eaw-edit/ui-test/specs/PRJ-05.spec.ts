// PRJ-05: two projects' files of one name both show their diagnostics, each tagged with its project.
import path from "node:path";
import { expect } from "@wdio/globals";
import { eaw, EAW, findings, findingOn, foc, FOC, insertLineAfter, projectsReady, revertAndClose } from "../protocol.js";

const eawFile = path.join(eaw, "Data", "Xml", "Spaceunitscapital.xml");
const focFile = path.join(foc, "Data", "Xml", "Spaceunitscapital.xml");

describe("PRJ-05", () => {
    it("shows the warning in both files, each from its own project, once", async () => {
        await projectsReady(2);
        try {
            expect(await insertLineAfter(eawFile, 391, "<Zzq_Not_A_Tag>1</Zzq_Not_A_Tag>")).toContain('Name="Star_Destroyer"');
            expect(await insertLineAfter(focFile, 406, "<Zzq_Not_A_Tag>1</Zzq_Not_A_Tag>")).toContain('Name="Star_Destroyer"');
            const inEaw = await findingOn(eawFile, 392, "aetswg-006-0012");
            const inFoc = await findingOn(focFile, 407, "aetswg-006-0012");
            expect(inEaw.message.startsWith("<Zzq_Not_A_Tag> is not a known SpaceUnit tag.")).toBe(true);
            expect(inEaw.source).toBe(`aetswg (${EAW})`);
            expect(inFoc.source).toBe(`aetswg (${FOC})`);
            expect((await findings(eawFile)).filter((f) => f.line === 392 && f.code === "aetswg-006-0012")).toHaveLength(1);
            expect((await findings(focFile)).filter((f) => f.line === 407 && f.code === "aetswg-006-0012")).toHaveLength(1);
        } finally {
            await revertAndClose(eawFile);
            await revertAndClose(focFile);
        }
    });
});
