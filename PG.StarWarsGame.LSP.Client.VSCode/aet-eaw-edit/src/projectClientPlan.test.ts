// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { discoverProjects } from "./projectDiscovery";
import {
    clientSpecs,
    documentSelectorFor,
    ownsPath,
    planChanges,
    statusText,
    type ClientSpec,
} from "./projectClientPlan";

function pgproj(name: string, references: string[] = []): string {
    return JSON.stringify({
        name,
        directories: { xml: ["data/xml"] },
        projectReferences: references.map((path) => ({ path })),
    });
}

function discover(folders: string[], files: Record<string, string>) {
    const byKey = new Map(
        Object.entries(files).map(([p, t]) => [p.toLowerCase(), t]),
    );
    return discoverProjects({
        folders,
        projectFiles: Object.keys(files),
        read: (path) => byKey.get(path.toLowerCase()),
    });
}

const EAWX = {
    "c:/dev/EaWX/Rev/rev.pgproj": pgproj("Rev", ["../Data/core.pgproj"]),
    "c:/dev/EaWX/FotR/fotr.pgproj": pgproj("FotR", ["../Data/core.pgproj"]),
    "c:/dev/EaWX/Data/core.pgproj": pgproj("Core"),
};

/**
 * One language server per open project (#229): which clients to run, what each one covers, and
 * what changes when the workspace does.
 */
describe("clientSpecs", () => {
    /** One project keeps today's client exactly: every file, the first folder as its root. */
    it("runs one unrestricted client for a single project, rooted at the first folder", () => {
        const folders = ["c:/mods/MyMod"];
        const specs = clientSpecs(
            discover(folders, {
                "c:/mods/MyMod/mymod.pgproj": pgproj("My Mod"),
            }),
            folders,
        );

        assert.equal(specs.length, 1);
        assert.equal(specs[0].workspaceRoot, "c:/mods/MyMod");
        assert.equal(specs[0].logDirectory, "c:/mods/MyMod");
        assert.equal(specs[0].projectPath, "c:/mods/MyMod/mymod.pgproj");
        assert.equal(specs[0].roots, undefined);
    });

    it("still runs one client for a folder with no project file, as today", () => {
        const folders = ["c:/notes"];
        const specs = clientSpecs(discover(folders, {}), folders);

        assert.equal(specs.length, 1);
        assert.equal(specs[0].projectPath, undefined);
        assert.equal(specs[0].workspaceRoot, "c:/notes");
    });

    it("runs no client for an empty window", () => {
        assert.deepEqual(clientSpecs(discover([], {}), []), []);
    });

    it("runs one client per project when there are several, each over its own directories", () => {
        const folders = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR"];
        const specs = clientSpecs(discover(folders, EAWX), folders);

        assert.deepEqual(
            specs.map((s) => [
                s.name,
                s.workspaceRoot,
                s.logDirectory,
                s.roots,
            ]),
            [
                [
                    "Rev",
                    "c:/dev/EaWX/Rev",
                    "c:/dev/EaWX/Rev",
                    ["c:/dev/EaWX/Rev", "c:/dev/EaWX/Data"],
                ],
                [
                    "FotR",
                    "c:/dev/EaWX/FotR",
                    "c:/dev/EaWX/FotR",
                    ["c:/dev/EaWX/FotR"],
                ],
            ],
        );
        assert.deepEqual(specs[1].watched, [
            "c:/dev/EaWX/FotR",
            "c:/dev/EaWX/Data",
        ]);
    });
});

describe("clientSpecs canonical spelling", () => {
    it("spells each root as the disk does", () => {
        const folders = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR"];
        const onDisk = (d: string) => d.replace("/Data", "/DATA");

        const [rev] = clientSpecs(discover(folders, EAWX), folders, onDisk);

        assert.deepEqual(rev.roots, ["c:/dev/EaWX/Rev", "c:/dev/EaWX/DATA"]);
    });
});

