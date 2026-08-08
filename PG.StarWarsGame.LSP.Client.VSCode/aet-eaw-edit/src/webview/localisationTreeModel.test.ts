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

function nls(label: string, language: string, overrides: Partial<LocProjectInfo> = {}): LocProjectInfo {
    return project({
        label,
        filePath: `/ws/data/text/${label}`,
        resourceType: 'Nls',
        language,
        ...overrides,
    });
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

    // ── language siblings ────────────────────────────────────────────────────

    // A .properties or .dat project is one logical file split across languages. Listed flat, the
    // parts read as unrelated files that happen to sort next to each other.
    it('gathers language siblings under one entry', () => {
        const nodes = groupProjects([
            nls('mastertextfile_english.properties', 'ENGLISH'),
            nls('mastertextfile_german.properties', 'GERMAN'),
        ]);

        const children = nodes[0].children;
        assert.equal(children.length, 1);
        assert.equal(children[0].kind, 'fileset');
        assert.equal(children[0].label, 'mastertextfile.properties');
        assert.deepEqual(children[0].children.map(c => c.label), ['ENGLISH', 'GERMAN']);
    });

    // The tree opens a text set as one merged table but not a credits one - credits rows are
    // addressed by position and duplicate keys are the format, so merging them by key is undefined.
    // It can only tell them apart if the set says which it is.
    it('carries the category up onto the set', () => {
        const nodes = groupProjects([
            nls('creditstext_english.properties', 'ENGLISH', { category: 'credits' }),
            nls('creditstext_german.properties', 'GERMAN', { category: 'credits' }),
        ]);

        const set = nodes[0].children[0];
        assert.equal(set.kind, 'fileset');
        assert.equal(set.kind === 'fileset' ? set.category : '', 'credits');
    });

    // The set is named for the file it would be if the format could hold every language - the
    // folder it sits in is not part of that name.
    it('names a set without the folder it lives in', () => {
        const nodes = groupProjects([
            nls('mastertextfile_english.properties', 'ENGLISH',
                { filePath: '/mods/my mod/data/text/mastertextfile_english.properties' }),
            nls('mastertextfile_german.properties', 'GERMAN',
                { filePath: '/mods/my mod/data/text/mastertextfile_german.properties' }),
        ]);

        assert.equal(nodes[0].children[0].label, 'mastertextfile.properties');
    });

    it('names the languages a set holds so the tree can describe it', () => {
        const nodes = groupProjects([
            nls('mastertextfile_german.properties', 'GERMAN'),
            nls('mastertextfile_english.properties', 'ENGLISH'),
        ]);

        const set = nodes[0].children[0];
        assert.deepEqual(set.kind === 'fileset' ? set.languages : [], ['ENGLISH', 'GERMAN']);
    });

    // One file is not a set - a parent there is a node you always expand past, the same reason the
    // project level only appears when more than one project contributes.
    it('leaves a lone single-language file flat', () => {
        const nodes = groupProjects([nls('mastertextfile_english.properties', 'ENGLISH')]);

        assert.equal(nodes[0].children[0].kind, 'file');
        assert.equal(nodes[0].children[0].label, 'mastertextfile_english.properties');
    });

    // Grouping is by name, so two stems in one folder stay apart.
    it('keeps different stems in separate sets', () => {
        const nodes = groupProjects([
            nls('mastertextfile_english.properties', 'ENGLISH'),
            nls('mastertextfile_german.properties', 'GERMAN'),
            nls('extratext_english.properties', 'ENGLISH'),
            nls('extratext_german.properties', 'GERMAN'),
        ]);

        assert.deepEqual(nodes[0].children.map(c => c.label),
            ['extratext.properties', 'mastertextfile.properties']);
    });

    // CSV and XML hold every language in one file, so they have no siblings to gather.
    it('never groups a multi-language format', () => {
        const nodes = groupProjects([
            project({ label: 'a_english.csv', filePath: '/ws/data/text/a_english.csv' }),
            project({ label: 'a_german.csv', filePath: '/ws/data/text/a_german.csv' }),
        ]);

        assert.deepEqual(nodes[0].children.map(c => c.kind), ['file', 'file']);
    });

    // Two projects can each ship a mastertextfile_*.properties; merging them would hide one layer
    // behind the other.
    it('does not merge sets across projects', () => {
        const nodes = groupProjects([
            nls('mastertextfile_english.properties', 'ENGLISH', { projectName: 'Root', rank: 2 }),
            nls('mastertextfile_german.properties', 'GERMAN', { projectName: 'Root', rank: 2 }),
            nls('mastertextfile_english.properties', 'ENGLISH',
                { projectName: 'Dep', rank: 1, filePath: '/dep/data/text/mastertextfile_english.properties' }),
            nls('mastertextfile_french.properties', 'FRENCH',
                { projectName: 'Dep', rank: 1, filePath: '/dep/data/text/mastertextfile_french.properties' }),
        ]);

        const layers = nodes[0].children;
        assert.deepEqual(layers.map(l => l.label), ['Root', 'Dep']);
        for (const layer of layers) {
            assert.equal(layer.children.length, 1);
            assert.equal(layer.children[0].kind, 'fileset');
        }
    });
});
