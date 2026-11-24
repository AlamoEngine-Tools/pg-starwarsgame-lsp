// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The What's New page: its command, and the page opening by itself once per new version.

import * as vscode from "vscode";

import { shownVersion, whatsNewOnStart } from "../whatsNewModel";
import {
    OPEN_AFTER_UPDATE_SETTING,
    OPEN_WHATS_NEW,
    WhatsNewPanel,
} from "../whatsNewPanel";

import type { HostDeps } from "./host";

export function register(
    context: vscode.ExtensionContext,
    deps: HostDeps,
): void {
    const { stores } = deps;

    // The What's New page belongs to the extension, not to a project: it opens from the palette
    // in any window, project or none.
    const extensionVersion = String(
        context.extension.packageJSON.version ?? "unknown",
    );
    // The manifest's preview flag, the one the Marketplace badges the extension with; the page
    // carries the same mark.
    const extensionPreview = context.extension.packageJSON.preview === true;
    // The page's version: the installed one, or Unreleased in the development host, where the
    // manifest still names the last release and the notes being written sit above its section.
    const whatsNewVersion = shownVersion(
        extensionVersion,
        context.extensionMode === vscode.ExtensionMode.Development,
    );
    context.subscriptions.push(
        vscode.commands.registerCommand(OPEN_WHATS_NEW, () =>
            WhatsNewPanel.show(
                context.extensionUri,
                whatsNewVersion,
                extensionPreview,
                stores.panelLayout.read,
            ),
        ),
    );
    // Once per new version, and on the first start, which stands in for a walkthrough: nothing
    // stored a version before this page existed, so every user of the first release carrying it
    // is a first start, and that release's notes are the ones that matter most to them.
    {
        const seen = whatsNewOnStart(
            stores.whatsNewSeenVersion.read(),
            extensionVersion,
            vscode.workspace
                .getConfiguration()
                .get<boolean>(OPEN_AFTER_UPDATE_SETTING, true),
        );
        if (seen.store !== undefined) {
            stores.whatsNewSeenVersion.save(seen.store);
        }
        if (seen.open) {
            void WhatsNewPanel.show(
                context.extensionUri,
                whatsNewVersion,
                extensionPreview,
                stores.panelLayout.read,
            );
        }
    }
}