describe("planChanges", () => {
    const folders = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR"];
    const both = clientSpecs(discover(folders, EAWX), folders);
    const running = (specs: ClientSpec[]) =>
        new Map(specs.map((s) => [s.key, s.signature]));

    it("changes nothing when the projects are the same", () => {
        const plan = planChanges(running(both), both);

        assert.deepEqual(plan, { start: [], stop: [] });
    });

    it("starts only the project that appeared", () => {
        const three = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR", "c:/dev/EaWX/TR"];
        const next = clientSpecs(
            discover(three, {
                ...EAWX,
                "c:/dev/EaWX/TR/tr.pgproj": pgproj("TR", [
                    "../Data/core.pgproj",
                ]),
            }),
            three,
        );

        const plan = planChanges(running(both), next);

        assert.deepEqual(
            plan.start.map((s) => s.name),
            ["TR"],
        );
        assert.deepEqual(plan.stop, []);
    });

    it("stops only the project that went", () => {
        const fewer = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR", "c:/dev/EaWX/TR"];
        const three = clientSpecs(
            discover(fewer, {
                ...EAWX,
                "c:/dev/EaWX/TR/tr.pgproj": pgproj("TR", [
                    "../Data/core.pgproj",
                ]),
            }),
            fewer,
        );

        const plan = planChanges(running(three), both);

        assert.deepEqual(plan.start, []);
        assert.deepEqual(plan.stop, [three[2].key]);
    });

    it("restarts a project whose directories changed", () => {
        const fotrFirst = ["c:/dev/EaWX/FotR", "c:/dev/EaWX/Rev"];
        const next = clientSpecs(discover(fotrFirst, EAWX), fotrFirst);

        const plan = planChanges(running(both), next);

        // The shared dependency moved from Rev to FotR, so both servers' document selectors change.
        assert.deepEqual(plan.stop.sort(), both.map((s) => s.key).sort());
        assert.equal(plan.start.length, 2);
    });
});

describe("documentSelectorFor", () => {
    it("selects every file of the three languages for an unrestricted client", () => {
        const [spec] = clientSpecs(
            discover(["c:/m"], { "c:/m/m.pgproj": pgproj("M") }),
            ["c:/m"],
        );

        assert.deepEqual(documentSelectorFor(spec), [
            { scheme: "file", language: "xml" },
            { scheme: "file", language: "lua" },
            { scheme: "file", language: "plaintext", pattern: "**/*.txt" },
        ]);
    });

    it("limits a project's client to its own roots", () => {
        const folders = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR"];
        const [, fotr] = clientSpecs(discover(folders, EAWX), folders);

        assert.deepEqual(documentSelectorFor(fotr), [
            { scheme: "file", language: "xml", pattern: "c:/dev/EaWX/FotR/**" },
            { scheme: "file", language: "lua", pattern: "c:/dev/EaWX/FotR/**" },
            {
                scheme: "file",
                language: "plaintext",
                pattern: "c:/dev/EaWX/FotR/**/*.txt",
            },
        ]);
    });
});

describe("ownsPath", () => {
    const folders = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR"];
    const [rev, fotr] = clientSpecs(discover(folders, EAWX), folders);

    it("gives a file change to the servers that watch its directory", () => {
        assert.equal(
            ownsPath(rev, "C:\\dev\\EaWX\\Data\\Data\\XML\\units.xml"),
            true,
        );
        assert.equal(
            ownsPath(fotr, "c:/dev/EaWX/Data/Data/XML/units.xml"),
            true,
        );
        assert.equal(
            ownsPath(fotr, "c:/dev/EaWX/Rev/Data/XML/units.xml"),
            false,
        );
    });

    it("gives every change to an unrestricted client", () => {
        const [single] = clientSpecs(
            discover(["c:/m"], { "c:/m/m.pgproj": pgproj("M") }),
            ["c:/m"],
        );

        assert.equal(ownsPath(single, "d:/elsewhere/a.xml"), true);
    });
});

describe("statusText", () => {
    it("reads exactly as before for one server", () => {
        assert.equal(
            statusText([{ name: "M", state: "starting" }]),
            "$(loading~spin) EaWEdit LSP: Starting...",
        );
        assert.equal(
            statusText([{ name: "M", state: "indexing" }]),
            "$(loading~spin) EaWEdit LSP: Indexing workspace...",
        );
        assert.equal(
            statusText([{ name: "M", state: "ready" }]),
            "$(check) EaWEdit LSP",
        );
        assert.equal(
            statusText([{ name: "M", state: "failed" }]),
            "$(error) EaWEdit LSP: Failed to start",
        );
    });

    /** A declined project migration stops the server; the bar must not keep reading as running. */
    it("says a stopped server is stopped", () => {
        assert.equal(
            statusText([{ name: "M", state: "stopped" }]),
            "$(circle-slash) EaWEdit LSP: Stopped",
        );
    });

    it("counts the projects indexed when there are several", () => {
        assert.equal(
            statusText([
                { name: "Rev", state: "ready" },
                { name: "FotR", state: "indexing" },
                { name: "TR", state: "starting" },
            ]),
            "$(loading~spin) EaWEdit LSP: 1 of 3 projects indexed",
        );
        assert.equal(
            statusText([
                { name: "Rev", state: "ready" },
                { name: "FotR", state: "ready" },
            ]),
            "$(check) EaWEdit LSP: 2 projects indexed",
        );
    });

    it("names a failure among several", () => {
        assert.equal(
            statusText([
                { name: "Rev", state: "ready" },
                { name: "FotR", state: "failed" },
            ]),
            "$(error) EaWEdit LSP: 1 of 2 projects indexed, 1 failed",
        );
    });
});
