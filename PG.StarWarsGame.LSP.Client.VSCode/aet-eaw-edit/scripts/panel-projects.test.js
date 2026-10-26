// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Every panel belongs to exactly one project (#276).
//
// A panel that reached a server any other way than through its project - a gateway passed in on
// its own, or the window's routed gateway, which follows whatever editor is in front - showed one
// project's data and wrote through another's server once a second project ran. A panel kept as a
// single static instance served every project from one tab, reloading through whichever had opened
// it last. Both shapes are what this holds the panel sources against.

const assert = require("node:assert/strict");
const {describe, it} = require("node:test");
const fs = require("node:fs");
const path = require("node:path");

const src = process.env.PANEL_PROJECTS_SRC ?? path.join(__dirname, "..", "src");

/** The panel classes: every class extending the shared host, with its file's text. */
function panelClasses() {
    return fs
        .readdirSync(src)
        .filter((name) => name.endsWith(".ts") && !name.endsWith(".test.ts"))
        .flatMap((name) => {
            const text = fs.readFileSync(path.join(src, name), "utf8");
            return [...text.matchAll(/class (\w+) extends WebviewPanelHost\b/g)].map((m) => ({
                name: m[1],
                file: name,
                text,
            }));
        });
}

describe("panel projects", () => {
    it("finds the panels", () => {
        assert.ok(panelClasses().length >= 6);
    });

    it("gives no panel a gateway of its own - only its project's", () => {
        assert.deepEqual(
            panelClasses()
                .filter((panel) => /\bLspGateway\b/.test(panel.text))
                .map((panel) => panel.name),
            [],
        );
    });

    it("keeps every panel in a registry filed by project, never as a single instance", () => {
        assert.deepEqual(
            panelClasses()
                .filter(
                    (panel) =>
                        !panel.text.includes(`new PanelRegistry<${panel.name}>`) ||
                        new RegExp(`static \\w+\\s*:\\s*${panel.name}\\s*\\|\\s*undefined`).test(panel.text),
                )
                .map((panel) => panel.name),
            [],
        );
    });

    it("keys a panel's per-subject state by project and subject, like its registry", () => {
        // A static map keyed by the subject alone served two projects' same-named objects from one
        // entry: the camera and selection left on one greeted the other.
        for (const panel of panelClasses()) {
            const staticMaps = [...panel.text.matchAll(/static readonly (\w+) = new Map<string/g)].map((m) => m[1]);
            for (const map of staticMaps) {
                const keyed = new RegExp(`${map}\\.(get|set|has|delete)\\(\\s*this\\.stateKey\\b`).test(panel.text);
                if (keyed) {
                    assert.match(
                        panel.text,
                        /stateKey\(\): string \{\s*return projectSubjectKey\(this\.project\.key,/,
                        `${panel.name}.${map} is keyed without the project`,
                    );
                }
            }
        }
    });

    it("builds a panel's project in one place", () => {
        const extension = fs.readFileSync(path.join(src, "extension.ts"), "utf8");
        // The one literal of a project is panelProject()'s; a second would be a panel bound some
        // other way, and the routed gateway handed to one is exactly the bug. A panel opened while
        // no project runs gets the pending gateway, which binds to the one project that starts and
        // never to the editor in front (#318).
        assert.equal(
            [...extension.matchAll(/\bgateway: resolved \? gatewayFor\(resolved\) : pendingProjectGateway\b/g)].length,
            1,
        );
        assert.equal([...extension.matchAll(/\bgateway: gatewayFor\(/g)].length, 0);
        assert.doesNotMatch(extension, /\bgateway: lsp\b/);
    });

    it("tells every gateway about a route change, through one event", () => {
        const extension = fs.readFileSync(path.join(src, "extension.ts"), "utf8");
        // Calling routesChanged() on one gateway by hand is how the project gateways were missed.
        assert.doesNotMatch(extension, /\.routesChanged\(\)/);
        assert.ok([...extension.matchAll(/\brouteChanges\.fire\(\)/g)].length >= 2);
        // Every routed gateway is built on the shared routes.
        const constructions = [...extension.matchAll(/new RoutedLspGateway\(([\s\S]*?)\);/g)];
        assert.ok(constructions.length >= 3);
        for (const [, args] of constructions) {
            assert.match(args, /\brouteChanges\b/);
        }
        // The pending gateway routes to the single project only, never through the editor.
        assert.match(
            extension,
            /const pendingProjectGateway = new RoutedLspGateway\(\s*\(\) =>\s*\(?clients\.size === 1 \? primaryClient\(\)\?\.gateway : undefined\)?,/,
        );
    });
});
