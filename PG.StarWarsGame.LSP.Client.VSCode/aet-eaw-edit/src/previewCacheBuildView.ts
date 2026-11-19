// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

// The background build of a project's preview cache, as the reader sees it (#436).
//
// The server converts every model of a project once its first index is live and says how far it is
// with `aet/previewCacheBuild`. This turns those notifications into ONE progress the reader can
// cancel, however many arrive, and ends it when the server says the build has stopped.
//
// Pure - no `vscode` import. How a progress is drawn is handed in, so this is unit-tested.

/** What the server says about the build. */
export interface PreviewCacheBuildProgress {
    /** `running` while models are being converted, `ended` when the build has stopped. */
    state: string;
    done: number;
    total: number;
}

/** One progress on screen. */
export interface ShownProgress {
    /**
     * @param message What is said beside the title.
     * @param fraction How much is done, 0 to 1.
     */
    report(message: string, fraction: number): void;
    end(): void;
}

/** How a cancellable progress is put on screen. */
export type ShowProgress = (onCancel: () => void) => ShownProgress;

export class PreviewCacheBuildView {
    private shown: ShownProgress | null = null;

    /**
     * @param show Puts a progress on screen and calls back when the reader cancels it.
     * @param cancel Tells the server to stop the build.
     * @param describe The words for "done of total".
     */
    constructor(
        private readonly show: ShowProgress,
        private readonly cancel: () => void,
        private readonly describe: (done: number, total: number) => string,
    ) {}

    /** Takes one notification from the server. */
    update(progress: PreviewCacheBuildProgress): void {
        if (progress.state !== "running") {
            this.end();
            return;
        }

        this.shown ??= this.show(this.cancel);
        this.shown.report(
            this.describe(progress.done, progress.total),
            progress.total <= 0
                ? 1
                : Math.min(1, progress.done / progress.total),
        );
    }

    /** Takes the progress off screen: the build ended, or its server stopped. */
    end(): void {
        this.shown?.end();
        this.shown = null;
    }
}
