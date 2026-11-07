// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The gateway the panels and navigators hold, over one server per project (#229).
//
// They are handed one gateway at construction and keep it for the session, while servers now start
// and stop per project. This is that one object: each call goes to whichever project's server the
// route names at the moment of the call. Every helper of the base class funnels through `request`,
// `isRunning` and the ready state, so those are all it overrides.

import { LspGateway, type LspMessageSink, type LspOutcome } from "./lspGateway";

/**
 * The one event every routed gateway hears when the set of servers changes (#318).
 *
 * It used to be a method called by hand on the window's gateway alone, so a project's own gateway
 * - whose client was between stopping and starting when a panel asked - never heard that its route
 * was back and sat out the whole timeout. A gateway built on the shared routes cannot be missed.
 */
export class RouteChanges {
    private readonly _listeners = new Set<() => void>();

    /** Calls `listener` on every change, for the life of the window. */
    onChanged(listener: () => void): void {
        this._listeners.add(listener);
    }

    /** The set of servers changed: every gateway looks at its route again. */
    fire(): void {
        for (const listener of this._listeners) {
            listener();
        }
    }
}

export class RoutedLspGateway extends LspGateway {
    private readonly _waitingForRoute = new Set<() => void>();

    /**
     * @param _route The gateway of the server a call goes to now, or undefined when there is none.
     * @param sink Where the helpers' messages go when no server is there to answer.
     * @param routes The shared routes event; every change releases whoever waits for a route here.
     */
    constructor(
        private readonly _route: () => LspGateway | undefined,
        sink?: LspMessageSink,
        routes?: RouteChanges,
    ) {
        super(() => undefined, sink);
        routes?.onChanged(() => this.routesChanged());
    }

    override get isRunning(): boolean {
        return this._route()?.isRunning ?? false;
    }

    override request<T>(
        method: string,
        params: unknown = {},
    ): Promise<LspOutcome<T>> {
        const target = this._route();
        return target
            ? target.request<T>(method, params)
            : super.request<T>(method, params);
    }

    /**
     * Readiness is the routed server's. With none to route to yet - the window is still discovering
     * its projects - it waits for one to appear and then for that one, within the same budget.
     */
    override async whenReady(timeoutMs = 30000): Promise<boolean> {
        const started = Date.now();
        if (
            this._route() === undefined &&
            !(await this.waitForRoute(timeoutMs))
        ) {
            return false;
        }

        const target = this._route();
        const remaining = Math.max(0, timeoutMs - (Date.now() - started));
        return target ? target.whenReady(remaining) : false;
    }

    /** Readiness belongs to each project's own gateway; marking the router changes nothing. */
    override markReady(): void {}

    override markStopped(): void {}

    /** The set of servers changed: anyone waiting for a first one looks again. */
    routesChanged(): void {
        const waiting = [...this._waitingForRoute];
        this._waitingForRoute.clear();
        for (const release of waiting) {
            release();
        }
    }

    private waitForRoute(timeoutMs: number): Promise<boolean> {
        return new Promise<boolean>((resolve) => {
            const release = (): void => {
                if (this._route() === undefined) {
                    // Changed, but still nothing to route to: keep waiting.
                    this._waitingForRoute.add(release);
                    return;
                }
                clearTimeout(timer);
                resolve(true);
            };
            const timer = setTimeout(() => {
                this._waitingForRoute.delete(release);
                resolve(false);
            }, timeoutMs);
            this._waitingForRoute.add(release);
        });
    }
}
