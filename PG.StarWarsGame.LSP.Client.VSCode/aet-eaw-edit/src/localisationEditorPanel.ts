// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

import { basename } from "path";
import * as vscode from "vscode";

import { CreditsPreviewPanel } from "./creditsPreviewPanel";
import {
    readDialogGeometry,
    saveDialogGeometry,
} from "./dialogGeometryStorage";
import { heldBack, reportSave } from "./editBatchReport";
import { EditBatchClient, type EditBatchSave } from "./lsp/editBatchClient";
import { LocalisationPanelState } from "./shared/localisationPanelState";
import { mergeMembers } from "./shared/locSetMerge";
import { siblingsOf } from "./shared/localisationTreeModel";
import {
    PanelRegistry,
    panelSetKey,
    WebviewPanelHost,
} from "./webviewPanelHost";
import { PanelProject } from "./panelProject";
import type { CreditsCrawl } from "./protocol/panels/creditsPreview";
import type {
    LocalisationEditorCrawlRows,
    LocalisationEditorToHost,
    LocalisationEditorToWebview,
    LocalisationSeedSource,
} from "./protocol/panels/localisationEditor";
import {
    CreateLocalisationLanguageFileResult,
    EDIT_BATCH_EDITOR,
    type EditBatchEditor,
    type EditBatchSubject,
    GetBaselineEntriesResult,
    GetLanguagesResult,
    GetLocalisationProjectsResult,
    GetLocalisationRowsResult,
    LOC_CATEGORY,
} from "./protocol";

/**
 * One file of an open set, and what is known about it.
 *
 * A single file is a set of one, so every path below is the same path - there is no separate
 * "one file" mode to keep in step with the set one.
 */
interface Member {
    filePath: string;
    languages: string[];
    contentHash?: string;
}

/**
 * The localisation grid, hosted in a real editor tab.
 *
 * One panel per set of files, keyed case-insensitively by their paths: two tabs on one file would
 * each hold their own staged queue and their own content hash, so whichever saved second would be
 * refused as stale having silently lost the other's work.
 *
 * Shares its close-time save prompt with {@link StoryGraphPanel} - a disposed webview cannot veto
 * its own close, so the queue is mirrored here as it changes.
 */
export class LocalisationEditorPanel extends WebviewPanelHost<
    LocalisationEditorToHost,
    LocalisationEditorToWebview
