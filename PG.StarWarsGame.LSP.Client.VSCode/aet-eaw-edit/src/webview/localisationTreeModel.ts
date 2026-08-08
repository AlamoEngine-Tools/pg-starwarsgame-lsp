// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Pure shape of the localisation Files tree, kept free of `vscode` so the grouping rules can be
// unit-tested on their own. The view provider is then a thin mapping from these nodes to TreeItems.

import { LOC_CATEGORY, LocProjectInfo } from '../protocol';

// Re-exported so a consumer of the tree model does not need a second import for the thing every
// node in it carries. The declaration itself lives in ../protocol, with the rest of the contract.
export { LocProjectInfo };

export type LocTreeNode =
    | { kind: 'group'; label: string; category: string; children: LocTreeNode[] }
    | { kind: 'layer'; label: string; children: LocTreeNode[] }
    /** A set of language siblings - one logical file the format forced across several. */
    | {
        kind: 'fileset'; label: string; languages: string[]; category: string;
        children: LocTreeNode[];
    }
    | { kind: 'file'; label: string; project: LocProjectInfo; children: LocTreeNode[] };

/** Alias for the protocol constant, kept so the tree's own rules read in its vocabulary. */
export const CREDITS_CATEGORY = LOC_CATEGORY.credits;

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

    if (byProject.size <= 1) { return gatherSiblings(projects); }

    // Highest rank first: the root project's own files are the ones being edited, and a
    // dependency's are usually read-only in practice.
    return [...byProject.entries()]
        .sort((a, b) => rankOf(b[1]) - rankOf(a[1]))
        .map(([name, owned]) => ({
            kind: 'layer' as const,
            label: name,
            children: gatherSiblings(owned),
        }));
}

/**
 * Collapses each set of language siblings into one entry.
 *
 * A .properties or .dat project is one logical file the format forced across several - listed flat,
 * the parts read as unrelated files that happen to sort next to each other, and nothing says which
 * languages the project actually covers. Only the single-language formats have siblings: CSV and
 * XML carry every language inside one file, and the server leaves their `language` unset, so they
 * fall through untouched.
 *
 * A set of one is left flat, for the same reason the project level only appears when more than one
 * project contributes: a parent with a single child is a node you always expand past.
 */
function gatherSiblings(projects: LocProjectInfo[]): LocTreeNode[] {
    const sets = new Map<string, LocProjectInfo[]>();
    const loose: LocProjectInfo[] = [];

    const labels = new Map<string, string>();

    for (const project of projects) {
        const name = siblingName(project);
        if (name === null) { loose.push(project); continue; }

        const key = `${name.directory}|${name.setLabel}`;
        labels.set(key, name.setLabel);

        const existing = sets.get(key);
        if (existing) { existing.push(project); } else { sets.set(key, [project]); }
    }

    const nodes: LocTreeNode[] = loose.map(fileNode);
    for (const [key, owned] of sets) {
        if (owned.length === 1) { nodes.push(fileNode(owned[0])); continue; }

        const sorted = [...owned].sort((a, b) => languageOf(a).localeCompare(languageOf(b)));
        nodes.push({
            kind: 'fileset',
            label: labels.get(key) ?? key,
            languages: sorted.map(languageOf),
            // Carried up from the files so a consumer can tell a credits set from a text one
            // without re-deriving it - the two are editable in different ways.
            category: sorted[0].category,
            // Labelled by language rather than by file name: inside a set, the language is the only
            // thing that distinguishes one part from another, and the file name just repeats it.
            children: sorted.map(p => ({
                kind: 'file' as const, label: languageOf(p), project: p, children: [],
            })),
        });
    }

    return nodes.sort(byLabel);
}

/**
 * A file's name with its language taken out of it, split from the folder it sits in.
 *
 * Two things are wanted here - whether two files belong together, and what to call the set - and
 * they used to be packed into one string with a separator. The separator had to be a character no
 * path could contain, which meant a literal NUL byte in the source, and that makes the file binary
 * to git, grep and every editor. Returning the parts is simpler and cannot be mis-split by a folder
 * whose name happens to contain the separator.
 */
interface SiblingName {
    directory: string;
    /** The file name without its language suffix, e.g. `mastertextfile.properties`. */
    setLabel: string;
}

/**
 * The other files of the same logical project - the ones the tree groups into one set.
 *
 * Built on the same rule the tree groups by, rather than a second one that would answer "are these
 * two files related" differently from what the user can see grouped in front of them.
 *
 * Empty for a file that names no language (CSV, XML), which carries every language itself and has
 * no siblings by construction.
 */
export function siblingsOf(
    project: LocProjectInfo, projects: readonly LocProjectInfo[],
): LocProjectInfo[] {
    const name = siblingName(project);
    if (name === null) { return []; }

    return projects.filter(other => {
        if (other.filePath.toLowerCase() === project.filePath.toLowerCase()) { return false; }

        const otherName = siblingName(other);
        return otherName !== null
            && otherName.directory === name.directory
            && otherName.setLabel === name.setLabel;
    });
}

/** Null for a file that names no language, which therefore has no siblings. */
function siblingName(project: LocProjectInfo): SiblingName | null {
    const language = project.language;
    if (!language) { return null; }

    const separator = Math.max(project.filePath.lastIndexOf('/'), project.filePath.lastIndexOf('\\'));
    const directory = separator < 0 ? '' : project.filePath.slice(0, separator);

    const suffix = `_${language}`;
    const dot = project.label.lastIndexOf('.');
    const stem = dot < 0 ? project.label : project.label.slice(0, dot);
    const extension = dot < 0 ? '' : project.label.slice(dot);

    // Case-insensitively, since the engine reads names either way and older files are PascalCase.
    const withoutLanguage = stem.toLowerCase().endsWith(suffix.toLowerCase())
        ? stem.slice(0, stem.length - suffix.length)
        : stem;

    return {
        directory: directory.toLowerCase(),
        setLabel: `${withoutLanguage.toLowerCase()}${extension.toLowerCase()}`,
    };
}

function languageOf(project: LocProjectInfo): string {
    return project.language ?? '';
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
