// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { groupProjects, LocProjectInfo } from './localisationTreeModel';

function project(overrides: Partial<LocProjectInfo> = {}): LocProjectInfo {
    return {
        label: 'MasterTextFile.csv',
        filePath: '/ws/data/text/MasterTextFile.csv',
        resourceType: 'Csv',
        projectName: 'Root',
        rank: 1,
        category: 'text',
        ...overrides,
    };
}

describe('groupProjects', () => {
    it('returns nothing for an empty workspace', () => {
        assert.deepEqual(groupProjects([]), []);
    });

    // Text first: it is what a modder opens every day, and credits are a rarity.
    it('puts text files before credits files', () => {
        const nodes = groupProjects([
            project({ label: 'creditstext.csv', category: 'credits' }),
            project({ label: 'MasterTextFile.csv' }),
        ]);

        assert.deepEqual(nodes.map(n => n.label), ['Text files', 'Credits files']);
    });

    // An empty group is noise - a workspace with no credits file should not show a credits header.
    it('omits a group with no files', () => {
        const nodes = groupProjects([project()]);

        assert.equal(nodes.length, 1);
        assert.equal(nodes[0].label, 'Text files');
    });

    // The common case is one project; a layer level there would be a node you always expand past.
    it('lists files directly under the group when one project contributes', () => {
        const nodes = groupProjects([
            project({ label: 'a.csv' }),
            project({ label: 'b.csv' }),
        ]);

        assert.deepEqual(nodes[0].children.map(c => c.kind), ['file', 'file']);
    });

    it('inserts a layer level when several projects contribute to one group', () => {
        const nodes = groupProjects([
            project({ label: 'root.csv', projectName: 'Root', rank: 1 }),
            project({ label: 'dep.csv', projectName: 'Core', rank: 0 }),
        ]);

        const layers = nodes[0].children;
        assert.deepEqual(layers.map(l => l.kind), ['layer', 'layer']);
        // Highest rank first: the root project's own files are what the user is editing.
        assert.deepEqual(layers.map(l => l.label), ['Root', 'Core']);
    });

    it('decides the layer level per group, not for the whole tree', () => {
        const nodes = groupProjects([
            project({ label: 'root.csv', projectName: 'Root', rank: 1 }),
            project({ label: 'dep.csv', projectName: 'Core', rank: 0 }),
            project({ label: 'creditstext.csv', category: 'credits', projectName: 'Root', rank: 1 }),
        ]);

        assert.equal(nodes[0].children[0].kind, 'layer'); // two projects
        assert.equal(nodes[1].children[0].kind, 'file');  // one project
    });

    it('sorts files by label within their parent', () => {
        const nodes = groupProjects([
            project({ label: 'zeta.csv' }),
            project({ label: 'alpha.csv' }),
        ]);

        assert.deepEqual(nodes[0].children.map(c => c.label), ['alpha.csv', 'zeta.csv']);
    });

    it('carries the project through to the file node so a click can open it', () => {
        const nodes = groupProjects([project({ filePath: '/ws/x.csv' })]);
        const file = nodes[0].children[0];

        assert.equal(file.kind, 'file');
        assert.equal(file.kind === 'file' ? file.project.filePath : '', '/ws/x.csv');
    });

    // An unrecognised category must not vanish from the tree: a file the user cannot see is a file
    // they cannot fix.
    it('treats an unknown category as text rather than dropping the file', () => {
        const nodes = groupProjects([project({ category: 'something-new' })]);

        assert.equal(nodes[0].label, 'Text files');
        assert.equal(nodes[0].children.length, 1);
    });
});
