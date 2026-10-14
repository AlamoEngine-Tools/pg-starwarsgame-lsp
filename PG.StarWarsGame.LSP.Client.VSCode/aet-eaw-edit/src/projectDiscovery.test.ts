// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { discoverProjects } from "./projectDiscovery";

/** A project file with the given references and directories, as a modder writes one. */
function pgproj(
    name: string,
    references: string[] = [],
    xml: string[] = ["data/xml"],
): string {
    return JSON.stringify({
        name,
        directories: { xml, scripts: ["data/scripts"], art: ["data/art"] },
        projectReferences: references.map((path) => ({ path })),
        _type: "aetswg.ModProject",
        _typeVersion: "aetswg-1.0.0",
    });
}

/** Discovery over an in-memory set of project files, the way the extension feeds it from a glob. */
function discover(
    folders: string[],
    files: Record<string, string>,
    found = Object.keys(files),
) {
    const byKey = new Map(
        Object.entries(files).map(([p, t]) => [
            p.replace(/\\/g, "/").toLowerCase(),
            t,
        ]),
    );
    return discoverProjects({
        folders,
        projectFiles: found,
        read: (path) => byKey.get(path.replace(/\\/g, "/").toLowerCase()),
    });
}

describe("discoverProjects", () => {
    it("finds the one project of a single-project folder", () => {
        const result = discover(["c:/mods/MyMod"], {
            "c:/mods/MyMod/mymod.pgproj": pgproj("My Mod"),
        });

        assert.equal(result.projects.length, 1);
        const [project] = result.projects;
        assert.equal(project.name, "My Mod");
        assert.equal(project.path, "c:/mods/MyMod/mymod.pgproj");
        assert.equal(project.directory, "c:/mods/MyMod");
        assert.deepEqual(project.roots, ["c:/mods/MyMod"]);
        assert.deepEqual(result.foldersWithoutProject, []);
    });

    it("finds sibling projects under one folder, in path order", () => {
        const result = discover(["c:/dev/EaWX"], {
            "c:/dev/EaWX/Rev/rev.pgproj": pgproj("Rev"),
            "c:/dev/EaWX/FotR/fotr.pgproj": pgproj("FotR"),
        });

        assert.deepEqual(
            result.projects.map((p) => p.name),
            ["FotR", "Rev"],
        );
    });

    it("counts one project file reached through two folders once", () => {
        const result = discover(
            ["c:/dev/EaWX", "C:\\dev\\EaWX\\Rev"],
            { "c:/dev/EaWX/Rev/rev.pgproj": pgproj("Rev") },
            ["c:/dev/EaWX/Rev/rev.pgproj", "C:\\dev\\EaWX\\Rev\\rev.pgproj"],
        );

        assert.equal(result.projects.length, 1);
    });

    it("reads the project's directories, resolved against its own folder", () => {
        const result = discover(["c:/mods/MyMod"], {
            "c:/mods/MyMod/mymod.pgproj": pgproj(
                "My Mod",
                [],
                ["data/xml", "../Shared/xml"],
            ),
        });

        assert.deepEqual(result.projects[0].directories, [
            "c:/mods/MyMod/data/xml",
            "c:/mods/Shared/xml",
            "c:/mods/MyMod/data/scripts",
            "c:/mods/MyMod/data/art",
        ]);
        assert.deepEqual(result.projects[0].roots, [
            "c:/mods/MyMod",
            "c:/mods/Shared/xml",
        ]);
    });

    it("reads a project file with comments and trailing commas, as the server does", () => {
        const text = `{
            // the mod
            "name": "Commented",
            "directories": { "xml": ["data/xml",], },
            "projectReferences": [],
        }`;
        const result = discover(["c:/mods/MyMod"], {
            "c:/mods/MyMod/mymod.pgproj": text,
        });

        assert.equal(result.projects[0].name, "Commented");
        assert.deepEqual(result.projects[0].directories, [
            "c:/mods/MyMod/data/xml",
        ]);
    });

    it("walks the reference closure, dependencies first, through a cycle without looping", () => {
        const result = discover(
            ["c:/dev/EaWX/FotR"],
            {
                "c:/dev/EaWX/FotR/fotr.pgproj": pgproj("FotR", [
                    "../CoreSaga/coresaga.pgproj",
                ]),
                "c:/dev/EaWX/CoreSaga/coresaga.pgproj": pgproj("CoreSaga", [
                    "../Data/core.pgproj",
                ]),
                "c:/dev/EaWX/Data/core.pgproj": pgproj("Core", [
                    "../FotR/fotr.pgproj",
                ]),
            },
            ["c:/dev/EaWX/FotR/fotr.pgproj"],
        );

        assert.deepEqual(result.projects[0].closure, [
            "c:/dev/EaWX/Data/core.pgproj",
            "c:/dev/EaWX/CoreSaga/coresaga.pgproj",
        ]);
    });

    it("serves a project file inside another project's directory as that project's dependency", () => {
        const result = discover(["c:/mods/MyMod"], {
            "c:/mods/MyMod/mymod.pgproj": pgproj("My Mod", [
                "deps/Core/core.pgproj",
            ]),
            "c:/mods/MyMod/deps/Core/core.pgproj": pgproj("Core"),
        });

        assert.deepEqual(
            result.projects.map((p) => p.name),
            ["My Mod"],
        );
        assert.deepEqual(result.nested, [
            "c:/mods/MyMod/deps/Core/core.pgproj",
        ]);
        assert.equal(
            result.ownerOf("c:/mods/MyMod/deps/Core/data/xml/units.xml")?.name,
            "My Mod",
        );
    });

    it("gives a dependency opened as a folder its own project", () => {
        const result = discover(["c:/dev/EaWX/Rev", "c:/dev/EaWX/Data"], {
            "c:/dev/EaWX/Rev/rev.pgproj": pgproj("Rev", [
                "../Data/core.pgproj",
            ]),
            "c:/dev/EaWX/Data/core.pgproj": pgproj("Core"),
        });

        assert.deepEqual(
            result.projects.map((p) => p.name),
            ["Rev", "Core"],
        );
        assert.deepEqual(result.projects[0].libraries, []);
        assert.equal(
            result.ownerOf("c:/dev/EaWX/Data/data/xml/units.xml")?.name,
            "Core",
        );
        assert.equal(
            result.ownerOf("c:/dev/EaWX/Rev/data/xml/units.xml")?.name,
            "Rev",
        );
    });

    it("leaves another open project's files to it when a declared directory reaches into them", () => {
        const result = discover(["c:/dev/EaWX/Rev", "c:/dev/EaWX/Data"], {
            "c:/dev/EaWX/Rev/rev.pgproj": pgproj(
                "Rev",
                [],
                ["data/xml", "../Data/data/xml"],
            ),
            "c:/dev/EaWX/Data/core.pgproj": pgproj("Core"),
        });

        assert.deepEqual(result.projects[0].roots, ["c:/dev/EaWX/Rev"]);
        assert.equal(
            result.ownerOf("c:/dev/EaWX/Data/data/xml/units.xml")?.name,
            "Core",
        );
    });

    it("assigns a dependency under no folder to the first project that references it, and only to it", () => {
        const result = discover(
            ["c:/dev/EaWX/Rev", "c:/dev/EaWX/FotR"],
            {
                "c:/dev/EaWX/Rev/rev.pgproj": pgproj("Rev", [
                    "../Data/core.pgproj",
                ]),
                "c:/dev/EaWX/FotR/fotr.pgproj": pgproj("FotR", [
                    "../Data/core.pgproj",
                ]),
                "c:/dev/EaWX/Data/core.pgproj": pgproj("Core"),
            },
            ["c:/dev/EaWX/Rev/rev.pgproj", "c:/dev/EaWX/FotR/fotr.pgproj"],
        );

        const [rev, fotr] = result.projects;
        assert.deepEqual(rev.libraries, ["c:/dev/EaWX/Data/core.pgproj"]);
        assert.deepEqual(rev.roots, ["c:/dev/EaWX/Rev", "c:/dev/EaWX/Data"]);
        assert.deepEqual(fotr.libraries, []);
        assert.deepEqual(fotr.roots, ["c:/dev/EaWX/FotR"]);
        assert.equal(
            result.ownerOf("c:/dev/EaWX/Data/data/xml/units.xml")?.name,
            "Rev",
        );
    });

    it("lists a folder with no project file, and contributes nothing for it", () => {
        const result = discover(["c:/mods/MyMod", "c:/notes"], {
            "c:/mods/MyMod/mymod.pgproj": pgproj("My Mod"),
        });

        assert.equal(result.projects.length, 1);
        assert.deepEqual(result.foldersWithoutProject, ["c:/notes"]);
        assert.equal(result.ownerOf("c:/notes/todo.xml"), undefined);
    });

    it("does not count a folder inside a project as a folder without one", () => {
        const result = discover(
            ["c:/mods/MyMod", "c:/mods/MyMod/Data/XML"],
            {
                "c:/mods/MyMod/mymod.pgproj": pgproj("My Mod"),
            },
            ["c:/mods/MyMod/mymod.pgproj"],
        );

        assert.deepEqual(result.foldersWithoutProject, []);
    });

    it("keeps a project file that cannot be read, so the server can report why", () => {
        const result = discover(["c:/mods/MyMod"], {
            "c:/mods/MyMod/mymod.pgproj": "{ not json",
        });

        assert.equal(result.projects.length, 1);
        assert.equal(result.projects[0].name, "mymod");
        assert.deepEqual(result.unreadable, ["c:/mods/MyMod/mymod.pgproj"]);
    });

    it("matches ownership on a directory boundary, not on a name prefix", () => {
        const result = discover(["c:/dev"], {
            "c:/dev/Rev/rev.pgproj": pgproj("Rev"),
            "c:/dev/RevExtra/extra.pgproj": pgproj("Extra"),
        });

        assert.equal(
            result.ownerOf("C:\\dev\\RevExtra\\Data\\XML\\a.xml")?.name,
            "Extra",
        );
        assert.equal(result.ownerOf("c:/dev/Rev/Data/XML/a.xml")?.name, "Rev");
    });
});
