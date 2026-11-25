// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The messages every panel shares with its webview (#208). Each panel's own file in this folder adds
// its vocabulary to these; WebviewPanelHost handles them for all of them.

/** A request the webview makes to the panel's project's server, relayed by the panel. */
export interface HostRequest {
    type: "request";
    /** Chosen by the webview; the response carries it back. */
    id: number;
    /** An `aet/...` method on the panel's allow-list; anything else is answered with null. */
    method: string;
    params?: unknown;
}

/** The server's answer to a {@link HostRequest}: its result, or null when there was none. */
export interface HostResponse {
    type: "response";
    id: number;
    result: unknown;
}

/** A dock or column the reader resized; the host keeps the size for every panel. */
export interface SetPanelLayout {
    type: "setPanelLayout";
    key: string;
    value: number;
}

/** A page asks to drop its staged changes; the host asks the reader first. */
export interface ConfirmDiscard {
    type: "confirmDiscard";
    /** Chosen by the page; the answer carries it back. */
    id: number;
    /** How many changes are staged, for the question's text. */
    count: number;
}

/** The reader's answer to a {@link ConfirmDiscard}. */
export interface DiscardAnswer {
    type: "discardAnswer";
    id: number;
    confirmed: boolean;
}

/** What every webview may send its host besides its own vocabulary. */
export type CommonToHost = HostRequest | SetPanelLayout | ConfirmDiscard;
