// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Story.Writer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     One story command's contribution to a workspace edit: text edits keyed by file, plus any
///     file the command creates (with its skeleton). A single command edits one file (most kinds),
///     two (manifest/campaign ops that both create a thread/manifest and edit its parent), or many
///     (a campaign-wide rename). <see cref="PrebuiltEdit" /> carries an opaque edit the executor
///     cannot decompose (the symbol-index rename, which also spans Lua) - it is applied as-is.
/// </summary>
internal sealed record StoryCommandEdit(
    IReadOnlyList<StoryFileEdit> Files,
    string Label,
    bool UseChangesMap = false,
    WorkspaceEdit? PrebuiltEdit = null);

/// <summary>
///     Edits for one file. <see cref="CreateWithSkeleton" /> (non-null) means the file must be
///     created with that skeleton before <see cref="Edits" /> apply - for a fresh thread/manifest
///     the skeleton is the whole content and <see cref="Edits" /> is empty.
/// </summary>
internal sealed record StoryFileEdit(
    string Uri,
    IReadOnlyList<StoryTextEdit> Edits,
    string? CreateWithSkeleton = null);

/// <summary>
///     Turns a story editor command envelope into text edits, validated against the campaign model,
///     without touching the workspace. Shared by <see cref="ExecuteStoryCommandHandler" /> (one
///     command → one applyEdit) and the batch endpoints (many commands composed over an in-memory
///     <see cref="WorkingTextSet" /> before a single applyEdit or an in-memory validation run).
///     Reads document text through the working set, so a command in a batch sees the results of the
///     commands before it. The writer is deliberately dumb; existence checks, duplicate-name rules
///     and read-only layer guards live here.
/// </summary>
internal sealed class StoryCommandExecutor(
    IStoryModelService modelService,
    IGameIndexService indexService,
    IDocumentTextSource textSource,
    ISchemaProvider schema,
    IFileHelper fileHelper,
    IModProjectReloadService reloadService,
    ILogger logger)
{
    /// <summary>The skeleton for a newly created plot manifest file.</summary>
    private const string ManifestFileSkeleton =
        "<?xml version=\"1.0\" ?>\n<Story_Mode_Plots>\n</Story_Mode_Plots>\n";

    /// <summary>
    ///     Produces the edits for one command against the given working set, or an error message.
    ///     The working set is NOT advanced here - callers that compose commands apply the returned
    ///     edits (<see cref="WorkingTextSet.Apply" />) before producing the next command.
    ///     <paramref name="composable" /> forces a rename to use the model-only path (re-parses the
    ///     working text, so it composes cleanly) instead of the symbol-index path, whose opaque
    ///     cross-file/Lua edit can't be folded into a batch.
    /// </summary>
    public (StoryCommandEdit? Edit, string? Error) Produce(
        ExecuteStoryCommandParams request, StoryCampaignModel model, WorkingTextSet texts,
        bool composable = false)
    {
        return request.Kind switch
        {
            "renameEvent" => RenameEvent(request, model, texts, composable),
            "createEvent" or "deleteEvent" or "setEventType" or "setRewardType" or "clearEventType"
                or "clearRewardType" or "setParams" or "setPerpetual" or "setDialog" or "setBranch"
                or "addPrereq" or "addPrereqGroup" or "addPrereqAlternatives" or "removePrereq"
                or "retargetControlEdge" or "createTacticalAttachment"
                => ThreadOp(request, model, texts),
            "createThread" or "deleteThread" or "setThreadState" or "attachLuaScript"
                => ManifestOp(request, texts),
            "addPlotManifest" or "removePlotManifest" => CampaignOp(request, texts),
            _ => (null, $"Unknown story command '{request.Kind}'.")
        };
    }

    /// <summary>
    ///     Composes a batch of commands over one working set, in order, re-parsing each step so a
    ///     command sees the results of the ones before it. Returns the 0-based index and message of
    ///     the first command that fails (nothing further is applied), or (null, null) on full
    ///     success. Renames run in composable (model-only) mode - see <see cref="Produce" />.
    /// </summary>
    public (int? FailedIndex, string? Error) Compose(
        StoryCampaignModel model, IReadOnlyList<ExecuteStoryCommandParams> commands, WorkingTextSet texts)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            var (produced, error) = Produce(commands[i], model, texts, true);
            if (error is not null) return (i, error);

            foreach (var file in produced!.Files)
            {
                if (file.CreateWithSkeleton is { } skeleton) texts.CreateFile(file.Uri, skeleton);
                texts.Apply(file.Uri, file.Edits);
            }
        }

        return (null, null);
    }

    // ── Thread-file operations ───────────────────────────────────────────────

    private (StoryCommandEdit?, string?) ThreadOp(
        ExecuteStoryCommandParams request, StoryCampaignModel model, WorkingTextSet texts)
    {
        if (string.IsNullOrEmpty(request.ThreadUri))
            return (null, "The command needs a threadUri.");

        var uri = fileHelper.NormalizeUri(request.ThreadUri);
        if (ReadOnlyLayerObjection(uri) is { } objection)
            return (null, objection);

        var text = texts.GetText(uri);
        if (text is null)
            return (null, $"'{uri}' is not readable.");

        // Parse fresh: the model's ranges may predate unflushed edits; the writer's ranges must
        // match the exact text the edits will be applied to.
        var thread = StoryThreadParser.Parse(text, uri);

        IReadOnlyList<StoryTextEdit> edits;
        string label;
        if (request.Kind is "createEvent" or "createTacticalAttachment")
        {
            if (string.IsNullOrWhiteSpace(request.NewName))
                return (null, $"{request.Kind} needs a newName.");
            if (model.Threads.SelectMany(t => t.Events).Any(e =>
                    string.Equals(e.Name, request.NewName, StringComparison.OrdinalIgnoreCase)))
                return (null, $"'{request.NewName}' already names an event in campaign " +
                              $"'{request.Campaign}' - event names resolve campaign-wide.");
            if (request.Kind == "createTacticalAttachment")
            {
                if (string.IsNullOrEmpty(request.File))
                    return (null, "createTacticalAttachment needs the tactical manifest file.");
                var eventType = string.Equals(request.Value, "space", StringComparison.OrdinalIgnoreCase)
                    ? "STORY_SPACE_TACTICAL"
                    : "STORY_LAND_TACTICAL";
                edits = StoryXmlWriter.CreateEvent(text, thread, request.NewName!, eventType, null,
                    [("Event_Param1", request.File!)]);
                label = $"Create tactical attachment '{request.NewName}'";
            }
            else
            {
                var extraTags = new List<(string Tag, string Value)>();
                foreach (var p in request.EventParams ?? [])
                    if (p.Value is not null)
                        extraTags.Add(($"Event_Param{p.Position + 1}", p.Value));
                foreach (var p in request.RewardParams ?? [])
                    if (p.Value is not null)
                        extraTags.Add(($"Reward_Param{p.Position + 1}", p.Value));
                edits = StoryXmlWriter.CreateEvent(text, thread, request.NewName!,
                    request.EventType, request.RewardType, extraTags.Count > 0 ? extraTags : null);
                label = $"Create story event '{request.NewName}'";
            }
        }
        else
        {
            var located = LocateEvent(thread, request.EventName);
            if (located.Error is not null) return (null, located.Error);
            var storyEvent = located.Event!;

            switch (request.Kind)
            {
                case "deleteEvent":
                    edits = StoryXmlWriter.DeleteEvent(text, storyEvent);
                    label = $"Delete story event '{storyEvent.Name}'";
                    break;
                case "setEventType":
                    edits = StoryXmlWriter.SetTagValue(text, storyEvent, "Event_Type", request.Value);
                    label = $"Set event type of '{storyEvent.Name}'";
                    break;
                case "setRewardType":
                    edits = StoryXmlWriter.SetTagValue(text, storyEvent, "Reward_Type", request.Value);
                    label = $"Set reward type of '{storyEvent.Name}'";
                    break;
                case "clearEventType":
                    edits = StoryXmlWriter.ClearTypeBlock(text, storyEvent, "Event");
                    label = $"Remove the trigger of '{storyEvent.Name}'";
                    break;
                case "clearRewardType":
                    edits = StoryXmlWriter.ClearTypeBlock(text, storyEvent, "Reward");
                    label = $"Remove the reward of '{storyEvent.Name}'";
                    break;
                case "setBranch":
                    edits = StoryXmlWriter.SetTagValue(text, storyEvent, "Branch", request.Value);
                    label = $"Set branch of '{storyEvent.Name}'";
                    break;
                case "setDialog":
                    edits = StoryXmlWriter.SetTagValue(text, storyEvent, "Story_Dialog", request.Value);
                    label = $"Set dialog of '{storyEvent.Name}'";
                    break;
                case "setPerpetual":
                    edits = StoryXmlWriter.SetTagValue(text, storyEvent, "Perpetual",
                        request.Flag == true ? "Yes" : null);
                    label = $"Set perpetual of '{storyEvent.Name}'";
                    break;
                case "setParams":
                {
                    var prefix = request.ParamKind?.ToUpperInvariant() switch
                    {
                        "EVENT" => "Event_Param",
                        "REWARD" => "Reward_Param",
                        _ => null
                    };
                    if (prefix is null) return (null, "setParams needs paramKind 'event' or 'reward'.");
                    if (request.Params is not { Count: > 0 }) return (null, "setParams needs params.");
                    edits = StoryXmlWriter.SetParams(text, storyEvent, prefix,
                        request.Params.Select(p => (p.Position, p.Value)).ToList());
                    label = $"Set params of '{storyEvent.Name}'";
                    break;
                }
                case "retargetControlEdge":
                {
                    if (request.GroupIndex is not { } slot || string.IsNullOrEmpty(request.Token))
                        return (null, "retargetControlEdge needs groupIndex (the reward slot) and token.");
                    edits = StoryXmlWriter.SetParams(text, storyEvent, "Reward_Param", [(slot, request.Token)]);
                    label = $"Retarget control edge of '{storyEvent.Name}'";
                    break;
                }
                case "addPrereq":
                    if (string.IsNullOrEmpty(request.Token)) return (null, "addPrereq needs a token.");
                    edits = StoryXmlWriter.AddPrereq(text, storyEvent, request.GroupIndex, request.Token!);
                    label = $"Add prereq to '{storyEvent.Name}'";
                    break;
                case "addPrereqGroup":
                    if (request.Tokens is not { Count: > 0 }) return (null, "addPrereqGroup needs tokens.");
                    edits = StoryXmlWriter.AddPrereqGroup(text, storyEvent, request.Tokens);
                    label = $"Add prereq group to '{storyEvent.Name}'";
                    break;
                case "addPrereqAlternatives":
                    if (request.Tokens is not { Count: > 0 })
                        return (null, "addPrereqAlternatives needs tokens.");
                    edits = StoryXmlWriter.AddPrereqAlternatives(text, storyEvent, request.Tokens);
                    label = $"Add prereq alternatives to '{storyEvent.Name}'";
                    break;
                case "removePrereq":
                {
                    if (string.IsNullOrEmpty(request.Token))
                        return (null, "removePrereq needs a token.");
                    if (request.GroupIndex is { } group)
                    {
                        if (group < 0 || group >= storyEvent.PrereqGroups.Count)
                            return (null, $"'{storyEvent.Name}' has no prereq group {group}.");
                        if (!storyEvent.PrereqGroups[group].Tokens.Any(t =>
                                string.Equals(t.Text, request.Token, StringComparison.OrdinalIgnoreCase)))
                            return (null,
                                $"Prereq group {group} of '{storyEvent.Name}' has no token '{request.Token}'.");
                    }
                    else if (!storyEvent.PrereqGroups.Any(g => g.Tokens.Any(t =>
                                 string.Equals(t.Text, request.Token, StringComparison.OrdinalIgnoreCase))))
                    {
                        return (null, $"'{storyEvent.Name}' has no prereq '{request.Token}'.");
                    }

                    // Without a group index (edge-removal gesture) the token goes from every line.
                    edits = StoryXmlWriter.RemovePrereq(text, storyEvent, request.GroupIndex, request.Token!);
                    label = $"Remove prereq from '{storyEvent.Name}'";
                    break;
                }
                default:
                    return (null, $"Unknown story command '{request.Kind}'.");
            }
        }

        if (edits.Count == 0)
            return (null, "The command produced no changes.");

        return (new StoryCommandEdit([new StoryFileEdit(uri, edits)], label), null);
    }

    private static (StoryEvent? Event, string? Error) LocateEvent(StoryThread thread, string? eventName)
    {
        if (string.IsNullOrEmpty(eventName))
            return (null, "The command needs an eventName.");
        var matches = thread.Events
            .Where(e => string.Equals(e.Name, eventName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            0 => (null, $"Event '{eventName}' was not found in the thread."),
            1 => (matches[0], null),
            _ => (null, $"'{eventName}' names {matches.Count} events in this thread - fix the duplicate first.")
        };
    }

    // ── Plot manifest operations ─────────────────────────────────────────────

    private (StoryCommandEdit?, string?) ManifestOp(ExecuteStoryCommandParams request, WorkingTextSet texts)
    {
        if (string.IsNullOrEmpty(request.File))
            return (null, "The command needs the plot manifest file.");
        if (string.IsNullOrEmpty(request.Value))
            return (null, "The command needs a value (thread file or script name).");

        var manifest = ResolveXmlRelative(request.File!, texts);
        if (manifest is null)
            return (null, $"Plot manifest '{request.File}' was not found in the workspace.");
        if (ReadOnlyLayerObjection(manifest.Value.Uri) is { } objection)
            return (null, objection);

        var (uri, path, text) = manifest.Value;
        IReadOnlyList<StoryTextEdit> edits;
        var files = new List<StoryFileEdit>();
        string label;
        switch (request.Kind)
        {
            case "createThread":
            {
                edits = StoryManifestWriter.AddEntry(text, "Active_Plot", request.Value!);
                var threadPath = Path.Combine(Path.GetDirectoryName(path) ?? "", request.Value!);
                var threadUri = fileHelper.NormalizeUri(threadPath);
                if (texts.GetText(threadUri) is null)
                    files.Add(new StoryFileEdit(threadUri, [], StoryManifestWriter.ThreadFileSkeleton));

                label = $"Create story thread '{request.Value}'";
                break;
            }
            case "deleteThread":
                edits = StoryManifestWriter.RemoveEntry(text, ["Active_Plot", "Suspended_Plot"], request.Value!);
                if (edits.Count == 0)
                    return (null, $"'{request.Value}' is not listed in '{request.File}'. " +
                                  "The file itself is never deleted - remove it manually if intended.");
                label = $"Detach story thread '{request.Value}'";
                break;
            case "setThreadState":
            {
                if (request.Flag is not { } suspended)
                    return (null, "setThreadState needs flag (true = suspended).");
                edits = StoryManifestWriter.RetagEntry(text, ["Active_Plot", "Suspended_Plot"],
                    request.Value!, suspended ? "Suspended_Plot" : "Active_Plot");
                if (edits.Count == 0)
                    return (null, $"'{request.Value}' is not listed in '{request.File}', " +
                                  "or it is already in the requested state.");
                label = $"Set thread state of '{request.Value}'";
                break;
            }
            case "attachLuaScript":
                edits = StoryManifestWriter.AddEntry(text, "Lua_Script", request.Value!);
                label = $"Attach Lua script '{request.Value}'";
                break;
            default:
                return (null, $"Unknown story command '{request.Kind}'.");
        }

        if (edits.Count == 0)
            return (null, "The command produced no changes.");

        files.Add(new StoryFileEdit(uri, edits));
        return (new StoryCommandEdit(files, label), null);
    }

    // ── Campaign set operations ──────────────────────────────────────────────

    private (StoryCommandEdit?, string?) CampaignOp(ExecuteStoryCommandParams request, WorkingTextSet texts)
    {
        if (string.IsNullOrEmpty(request.File))
            return (null, "The command needs the plot manifest file.");

        var chain = modelService.GetChainResult();
        var campaignChain = chain.Campaigns.FirstOrDefault(c =>
            string.Equals(c.Name, request.Campaign, StringComparison.OrdinalIgnoreCase));
        if (campaignChain is null || campaignChain.SourceFile.Length == 0)
            return (null, $"The campaign set file declaring '{request.Campaign}' could not be located.");

        var source = ResolveXmlRelative(campaignChain.SourceFile, texts);
        if (source is null)
            return (null, $"'{campaignChain.SourceFile}' is not readable.");
        if (ReadOnlyLayerObjection(source.Value.Uri) is { } objection)
            return (null, objection);

        var (uri, path, text) = source.Value;
        IReadOnlyList<StoryTextEdit> edits;
        var files = new List<StoryFileEdit>();
        string label;
        if (request.Kind == "addPlotManifest")
        {
            if (string.IsNullOrEmpty(request.Faction))
                return (null, "addPlotManifest needs the faction.");
            edits = StoryManifestWriter.AddCampaignStoryName(
                text, request.Campaign, request.Faction!, request.File!);
            if (edits.Count == 0)
                return (null, $"Campaign '{request.Campaign}' was not found in '{campaignChain.SourceFile}'.");

            var manifestPath = Path.Combine(Path.GetDirectoryName(path) ?? "", request.File!);
            var manifestUri = fileHelper.NormalizeUri(manifestPath);
            if (texts.GetText(manifestUri) is null)
                files.Add(new StoryFileEdit(manifestUri, [], ManifestFileSkeleton));

            label = $"Attach plot manifest '{request.File}'";
        }
        else
        {
            edits = StoryManifestWriter.RemoveCampaignStoryName(text, request.Campaign, request.File!);
            if (edits.Count == 0)
                return (null, $"'{request.File}' is not attached to campaign '{request.Campaign}'.");
            label = $"Detach plot manifest '{request.File}'";
        }

        files.Add(new StoryFileEdit(uri, edits));
        return (new StoryCommandEdit(files, label), null);
    }

    /// <summary>Resolves an xml-relative file against the workspace's xml roots, highest layer first.</summary>
    private (string Uri, string Path, string Text)? ResolveXmlRelative(string relativePath, WorkingTextSet texts)
    {
        var roots = reloadService.LastWorkspaceConfig?.XmlDirectories ?? [];
        foreach (var root in roots.Reverse())
        {
            var path = fileHelper.FindInWorkspace([root], relativePath);
            if (path is null) continue;
            var uri = fileHelper.NormalizeUri(path);
            if (texts.GetText(uri) is { } text)
                return (uri, path, text);
        }

        return null;
    }

    // ── Rename delegation ────────────────────────────────────────────────────

    private (StoryCommandEdit?, string?) RenameEvent(
        ExecuteStoryCommandParams request, StoryCampaignModel model, WorkingTextSet texts, bool composable)
    {
        if (string.IsNullOrEmpty(request.EventName) || string.IsNullOrWhiteSpace(request.NewName))
            return (null, "renameEvent needs eventName and newName.");

        // In a batch (composable), always take the model-only path: it re-parses the working text so
        // its positions are correct after earlier commands, and it produces per-file XML edits that
        // compose. The symbol-index path below produces an opaque, index-position (Lua-spanning) edit
        // that can't be folded into a batch - the trade-off is that a staged rename doesn't rewrite
        // Lua references (story-symbol indexing is opt-in and off by default anyway).
        //
        // Outside a batch, prefer the index-wide symbol rename when the event is a story symbol - it
        // also renames Lua occurrences. When story-symbol indexing is off or the event was just
        // created and isn't indexed yet, fall back to the campaign-scoped model rename.
        var index = indexService.Current;
        if (composable || !StoryRenameGuard.IsStorySymbol(request.EventName!, index))
            return ModelRenameEvent(request, model, texts);

        if (StoryRenameGuard.Check(request.EventName!, request.NewName, index) is { } objection)
            return (null, objection);

        var edit = XmlObjectRenameBuilder.Build(
            request.EventName!, request.NewName!, index, schema, textSource, logger);
        if (edit is null)
            return (null, $"'{request.EventName}' cannot be renamed (not owned by the project layer).");

        return (new StoryCommandEdit([], $"Rename story event '{request.EventName}'", PrebuiltEdit: edit), null);
    }

    /// <summary>
    ///     Renames a story event using only the campaign model (no symbol-index dependency): the
    ///     event's <c>Name</c> attribute plus every in-campaign reference - prereq tokens and
    ///     <c>StoryEventName</c> params (<c>TRIGGER_EVENT</c>, <c>RESET_EVENT</c>,
    ///     <c>DISABLE_STORY_EVENT</c>, …). Story event names are campaign-scoped, so this is the
    ///     correct breadth; Lua occurrences are only renamed by the symbol-based path (and there are
    ///     none tracked when symbol indexing is off).
    /// </summary>
    private (StoryCommandEdit?, string?) ModelRenameEvent(
        ExecuteStoryCommandParams request, StoryCampaignModel model, WorkingTextSet texts)
    {
        var oldName = request.EventName!;
        var newName = request.NewName!.Trim();

        // Re-parse each thread from its CURRENT text - the cached model's ranges may predate
        // unflushed edits, which produces stale/out-of-bounds ranges the client rejects wholesale
        // (this is exactly why ThreadOp re-parses before writing). Compute the rename off the fresh
        // parse so every range matches the document the edit lands on.
        // Parse each distinct file ONCE: a thread can appear in the model more than once (the same
        // file referenced by multiple faction manifests - common for shared tutorial threads), and
        // parsing it twice would emit the definition/reference edits twice, i.e. overlapping edits
        // that the client rejects wholesale.
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        var threads = new List<(string Uri, StoryThread Thread)>();
        foreach (var modelThread in model.Threads)
        {
            var uri = fileHelper.NormalizeUri(modelThread.DocumentUri);
            if (!seenUris.Add(uri)) continue;
            var text = texts.GetText(uri);
            if (text is not null)
                threads.Add((uri, StoryThreadParser.Parse(text, uri)));
        }

        var allEvents = threads.SelectMany(t => t.Thread.Events).ToList();
        var definitionCount = allEvents.Count(e =>
            string.Equals(e.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (definitionCount == 0)
            return (null, $"Event '{oldName}' was not found in campaign '{request.Campaign}'.");
        if (definitionCount > 1)
            return (null, $"'{oldName}' names {definitionCount} events in campaign '{request.Campaign}' - " +
                          "rename is ambiguous. Disambiguate them first.");
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)
            && allEvents.Any(e => string.Equals(e.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return (null, $"'{newName}' already names an event in campaign '{request.Campaign}'.");

        var definitionThread = threads.First(t =>
            t.Thread.Events.Any(e => string.Equals(e.Name, oldName, StringComparison.OrdinalIgnoreCase)));
        if (ReadOnlyLayerObjection(definitionThread.Uri) is { } objection)
            return (null, objection);

        var eventParamRefs = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryEventType"));
        var rewardParamRefs = StoryReferenceTypes.BuildParamMap(schema.GetEnum("StoryRewardType"));

        static bool IsEventNameParam(
            IReadOnlyDictionary<(string, int), string> map, string? type, int position)
        {
            return type is not null
                   && map.TryGetValue((type.ToUpperInvariant(), position), out var referenceType)
                   && string.Equals(referenceType, StoryReferenceTypes.EventName, StringComparison.Ordinal);
        }

        var byUri = new Dictionary<string, List<StoryTextEdit>>(StringComparer.Ordinal);

        void AddEdit(string threadUri, StorySourceRange range)
        {
            if (!byUri.TryGetValue(threadUri, out var list)) byUri[threadUri] = list = [];
            // A degraded (mixed-content) prereq line can hand several tokens the same span; dedupe
            // so the workspace edit never contains overlapping replacements.
            if (list.All(e => !e.Range.Equals(range))) list.Add(new StoryTextEdit(range, newName));
        }

        foreach (var (uri, thread) in threads)
        foreach (var storyEvent in thread.Events)
        {
            if (string.Equals(storyEvent.Name, oldName, StringComparison.OrdinalIgnoreCase))
                AddEdit(uri, storyEvent.NameRange);

            foreach (var group in storyEvent.PrereqGroups)
            foreach (var token in group.Tokens)
                if (string.Equals(token.Text, oldName, StringComparison.OrdinalIgnoreCase))
                    AddEdit(uri, token.Range);

            foreach (var param in storyEvent.EventParams)
                if (IsEventNameParam(eventParamRefs, storyEvent.EventType, param.Position)
                    && string.Equals(param.RawValue.Trim(), oldName, StringComparison.OrdinalIgnoreCase))
                    AddEdit(uri, param.Range);

            foreach (var param in storyEvent.RewardParams)
                if (IsEventNameParam(rewardParamRefs, storyEvent.RewardType, param.Position)
                    && string.Equals(param.RawValue.Trim(), oldName, StringComparison.OrdinalIgnoreCase))
                    AddEdit(uri, param.Range);
        }

        if (byUri.Count == 0)
            return (null, "The rename produced no changes.");

        // Uses the `changes` map (not documentChanges): the versioned-identifier form is rejected by
        // the client for thread files that aren't open/tracked (mirrors XmlObjectRenameBuilder).
        var files = byUri
            .Select(kvp => new StoryFileEdit(kvp.Key, SortedNonOverlapping(kvp.Value).ToList()))
            .ToList();
        foreach (var file in files)
            logger.LogDebug("Rename '{Old}'→'{New}' in {Uri}: {Edits}", oldName, newName, file.Uri,
                string.Join(", ", file.Edits.Select(e =>
                    $"[{e.Range.StartLine},{e.Range.StartColumn}-{e.Range.EndLine},{e.Range.EndColumn}]")));
        return (new StoryCommandEdit(files, $"Rename story event '{oldName}'", true), null);
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    private string? ReadOnlyLayerObjection(string uri)
    {
        var index = indexService.Current;
        if (!index.Documents.TryGetValue(uri, out var doc)) return null;
        var leaf = index.LeafLayerRank;
        if (leaf <= 0 || doc.LayerRank >= leaf) return null;
        var layer = doc.LayerName ?? "a dependency";
        return $"'{uri}' belongs to {layer}, which is read-only. " +
               "Copy the file into your project's xml directory to override it, then edit the copy.";
    }

    // ── Workspace-edit construction ──────────────────────────────────────────

    /// <summary>Builds the client-ready edit for one command in its native shape.</summary>
    public static WorkspaceEdit BuildWorkspaceEdit(StoryCommandEdit produced)
    {
        if (produced.PrebuiltEdit is { } prebuilt) return prebuilt;
        return produced.UseChangesMap
            ? BuildChangesMapEdit(produced.Files)
            : BuildDocumentChangesEdit(produced.Files);
    }

    /// <summary>
    ///     documentChanges form: file creations (with skeleton) first - so a freshly created
    ///     thread/manifest exists before anything references it - then one text-edit change per file.
    /// </summary>
    public static WorkspaceEdit BuildDocumentChangesEdit(IReadOnlyList<StoryFileEdit> files)
    {
        var changes = new List<WorkspaceEditDocumentChange>();
        foreach (var file in files)
        {
            if (file.CreateWithSkeleton is not { } skeleton) continue;
            changes.Add(new WorkspaceEditDocumentChange(new CreateFile
            {
                Uri = DocumentUri.From(file.Uri),
                Options = new CreateFileOptions { IgnoreIfExists = true }
            }));
            changes.Add(new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(file.Uri) },
                Edits = new TextEditContainer(new TextEdit { Range = new Range(0, 0, 0, 0), NewText = skeleton })
            }));
        }

        foreach (var file in files)
        {
            if (file.Edits.Count == 0) continue;
            changes.Add(new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(file.Uri) },
                Edits = new TextEditContainer(SortedNonOverlapping(file.Edits).Select(ToLspEdit))
            }));
        }

        return new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) };
    }

    /// <summary>
    ///     `changes` map form (URI → edits) for renames: the versioned documentChanges identifier is
    ///     rejected by the client for thread files that aren't open/tracked.
    /// </summary>
    public static WorkspaceEdit BuildChangesMapEdit(IReadOnlyList<StoryFileEdit> files)
    {
        var changes = files.ToDictionary(
            f => DocumentUri.From(f.Uri),
            f => (IEnumerable<TextEdit>)SortedNonOverlapping(f.Edits).Select(ToLspEdit).ToList());
        return new WorkspaceEdit { Changes = changes };
    }

    private static TextEdit ToLspEdit(StoryTextEdit e)
    {
        return new TextEdit
        {
            Range = new Range(e.Range.StartLine, e.Range.StartColumn, e.Range.EndLine, e.Range.EndColumn),
            NewText = e.NewText
        };
    }

    /// <summary>
    ///     Orders edits by start position and drops any that would overlap the previous one, so a
    ///     document never carries overlapping text edits (which the client rejects wholesale).
    /// </summary>
    public static IEnumerable<StoryTextEdit> SortedNonOverlapping(IEnumerable<StoryTextEdit> edits)
    {
        var ordered = edits
            .Where(e => e.Range.StartLine >= 0) // guard against unresolved (None) ranges
            .OrderBy(e => e.Range.StartLine).ThenBy(e => e.Range.StartColumn)
            .ToList();
        var result = new List<StoryTextEdit>();
        foreach (var edit in ordered)
        {
            var last = result.Count > 0 ? result[^1].Range : null;
            var startsAfterLast = last is null
                                  || edit.Range.StartLine > last.EndLine
                                  || (edit.Range.StartLine == last.EndLine && edit.Range.StartColumn >= last.EndColumn);
            if (startsAfterLast) result.Add(edit);
        }

        return result;
    }
}