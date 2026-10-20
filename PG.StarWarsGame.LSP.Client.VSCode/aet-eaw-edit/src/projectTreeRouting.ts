// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Where a navigator row's children come from (#230).
//
// Pure, so it is testable without a VS Code host: the empty key is a real key here. A window with a
// single project files its one tree under "", and a truthiness check on the owner turned every row
// below the top level into a row with no children.

/** What to answer for a request for children. */
export type ChildrenRoute =
    | { readonly kind: "projects" }
    | {
          readonly kind: "tree";
          readonly key: string;
          readonly fromRoot: boolean;
      }
    | { readonly kind: "none" };

/**
 * Routes a request for children.
 *
 * @param projectKeys The open projects' keys, in discovery order.
 * @param owner The key of the tree a row came from; `undefined` for the root, and for a row no tree
 *     produced.
 * @param isRoot Whether the request is for the top level.
 * @param projectRow The project a project row stands for, when the row is one.
 */
export function routeChildren(
    projectKeys: readonly string[],
    owner: string | undefined,
    isRoot: boolean,
    projectRow?: string,
): ChildrenRoute {
    if (isRoot) {
        if (projectKeys.length > 1) {
            return { kind: "projects" };
        }
        return projectKeys.length === 1
            ? { kind: "tree", key: projectKeys[0], fromRoot: true }
            : { kind: "none" };
    }
    if (projectRow !== undefined) {
        return { kind: "tree", key: projectRow, fromRoot: true };
    }
    return owner === undefined
        ? { kind: "none" }
        : { kind: "tree", key: owner, fromRoot: false };
}
