// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { LspGateway } from "./lsp/lspGateway";
import { RoutedLspGateway } from "./lsp/routedLspGateway";
import { projectSubjectKey, projectTitle } from "./panelProject";
import {
    clientSpecs,
    diagnosticCollectionNameFor,
    ownerKey,
    ownsPath,
    type ClientSpec,
} from "./projectClientPlan";
import {
    discoverSetup,
    fileOf,
    WORKSPACE_SETUPS,
    type SetupProject,
    type WorkspaceSetup,
} from "./testing/workspaceSetups";

function specsOf(setup: WorkspaceSetup): ClientSpec[] {
    return clientSpecs(discoverSetup(setup), setup.folders);
}

function clientOf(specs: ClientSpec[], project: SetupProject): ClientSpec {
    const key = ownerKey(specs, fileOf(project, "Data/Xml/Units.xml"));
    const spec = specs.find((s) => s.key === key);
    assert.ok(spec, `no client owns ${project.name}'s files`);
    return spec;
}

/** A gateway over a fake server that answers every request with its own name. */
function serverNamed(name: string): LspGateway {
    return new LspGateway(() => ({
        sendRequest: <T>(method: string) =>
            Promise.resolve(`${name}:${method}` as T),
    }));
}

/**
 * The client's project rules in a window of one project and of two (#280): which server owns a
 * file, what each client publishes under, which project a panel belongs to, and where a request
 * goes. The defects of the multi-project work came from the second shape, which these tests never
 * built.
 */
for (const setup of WORKSPACE_SETUPS) {
    describe(`in a window of ${setup.name}`, () => {
        it("runs one client per project, each owning its project's files", () => {
            const specs = specsOf(setup);

            assert.equal(specs.length, setup.projects.length);
            for (const project of setup.projects) {
                assert.equal(clientOf(specs, project).name, project.name);
            }
        });

        it("gives every client a diagnostic collection of its own", () => {
            const names = specsOf(setup).map((s) =>
                diagnosticCollectionNameFor("aet-eaw-edit", s),
            );

            assert.equal(new Set(names).size, names.length);
        });

        it("tells the mod's server about changes to every project it builds on", () => {
            const specs = specsOf(setup);
            const mod = clientOf(specs, setup.projects[0]);

            for (const project of setup.projects) {
                assert.ok(ownsPath(mod, fileOf(project, "Data/Xml/Units.xml")));
            }
        });

        it("keeps one subject of each project apart, and names the project once there are several", () => {
            const specs = specsOf(setup);
            const keys = setup.projects.map((p) =>
                projectSubjectKey(
                    clientOf(specs, p).key,
                    "Data/Text/MasterTextFile_English.dat",
                ),
            );

            assert.equal(new Set(keys).size, keys.length);
            assert.equal(
                projectTitle("Encyclopedia", "Mod", specs.length),
                specs.length > 1 ? "Encyclopedia - Mod" : "Encyclopedia",
            );
        });

        it("sends a request from a project's document to that project's server", async () => {
            const specs = specsOf(setup);
            const servers = new Map(
                specs.map((s) => [s.key, serverNamed(s.name)]),
            );
            let active = "";
            const routed = new RoutedLspGateway(() => {
                const key = ownerKey(specs, active);
                return key === undefined ? undefined : servers.get(key);
            });

            for (const project of setup.projects) {
                active = fileOf(project, "Data/Xml/Units.xml");
                assert.deepEqual(
                    await routed.request<string>("aet/getStoryPlots"),
                    {
                        ok: true,
                        value: `${project.name}:aet/getStoryPlots`,
                    },
                );
            }
        });
    });
}