> {
    private static readonly _panels =
        new PanelRegistry<LocalisationEditorPanel>();

    /** The tab a title-bar command applies to. */
    private static _active: LocalisationEditorPanel | null = null;

    private readonly _state = new LocalisationPanelState();

    /** Every file the table is built from, in column order. */
    private _members: Member[] = [];
    /**
     * The languages the engine officially supports, cached for the life of the tab.
     *
     * A property of the game rather than of the file, so it is fetched once. The editor offers only
     * these when adding a language - a made-up identifier produces a column the game never reads.
     */
    private _supportedLanguages: string[] | undefined;

    private constructor(
        private readonly _filePaths: string[],
        /**
         * Which language's column to show alone, when opened on one file of a set.
         *
         * Not readonly: the set and each of its languages are one tab, so clicking a different one
         * of them has to re-aim the tab that is already open rather than appear to do nothing.
         */
        private _focusLanguage: string | undefined,
        private readonly _label: string,
        private readonly _category: string,
        private readonly _extensionUri: vscode.Uri,
        project: PanelProject,
    ) {
        // Two view types, so a menu contribution can target one kind of file. The credits editor
        // has a crawl to preview; the translation editor has nothing of the sort.
        //
        // Two editors as well, not one that decides what it is looking at: a credits file is an
        // ordered list addressed by position, a translation file a lookup table addressed by key,
        // and the single grid that tried to be both put credits behaviour into text files
        // repeatedly. The category comes from the tree, so the choice is made before the webview
        // exists - the script cannot be swapped afterwards.
        super(_extensionUri, {
            project,
            viewType: isCredits(_category)
                ? "aetCreditsEditor"
                : "aetTranslationEditor",
            title: vscode.l10n.t("Loc: {0}", _label),
            column: vscode.ViewColumn.Active,
            script: isCredits(_category)
                ? "creditsEditor.js"
                : "translationEditor.js",
            enableFindWidget: true,
            // The grid fills the tab exactly. Without this the editor sits inside whatever margin
            // and padding the host's default stylesheet gives the body, and a full-height layout
            // then overflows by that much - which pushes the footer under the table off the bottom
            // of the window.
            bodyStyle:
                "  html, body { margin: 0; padding: 0; height: 100%; overflow: hidden; }\n" +
                "  #root { height: 100%; }",
        });

        this.onDidDispose(() => {
            if (LocalisationEditorPanel._active === this) {
                LocalisationEditorPanel._active = null;
            }
            void this._promptSaveOnClose();
        });

        // Tracked so a command contributed to the editor title bar knows which tab it belongs to -
        // a menu command carries no argument identifying the panel it was clicked on.
        this.panel.onDidChangeViewState((e) => {
            if (e.webviewPanel.active) {
                LocalisationEditorPanel._active = this;
            }
        });
        LocalisationEditorPanel._active = this;
    }

    /**
     * The file a file-level action applies to: convert, export to DAT, open as text.
     *
     * The first of the set. Those actions are about one file on disk, and for a set the one the
     * tree was opened on is the only defensible choice.
     */
    private get _filePath(): string {
        return this._filePaths[0];
    }

    static show(
        filePath: string,
        label: string,
        category: string,
        extensionUri: vscode.Uri,
        project: PanelProject,
    ): void {
        LocalisationEditorPanel.showSet(
            [filePath],
            label,
            category,
            extensionUri,
            project,
        );
    }

    /**
     * Opens a set of single-language files as one table.
     *
     * Keyed on the whole set, so opening the set and then one of its files reveals the same tab
     * rather than a second one holding a competing staged queue for the same files.
     *
     * @param focusLanguage Shown alone at first - what opening one language of a set means. The
     *     other columns are hidden, not absent, so they are one click away in the column menu.
     */
    static showSet(
        filePaths: string[],
        label: string,
        category: string,
        extensionUri: vscode.Uri,
        project: PanelProject,
        focusLanguage?: string,
    ): void {
        const id = panelSetKey(filePaths);
        const existing = LocalisationEditorPanel._panels.get(project.key, id);
        if (existing) {
            // The set and every language in it map to one tab - deliberately, so they cannot stage
            // competing edits against the same files. Revealing alone is therefore not enough:
            // clicking a language while the set is open has to change which columns are shown, or
            // the click does nothing at all.
            existing._focusOn(focusLanguage);
            existing.reveal();
            return;
        }

        LocalisationEditorPanel._panels.track(
            id,
            new LocalisationEditorPanel(
                filePaths,
                focusLanguage,
                label,
                category,
                extensionUri,
                project,
            ),
        );
    }

    /**
     * Tells every open tab of one project its file may have changed underneath it. All of that
     * project's, because `aet/localisationIndexUpdated` carries no payload - it cannot say which
     * file moved - and only that project's, because another project's server said nothing about
     * its own files.
     *
     * The tab decides what to do: one with nothing staged re-reads, one with staged edits does not,
     * since silently reloading would discard work still visible on screen.
     */
    static invalidateAll(projectKey: string): void {
        for (const panel of LocalisationEditorPanel._panels.forProject(
            projectKey,
        )) {
            panel.post({ type: "invalidate", dirty: panel._state.isDirty });
        }
    }

    /**
     * Shows the credits crawl for the active tab.
     *
     * `fullScreen` plays it over the editor itself and asks the host to fill the screen; otherwise
     * it opens in a panel beside, the way a Markdown or LaTeX preview does. Either way the editor
     * is asked for its staged rows first - the preview is of what you are about to save.
     */
    static previewCrawl(fullScreen: boolean): void {
        LocalisationEditorPanel._active?.post({
            type: "requestCrawlRows",
            fullScreen,
        });
    }

    static disposeAll(): void {
        LocalisationEditorPanel._panels.disposeAll();
    }

    protected async onMessage(msg: LocalisationEditorToHost): Promise<void> {
        switch (msg.type) {
            case "ready":
            case "fetch":
                await this._sendRows();
                break;
            case "pendingSync":
                this._state.syncPending(msg.commands);
                break;
            case "validateBatch":
                await this._validate(msg.commands);
                break;
            case "saveBatch":
                await this._save(msg.commands);
                break;
            case "requestBaseline":
                await this._sendBaseline();
                break;
            case "crawlRows":
                this._sendCrawlRows(msg);
                break;
            case "convertFormat":
                await this._convertFormat(msg.targetFormat);
                break;
            case "addLanguageFile":
                await this._addLanguageFile(msg.language);
                break;
            case "saveDialogGeometry":
                saveDialogGeometry(msg.id, msg.geometry);
                break;
            case "exportDat":
                await vscode.commands.executeCommand(
                    "aet-eaw-edit.lsp.exportLocalisationToDat",
                    {
                        project: { filePath: this._filePath },
                        projectKey: this.project.key,
                    },
                );
                break;
            default:
                break;
        }
    }

    /**
     * Re-aims an open tab at one language of its set, or back at all of them.
     *
     * A message rather than a re-read: the files have not changed, and re-reading would throw away
     * the staged queue along with the user's sort and selection just to hide some columns.
     */
    private _focusOn(language: string | undefined): void {
        this._focusLanguage = language;
        this.post({ type: "focusLanguage", language: language ?? null });
    }

    /** Forwards the editor's staged rows to the preview beside it, if one is open. */
    private _sendCrawlRows(msg: LocalisationEditorCrawlRows): void {
        const payload: CreditsCrawl = {
            type: "crawl",
            rows: msg.rows,
            languages: msg.languages,
            language: msg.language,
        };

        if (msg.open) {
            CreditsPreviewPanel.show(
                this._filePath,
                this._label,
                this._extensionUri,
                this.project,
                payload,
            );
        } else {
            CreditsPreviewPanel.update(this.project, this._filePath, payload);
        }
    }

    private async _supportedLanguagesAsync(): Promise<string[]> {
        if (this._supportedLanguages !== undefined) {
            return this._supportedLanguages;
        }

        // Not fatal: the editor is fully usable, it just cannot offer to add a language. Not cached
        // when the server is down either, so the list arrives once it comes back.
        if (!this.project.gateway.isRunning) {
            return [];
        }

        const result = await this.project.gateway.requestOr<GetLanguagesResult>(
            "aet/getLanguages",
            {},
            { languages: [] },
        );
        this._supportedLanguages = result.languages ?? [];
        return this._supportedLanguages;
    }

    private async _sendRows(): Promise<void> {
        // Read one file at a time rather than in parallel: the server serialises these anyway, and
        // a failure part-way through is far easier to report when the order is known.
        //
        // Reported into the webview rather than as a notification: these rows are the tab's whole
        // content, so the message belongs where the table would have been. A rejected read used to
        // escape here as an unhandled rejection and leave the grid stuck on "Loading...".
        const results: GetLocalisationRowsResult[] = [];
        for (const filePath of this._filePaths) {
            const outcome =
                await this.project.gateway.request<GetLocalisationRowsResult>(
                    "aet/getLocalisationRows",
                    { projectFilePath: filePath },
                );

            if (!outcome.ok) {
                this.post({
                    type: "error",
                    message:
                        outcome.reason === "offline"
                            ? vscode.l10n.t("LSP server is not running.")
                            : vscode.l10n.t(
                                  "Cannot read {0}: {1}",
                                  basename(filePath),
                                  outcome.message,
                              ),
                });
                return;
            }

            if (outcome.value.error) {
                this.post({ type: "error", message: outcome.value.error });
                return;
            }
            results.push(outcome.value);
        }

        // An unchanged set is not re-delivered. A save reloads the server's localisation index and
        // the watcher then reports that same write, so one save announced the index had moved twice
        // over - and each announcement made every open tab hand its webview every row again, reset
        // its selection, sort and inherited toggle, and re-fetch the baseline. See shouldDeliver.
        // For a set the hashes are combined, so any one file moving counts as the set moving.
        const combinedHash = results.map((r) => r.contentHash).join("|");
        if (!this._state.shouldDeliver(combinedHash)) {
            return;
        }

        this._members = this._filePaths.map((filePath, i) => ({
            filePath,
            languages: results[i].languages,
            contentHash: results[i].contentHash,
        }));

        const merged = mergeMembers(
            this._members.map((m, i) => ({
                filePath: m.filePath,
                languages: m.languages,
                rows: results[i].rows,
            })),
        );

        this._state.noteRead(combinedHash);
        this.post({
            supportedLanguages: await this._supportedLanguagesAsync(),
            // Sent with the file so a dialog knows where it belongs on its first render - asking
            // for it when one opens would show it centred and then move it.
            dialogGeometry: readDialogGeometry(),
            type: "rows",
            rows: merged.rows,
            languages: merged.languages,
            category: results[0].category,
            ordered: results[0].ordered,
            // A set is several single-language files, so a language is a file, not a column - the
            // grid offers "add language" as a new sibling either way.
            canAddLanguage:
                this._members.length === 1 && results[0].canAddLanguage,
            addLanguageCreatesFile: results[0].addLanguageCreatesFile,
            focusLanguage: this._focusLanguage ?? null,
        });

        // Only for a credits file with nothing in it. That is the one state where the editor can
        // offer to start it from a sibling, and the one where reading those siblings is worth the
        // round trips - a file with rows already has its content and would never use them.
        if (isCredits(this._category) && merged.rows.length === 0) {
            await this._sendSeedSources();
        }
    }

    /**
     * The other language files of this project, with their contents, so an empty credits file can
     * be started from one of them.
     *
     * Sent as its own message rather than folded into `rows`, so the shared panel hook stays free
     * of a concept only the credits editor has - it forwards anything it does not recognise.
     *
     * Best-effort throughout: this is a convenience, and an editor that cannot offer it is still a
     * working editor.
     */
    private async _sendSeedSources(): Promise<void> {
        const listed =
            await this.project.gateway.requestOr<GetLocalisationProjectsResult>(
                "aet/getLocalisationProjects",
                {},
                { projects: [] },
            );

        const self = (listed.projects ?? []).find(
            (p) => p.filePath.toLowerCase() === this._filePath.toLowerCase(),
        );
        if (self === undefined) {
            return;
        }

        const sources: LocalisationSeedSource[] = [];

        for (const sibling of siblingsOf(self, listed.projects ?? [])) {
            const read =
                await this.project.gateway.request<GetLocalisationRowsResult>(
                    "aet/getLocalisationRows",
                    { projectFilePath: sibling.filePath },
                );
            if (!read.ok || read.value.error) {
                continue;
            }

            // A sibling with nothing in it is no more use as a starting point than this file is.
            const rows = read.value.rows ?? [];
            if (rows.length === 0) {
                continue;
            }

            sources.push({
                filePath: sibling.filePath,
                label: sibling.label,
                language: sibling.language ?? "",
                rowCount: rows.length,
                rows,
            });
        }

        if (sources.length > 0) {
            this.post({ type: "seedSources", sources });
        }
    }

    /**
     * Rewrites this file in another format, keeping the original.
     *
     * Delegated to the command rather than requested here, so the palette entry and this button are
     * the same code path - including how the outcome is reported.
     */
    private async _convertFormat(targetFormat: string): Promise<void> {
        await vscode.commands.executeCommand(
            "aet-eaw-edit.lsp.convertLocalisationFormat",
            {
                filePath: this._filePath,
                targetFormat,
                category: this._category,
                projectKey: this.project.key,
            },
        );
    }

    /**
     * Adds a language to a single-language project by creating the sibling file that holds it.
     *
     * Not part of the staged batch: the batch composes new text for THIS file, and this writes a
     * different one. It lands on disk immediately, like every other localisation write, so the
     * result is offered for opening rather than left for the user to find in the tree.
     */
    private async _addLanguageFile(language: string): Promise<void> {
        const result =
            await this.project.gateway.requestOrReport<CreateLocalisationLanguageFileResult>(
                "aet/createLocalisationLanguageFile",
                { projectFilePath: this._filePath, language },
                vscode.l10n.t("Could not create the {0} file", language),
            );
        if (result === undefined) {
            return;
        }

        if (result.error || !result.writtenPath) {
            vscode.window.showErrorMessage(
                vscode.l10n.t(
                    "EaWEdit: {0}",
                    result.error ?? vscode.l10n.t("could not create the file."),
                ),
            );
            return;
        }

        const writtenPath = result.writtenPath;
        const choice = await vscode.window.showInformationMessage(
            vscode.l10n.t(
                "EaWEdit: Created {0} with every key from this file, ready to translate.",
                writtenPath,
            ),
            vscode.l10n.t("Open"),
        );

        if (choice === vscode.l10n.t("Open")) {
            LocalisationEditorPanel.show(
                writtenPath,
                basename(writtenPath),
                this._category,
                this._extensionUri,
                this.project,
            );
        }
    }

    /**
     * Fetches what the layers below this file already say, so the grid can hide the rows that only
     * repeat it.
     *
     * A failure is answered with an empty baseline rather than an error: the grid is fully usable
     * without one, and the only consequence is that nothing is hidden. Turning that into a modal
     * would interrupt editing over a convenience.
     */
    private async _sendBaseline(): Promise<void> {
        const result =
            await this.project.gateway.requestOr<GetBaselineEntriesResult>(
                "aet/getBaselineEntries",
                { projectFilePath: this._filePath },
                { entries: [] },
            );
        this.post({ type: "baselineRows", entries: result.entries ?? [] });
    }

    private async _validate(
        commands: Record<string, unknown>[],
    ): Promise<void> {
        // The Validate tag reads out whatever comes back, so a failure has to arrive as a result
        // too - otherwise the tag sits on the previous run's verdict, describing a document that no
        // longer exists.
        const outcome = await this._batches.validate(
            this._editor,
            this._subject,
            commands,
        );

        this.post(
            outcome.ok
                ? {
                      type: "problems",
                      problems: outcome.value.problems ?? [],
                      error: outcome.value.error ?? null,
                  }
                : { type: "problems", problems: [], error: outcome.message },
        );
    }

    /**
     * Writes the staged batch.
     *
     * The server composes it into one edit - for a set, across every file a change belongs to -
     * and VS Code applies the edit and saves the files it touched. One edit, so a set is written
     * whole or not at all, and one step of each file's undo history. A refused batch leaves the
     * whole queue staged, so nothing is silently half-applied from the user's point of view.
     */
    private async _save(commands: Record<string, unknown>[]): Promise<void> {
        // Answered rather than abandoned: a silent return left the Save button spinning with the
        // queue neither written nor released, which looks exactly like the button doing nothing.
        if (!this.project.gateway.isRunning) {
            void vscode.window.showWarningMessage(
                vscode.l10n.t(
                    "EaWEdit LSP: Server is not running; the edits are still staged.",
                ),
            );
            this.post({ type: "saveResult", success: false });
            return;
        }

        const saved = await this._batches.save(
            this._editor,
            this._subject,
            commands,
        );

        if (!saved.saved) {
            this._state.noteSaveFailed();
            await this._reportSaveFailure(saved);
            this.post({
                type: "saveResult",
                success: false,
                failedIndex: saved.failedIndex ?? null,
            });
            return;
        }

        // The hash each file has now is the one its next batch has to carry.
        const written = new Map(
            saved.documents.map((d) => [
                d.filePath.toLowerCase(),
                d.contentHash ?? undefined,
            ]),
        );
        for (const member of this._members) {
            member.contentHash =
                written.get(member.filePath.toLowerCase()) ??
                member.contentHash;
        }
        this._state.noteSaved(
            this._members.length > 0
                ? this._members.map((m) => m.contentHash ?? "").join("|")
                : (saved.documents[0]?.contentHash ?? undefined),
        );

        // On disk unless a file could not be written, which this names.
        reportSave(saved, vscode.l10n.t("Save failed"));
        this.post({ type: "saveResult", success: true });
    }

    /**
     * A refused save is nearly always the file having moved on disk, and the only way forward is a
     * reload - so the message offers it rather than leaving the user to find it.
     */
    private async _reportSaveFailure(
        saved: Extract<EditBatchSave, { saved: false }>,
    ): Promise<void> {
        const where =
            typeof saved.failedIndex === "number"
                ? ` (change ${saved.failedIndex + 1})`
                : "";
        const why =
            heldBack(saved) ??
            saved.message ??
            (saved.reason === "notApplied"
                ? vscode.l10n.t("The changes could not be applied.")
                : vscode.l10n.t("unknown error"));
        const choice = await vscode.window.showErrorMessage(
            vscode.l10n.t("EaWEdit: Could not save - {0}{1}", why, where),
            vscode.l10n.t("Reload"),
        );

        if (choice === vscode.l10n.t("Reload")) {
            await this._sendRows();
        }
    }

    /** Where this panel's staged batches are checked and saved: its project's server and editor. */
    private get _batches(): EditBatchClient {
        return new EditBatchClient(this.project.gateway, this.project.edits);
    }

    /** Which of the two localisation editors this panel is, as its batches say it. */
    private get _editor(): EditBatchEditor {
        return isCredits(this._category)
            ? EDIT_BATCH_EDITOR.credits
            : EDIT_BATCH_EDITOR.translation;
    }

    /**
     * What this panel's batches are about: the file it shows, or the set of single-language files
     * it shows as one table, each with the hash it was last read at. Which file of a set a change
     * goes to is the server's decision.
     */
    private get _subject(): EditBatchSubject {
        // A set that has not been read yet has no members; fall back to the file it was opened on.
        const members =
            this._members.length > 0
                ? this._members
                : [
                      {
                          filePath: this._filePath,
                          contentHash: this._state.contentHash,
                      },
                  ];
        return {
            documents: members.map((m) => ({
                filePath: m.filePath,
                contentHash: m.contentHash ?? null,
            })),
        };
    }

    private async _promptSaveOnClose(): Promise<void> {
        if (!this._state.shouldPromptOnClose()) {
            return;
        }

        const count = this._state.pendingCount;
        const choice = await vscode.window.showWarningMessage(
            count === 1
                ? vscode.l10n.t(
                      "EaWEdit: 1 unsaved localisation change in '{0}'.",
                      this._filePath,
                  )
                : vscode.l10n.t(
                      "EaWEdit: {0} unsaved localisation changes in '{1}'.",
                      count,
                      this._filePath,
                  ),
            { modal: true },
            vscode.l10n.t("Save"),
        );

        if (choice === vscode.l10n.t("Save")) {
            await this._save(this._state.pending);
        }
    }
}

/**
 * Which of the two editors a file gets, and which protocol its edits travel over.
 *
 * The category comes from the tree, which got it from the loader, so the choice is made before the
 * webview is created - the script cannot be swapped afterwards.
 */
function isCredits(category: string): boolean {
    return category === LOC_CATEGORY.credits;
}
