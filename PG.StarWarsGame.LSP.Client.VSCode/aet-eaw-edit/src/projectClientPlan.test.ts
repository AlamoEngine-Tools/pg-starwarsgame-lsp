// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";
import * as l10n from "@vscode/l10n";

import { discoverProjects } from "./projectDiscovery";
import {
    clientSpecs,
    diagnosticCollectionNameFor,
    documentSelectorFor,
    ownerKey,
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

    /**
     * A panel is bound to its project's key for its whole life (#276). The single project's client
     * was filed under a fixed key and, once a second project started, under its project file: the
     * panels opened before that talked to no server.
     */
    it("files a single project under the key it keeps when a second project starts", () => {
        const one = ["c:/dev/EaWX/FotR"];
        const two = ["c:/dev/EaWX/FotR", "c:/dev/EaWX/Rev"];
        const single = clientSpecs(
            discover(one, { "c:/dev/EaWX/FotR/fotr.pgproj": pgproj("FotR") }),
            one,
        );
        const several = clientSpecs(
            discover(two, {
                "c:/dev/EaWX/FotR/fotr.pgproj": pgproj("FotR"),
                "c:/dev/EaWX/Rev/rev.pgproj": pgproj("Rev"),
            }),
            two,
        );

        assert.ok(several.some((s) => s.key === single[0].key));
    });

    it("runs one client for a folder laid out like a mod with no project file", () => {
        const folders = ["c:/mods/Legacy"];
        const specs = clientSpecs(
            discover(folders, {}),
            folders,
            undefined,
            (directory) => directory === "c:/mods/Legacy",
        );

        assert.equal(specs.length, 1);
        assert.equal(specs[0].projectPath, undefined);
        assert.equal(specs[0].workspaceRoot, "c:/mods/Legacy");
    });

    /** Without lsp.enabled, any folder with an xml or lua file activates the extension. */
    it("runs no client for a folder that is neither a project nor a mod", () => {
        const folders = ["c:/notes"];

        assert.deepEqual(
            clientSpecs(discover(folders, {}), folders, undefined, () => false),
            [],
        );
        assert.deepEqual(clientSpecs(discover(folders, {}), folders), []);
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

describe("diagnosticCollectionNameFor", () => {
    it("gives every project's client a collection of its own", () => {
        const folders = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR"];
        const [rev, fotr] = clientSpecs(discover(folders, EAWX), folders);

        assert.notEqual(
            diagnosticCollectionNameFor("aet.pg.swg.lsp", rev),
            diagnosticCollectionNameFor("aet.pg.swg.lsp", fotr),
        );
    });

    it("never reuses the bare client id, which every client shares", () => {
        const [spec] = clientSpecs(
            discover(["c:/m"], { "c:/m/m.pgproj": pgproj("M") }),
            ["c:/m"],
        );

        assert.notEqual(
            diagnosticCollectionNameFor("aet.pg.swg.lsp", spec),
            "aet.pg.swg.lsp",
        );
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

        const root = "[cC]:/[dD][eE][vV]/[eE][aA][wW][xX]/[fF][oO][tT][rR]";
        assert.deepEqual(documentSelectorFor(fotr), [
            { scheme: "file", language: "xml", pattern: `${root}/**` },
            { scheme: "file", language: "lua", pattern: `${root}/**` },
            {
                scheme: "file",
                language: "plaintext",
                pattern: `${root}/**/*.txt`,
            },
        ]);
    });
});

describe("documentSelectorFor case", () => {
    /**
     * Measured in a live window: VS Code matches a selector's pattern against its own spelling of
     * the path, case-sensitively. A root spelled from the disk ("C:/...") selected nothing against
     * VS Code's lower-case drive letter, and a file opened under another spelling of its path - a
     * definition the server handed out (#291) - reached no project's server and got no
     * diagnostics. A project owns a path the way the server compares one: ignoring case.
     */
    it("matches the root in any case", () => {
        const spec = {
            ...clientSpecs(
                discover(["c:/m", "c:/n"], {
                    "c:/m/m.pgproj": pgproj("M"),
                    "c:/n/n.pgproj": pgproj("N"),
                }),
                ["c:/m", "c:/n"],
            )[0],
            roots: ["C:\\Mods\\M"],
        };

        assert.equal(
            documentSelectorFor(spec)[0].pattern,
            "[cC]:/[mM][oO][dD][sS]/[mM]/**",
        );
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

describe("ownerKey", () => {
    const folders = ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR"];
    const specs = clientSpecs(discover(folders, EAWX), folders);

    it("names the client whose roots hold the file", () => {
        assert.equal(
            ownerKey(specs, "C:\\dev\\EaWX\\FotR\\Data\\XML\\a.xml"),
            specs[1].key,
        );
        assert.equal(
            ownerKey(specs, "c:/dev/EaWX/Data/Data/XML/a.xml"),
            specs[0].key,
        );
    });

    it("names no client for a file outside every project", () => {
        assert.equal(ownerKey(specs, "c:/elsewhere/a.xml"), undefined);
    });

    it("names the single client for any file", () => {
        const [single] = clientSpecs(
            discover(["c:/m"], { "c:/m/m.pgproj": pgproj("M") }),
            ["c:/m"],
        );

        assert.equal(ownerKey([single], "d:/elsewhere/a.xml"), single.key);
    });
});

describe("statusText", () => {
    // The status bar reads in the editor's language: its texts come from the catalogue.
    it("reads its texts from the catalogue", () => {
        l10n.config({
            contents: {
                "EaWEdit LSP: Stopped": "TRANSLATED STOPPED",
                "EaWEdit LSP: {0} projects indexed": "TRANSLATED {0}",
            },
        });
        try {
            assert.equal(
                statusText([{ name: "A", state: "stopped" }]),
                "$(circle-slash) TRANSLATED STOPPED",
            );
            assert.equal(
                statusText([
                    { name: "A", state: "ready" },
                    { name: "B", state: "ready" },
                ]),
                "$(check) TRANSLATED 2",
            );
        } finally {
            l10n.config({ contents: {} });
        }
    });

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
