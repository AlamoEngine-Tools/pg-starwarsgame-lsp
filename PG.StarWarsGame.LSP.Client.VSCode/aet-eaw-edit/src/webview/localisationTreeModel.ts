// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Pure shape of the localisation Files tree, kept free of `vscode` so the grouping rules can be
// unit-tested on their own. The view provider is then a thin mapping from these nodes to TreeItems.

/** One localisation file, as `aet/getLocalisationProjects` reports it. */
export interface LocProjectInfo {
    label: string;
    filePath: string;
    resourceType: string;
    projectName: string;
    rank: number;
    /** 'text' or 'credits'; anything else is treated as text rather than hidden. */
    category: string;
}

export type LocTreeNode =
    | { kind: 'group'; label: string; category: string; children: LocTreeNode[] }
    | { kind: 'layer'; label: string; children: LocTreeNode[] }
    | { kind: 'file'; label: string; project: LocProjectInfo; children: LocTreeNode[] };

export const CREDITS_CATEGORY = 'credits';

/**
 * Groups files into "Text files" and "Credits files", with a project level inserted only where it
 * earns its keep.
 *
 * Text comes first because it is what a modder opens daily; credits are a rarity. A group with no
 * files is omitted rather than shown empty, and the project level appears only when more than one
 * project contributes to that group - otherwise it is a node you always expand past.
 */
export function groupProjects(projects: LocProjectInfo[]): LocTreeNode[] {
    const credits = projects.filter(p => p.category === CREDITS_CATEGORY);
    // Anything not explicitly credits is text: an unrecognised category must still be reachable,
    // since a file the user cannot see is a file they cannot fix.
    const text = projects.filter(p => p.category !== CREDITS_CATEGORY);

    const groups: LocTreeNode[] = [];
    if (text.length > 0) { groups.push(group('Text files', 'text', text)); }
    if (credits.length > 0) { groups.push(group('Credits files', CREDITS_CATEGORY, credits)); }
    return groups;
}

function group(label: string, category: string, projects: LocProjectInfo[]): LocTreeNode {
    return { kind: 'group', label, category, children: childrenOf(projects) };
}

function childrenOf(projects: LocProjectInfo[]): LocTreeNode[] {
    const byProject = new Map<string, LocProjectInfo[]>();
    for (const project of projects) {
        const existing = byProject.get(project.projectName);
        if (existing) { existing.push(project); } else { byProject.set(project.projectName, [project]); }
    }

    if (byProject.size <= 1) { return projects.map(fileNode).sort(byLabel); }

    // Highest rank first: the root project's own files are the ones being edited, and a
    // dependency's are usually read-only in practice.
    return [...byProject.entries()]
        .sort((a, b) => rankOf(b[1]) - rankOf(a[1]))
        .map(([name, owned]) => ({
            kind: 'layer' as const,
            label: name,
            children: owned.map(fileNode).sort(byLabel),
        }));
}

function fileNode(project: LocProjectInfo): LocTreeNode {
    return { kind: 'file', label: project.label, project, children: [] };
}

function rankOf(projects: LocProjectInfo[]): number {
    return projects[0]?.rank ?? 0;
}

function byLabel(a: LocTreeNode, b: LocTreeNode): number {
    return a.label.localeCompare(b.label);
}
