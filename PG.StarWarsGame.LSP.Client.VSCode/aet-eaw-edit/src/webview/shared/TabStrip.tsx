// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// Tabs inside a panel, drawn as the editor's own tab strip: the home tab a table keeps for itself,
// then a tab per entry opened from it. Its look is .editor-tabs in dockChromeCss; what opens,
// reuses, pins and closes a tab is editorTabs.

import * as l10n from "@vscode/l10n";

import { IconButton } from "./Button";

/** One tab beside the home tab. */
export interface StripTab {
    key: string;
    label: string;
    /** False for the one preview tab the next entry opened takes over. */
    pinned: boolean;
}

/**
 * The home tab, then each tab by its label. The unpinned one is in italics with a pin beside it, and
 * a double click on it keeps it too, as in the editor. The home tab has no close: it is the table.
 */
export function TabStrip({
    label,
    home,
    tabs,
    active,
    onHome,
    onSelect,
    onPin,
    onClose,
}: {
    /** What the strip holds, for a screen reader. */
    label: string;
    home: string;
    tabs: readonly StripTab[];
    /** The active tab's key; null when the home tab is active. */
    active: string | null;
    onHome: () => void;
    onSelect: (key: string) => void;
    onPin: (key: string) => void;
    onClose: (key: string) => void;
}): React.JSX.Element {
    return (
        <div className="editor-tabs" role="tablist" aria-label={label}>
            <div
                className={`editor-tab only-name${active === null ? " on" : ""}`}
            >
                <button
                    type="button"
                    role="tab"
                    aria-selected={active === null}
                    className="editor-tab-name"
                    onClick={onHome}
                >
                    {home}
                </button>
            </div>
            {tabs.map((tab) => (
                <div
                    key={tab.key}
                    className={`editor-tab${active === tab.key ? " on" : ""}${tab.pinned ? "" : " unpinned"}`}
                >
                    <button
                        type="button"
                        role="tab"
                        aria-selected={active === tab.key}
                        className="editor-tab-name"
                        title={
                            tab.pinned
                                ? tab.label
                                : l10n.t("Preview - double click to keep it")
                        }
                        onClick={() => onSelect(tab.key)}
                        onDoubleClick={() => onPin(tab.key)}
                    >
                        {tab.label}
                    </button>
                    {!tab.pinned && (
                        <IconButton
                            icon="pin"
                            title={l10n.t("Keep open")}
                            onClick={() => onPin(tab.key)}
                        />
                    )}
                    <IconButton
                        icon="close"
                        title={l10n.t("Close")}
                        onClick={() => onClose(tab.key)}
                    />
                </div>
            ))}
        </div>
    );
}
