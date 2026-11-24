// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The commands that take the reader somewhere: the references of a symbol.

import * as vscode from "vscode";

import type { HostDeps } from "./host";

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    context.subscriptions.push(
        vscode.commands.registerCommand(
            "aet-eaw-edit.lsp.showReferences",
            (
                uriStr: string,
                position: { line: number; character: number },
                rawLocations: {
                    uri: string;
                    range: {
                        start: { line: number; character: number };
                        end: { line: number; character: number };
                    };
                }[],
            ) => {
                const uri = vscode.Uri.parse(uriStr);
                const pos = new vscode.Position(
                    position.line,
                    position.character,
                );
                const locations = rawLocations.map(
                    (l) =>
                        new vscode.Location(
                            vscode.Uri.parse(l.uri),
                            new vscode.Range(
                                new vscode.Position(
                                    l.range.start.line,
                                    l.range.start.character,
                                ),
                                new vscode.Position(
                                    l.range.end.line,
                                    l.range.end.character,
                                ),
                            ),
                        ),
                );
                vscode.commands.executeCommand(
                    "editor.action.showReferences",
                    uri,
                    pos,
                    locations,
                );
            },
        ),
    );
}
