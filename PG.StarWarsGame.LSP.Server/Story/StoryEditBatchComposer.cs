// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Editing;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     The story graph's side of the write-back contract. Its subject is a campaign and a faction;
///     its commands are <see cref="StoryCommandDto" />s, driven in order over one
///     <see cref="WorkingTextSet" /> and re-parsed after each step, so a command sees the results of
///     the commands before it.
///     <para>
///         Validate runs the XML diagnostics over the composed texts and places each problem on the
///         graph node whose event holds it. Compose turns the composed texts into one edit: a
///         create for each new thread or manifest, then one replacement of the whole text per
///         changed file.
///     </para>
/// </summary>
public sealed class StoryEditBatchComposer(
    IStoryModelService modelService,
    IGameIndexService indexService,
    IDocumentTextSource textSource,
    ISchemaProvider schema,
    IFileHelper fileHelper,
    IModProjectReloadService reloadService,
    ILspConfigurationProvider config,
    IStorySideNamesSource sides,
    IXmlDiagnosticsCollector collector,
    ILogger<StoryEditBatchComposer> logger,
    IMessageCatalog messages) : IEditBatchComposer
{
    /// <summary>The <c>Editor</c> value of the story graph's batches.</summary>
    public const string EditorName = "story";

    public string Editor => EditorName;

    public Task<ValidateEditBatchResult> ValidateAsync(
        EditBatchSubject subject, JArray commands, CancellationToken ct)
    {
        // Not gated on editing: validation writes nothing, and the panel's Validate button is
        // available in every mode (it is the only source of story diagnostics
        // there). In View mode the batch is empty, so this is simply "diagnose the committed
        // campaign" - gating it behind Edit mode would leave read-only users with no diagnostics.

        if (EditBatchCommands.Read<StoryCommandDto>(commands, messages, out var staged) is { } unreadable)
            return Task.FromResult(new ValidateEditBatchResult([], unreadable));

        var (campaign, faction) = (subject.Campaign ?? string.Empty, subject.Faction ?? string.Empty);
        var model = modelService.GetCampaignModel(campaign, faction);
        if (model is null)
            return Task.FromResult(new ValidateEditBatchResult([],
                messages.Format("StoryHandlers_CampaignWasNotFound", campaign)));

        var executor = new StoryCommandExecutor(
            modelService, indexService, textSource, schema, fileHelper, reloadService,
            NullLogger.Instance, messages);
        var texts = new WorkingTextSet(textSource);

        var (failedIndex, composeError) = executor.Compose(
            model, staged.Select(c => c.ToParams(campaign, faction)).ToList(), texts);
        if (composeError is not null)
            return Task.FromResult(new ValidateEditBatchResult([],
                messages.Format("StoryHandlers_ChangeCanTBeStaged", (failedIndex ?? 0) + 1,
                    staged.Count, composeError)));

        // Validate every thread the campaign knows about (reading its staged text where present),
        // plus any new thread the batch created that already has events.
        var modelThreadUris = new HashSet<string>(
            model.Threads.Select(t => fileHelper.NormalizeUri(t.DocumentUri)), DocumentUris.Comparer);
        var candidates = new HashSet<string>(modelThreadUris, DocumentUris.Comparer);
        foreach (var (uri, _, _) in texts.Changed())
            candidates.Add(fileHelper.NormalizeUri(uri));

        var index = indexService.Current;
        var results = new List<EditBatchProblemDto>();
        foreach (var uri in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var text = texts.GetText(uri);
            if (text is null) continue;

            var thread = StoryThreadParser.Parse(text, uri);
            // Skip changed non-thread files (manifests, campaign sets) - they carry no events, so
            // there is nothing to pin to the story graph.
            if (!modelThreadUris.Contains(uri) && thread.Events.Count == 0) continue;

            StoryDiagnosticsCorrelator.Correlate(
                uri, thread.Events,
                collector.Collect(uri, text, index),
                e => StoryGraphBuilder.EventNodeId(uri, e.Name),
                results);
        }

        return Task.FromResult(new ValidateEditBatchResult(results));
    }

    public Task<ApplyEditBatchResult> ComposeAsync(
        EditBatchSubject subject, JArray commands, CancellationToken ct)
    {
        if (StoryEditingFeature.Rejection(config, messages) is { } rejection)
            return Task.FromResult(new ApplyEditBatchResult(false, Error: rejection));

        if (EditBatchCommands.Read<StoryCommandDto>(commands, messages, out var staged) is { } unreadable)
            return Task.FromResult(new ApplyEditBatchResult(false, Error: unreadable));

        // Checked before anything is composed, and over EVERY command: a batch is applied as one
        // edit, so one command touching a referenced project has to stop the whole batch rather
        // than leave it half applied.
        foreach (var command in staged)
            if (DependencyOwnership.Rejection(reloadService.LastWorkspaceConfig, command.ThreadUri)
                is { } notOurs)
                return Task.FromResult(new ApplyEditBatchResult(false, Error: messages.Format(notOurs)));

        var (campaign, faction) = (subject.Campaign ?? string.Empty, subject.Faction ?? string.Empty);
        var model = modelService.GetCampaignModel(campaign, faction);
        if (model is null)
            return Task.FromResult(new ApplyEditBatchResult(false,
                Error: messages.Format("StoryHandlers_CampaignWasNotFound", campaign)));

        if (staged.Count == 0)
            return Task.FromResult(new ApplyEditBatchResult(true));

        var executor = new StoryCommandExecutor(
            modelService, indexService, textSource, schema, fileHelper, reloadService, logger,
            messages, sides);
        var texts = new WorkingTextSet(textSource);

        var (failedIndex, error) = executor.Compose(
            model, staged.Select(c => c.ToParams(campaign, faction)).ToList(), texts);
        if (error is not null)
            return Task.FromResult(new ApplyEditBatchResult(false, failedIndex, error));

        var changed = texts.Changed().ToList();
        if (changed.Count == 0)
            return Task.FromResult(new ApplyEditBatchResult(true));

        logger.LogDebug("Story batch of {Count} -> an edit touching {Files} file(s)", staged.Count, changed.Count);
        return Task.FromResult(new ApplyEditBatchResult(true, Edit: EditBatchEdits.Build(changed
            .Select(c => new EditBatchFileChange(c.Uri, c.Original, c.Current, texts.VersionOf(c.Uri)))
            .ToList())));
    }
}