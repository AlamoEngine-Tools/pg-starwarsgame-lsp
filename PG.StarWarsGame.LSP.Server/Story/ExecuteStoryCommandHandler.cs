// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
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
///     Executes story editor mutations: validates against the campaign model, produces minimal
///     edits via <see cref="StoryXmlWriter" />, and applies them through
///     <c>workspace/applyEdit</c> (undo/redo and open-editor sync come free). Non-project layers
///     are read-only — mutations there are rejected with the override-copy guidance.
/// </summary>
public sealed class ExecuteStoryCommandHandler(
    IStoryModelService modelService,
    IGameIndexService indexService,
    IDocumentTextSource textSource,
    ISchemaProvider schema,
    IFileHelper fileHelper,
    IModProjectReloadService reloadService,
    IWorkspaceEditApplier applier,
    ILspConfigurationProvider config,
    ILogger<ExecuteStoryCommandHandler> logger)
    : IJsonRpcRequestHandler<ExecuteStoryCommandParams, ExecuteStoryCommandResult>
{
    /// <summary>The skeleton for a newly created plot manifest file.</summary>
    private const string ManifestFileSkeleton =
        "<?xml version=\"1.0\" ?>\n<Story_Mode_Plots>\n</Story_Mode_Plots>\n";

    public async Task<ExecuteStoryCommandResult> Handle(ExecuteStoryCommandParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Error(rejection);

        var model = modelService.GetCampaignModel(request.Campaign);
        if (model is null)
            return Error($"Campaign '{request.Campaign}' was not found.");

        return request.Kind switch
        {
            "renameEvent" => await RenameEventAsync(request, ct),
            "createEvent" or "deleteEvent" or "setEventType" or "setRewardType" or "setParams"
                or "setPerpetual" or "setDialog" or "setBranch" or "addPrereq" or "removePrereq"
                or "retargetControlEdge" or "createTacticalAttachment"
                => await ThreadOpAsync(request, model, ct),
            "createThread" or "deleteThread" or "setThreadState" or "attachLuaScript"
                => await ManifestOpAsync(request, ct),
            "addPlotManifest" or "removePlotManifest" => await CampaignOpAsync(request, ct),
            _ => Error($"Unknown story command '{request.Kind}'.")
        };
    }

    // ── Thread-file operations ───────────────────────────────────────────────

    private async Task<ExecuteStoryCommandResult> ThreadOpAsync(
        ExecuteStoryCommandParams request, StoryCampaignModel model, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.ThreadUri))
            return Error("The command needs a threadUri.");

        var uri = fileHelper.NormalizeUri(request.ThreadUri);
        if (ReadOnlyLayerObjection(uri) is { } objection)
            return Error(objection);

        var text = textSource.GetText(uri)?.Text;
        if (text is null)
            return Error($"'{uri}' is not readable.");

        // Parse fresh: the model's ranges may predate unflushed edits; the writer's ranges must
        // match the exact text the edits will be applied to.
        var thread = StoryThreadParser.Parse(text, uri);

        IReadOnlyList<StoryTextEdit> edits;
        string label;
        if (request.Kind is "createEvent" or "createTacticalAttachment")
        {
            if (string.IsNullOrWhiteSpace(request.NewName))
                return Error($"{request.Kind} needs a newName.");
            if (model.Threads.SelectMany(t => t.Events).Any(e =>
                    string.Equals(e.Name, request.NewName, StringComparison.OrdinalIgnoreCase)))
                return Error($"'{request.NewName}' already names an event in campaign " +
                             $"'{request.Campaign}' — event names resolve campaign-wide.");
            if (request.Kind == "createTacticalAttachment")
            {
                if (string.IsNullOrEmpty(request.File))
                    return Error("createTacticalAttachment needs the tactical manifest file.");
                var eventType = string.Equals(request.Value, "space", StringComparison.OrdinalIgnoreCase)
                    ? "STORY_SPACE_TACTICAL"
                    : "STORY_LAND_TACTICAL";
                edits = StoryXmlWriter.CreateEvent(text, thread, request.NewName!, eventType, null,
                    [("Event_Param1", request.File!)]);
                label = $"Create tactical attachment '{request.NewName}'";
            }
            else
            {
                edits = StoryXmlWriter.CreateEvent(text, thread, request.NewName!,
                    request.EventType, request.RewardType);
                label = $"Create story event '{request.NewName}'";
            }
        }
        else
        {
            var located = LocateEvent(thread, request.EventName);
            if (located.Error is not null) return Error(located.Error);
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
                    if (prefix is null) return Error("setParams needs paramKind 'event' or 'reward'.");
                    if (request.Params is not { Count: > 0 }) return Error("setParams needs params.");
                    edits = StoryXmlWriter.SetParams(text, storyEvent, prefix,
                        request.Params.Select(p => (p.Position, p.Value)).ToList());
                    label = $"Set params of '{storyEvent.Name}'";
                    break;
                }
                case "retargetControlEdge":
                {
                    if (request.GroupIndex is not { } slot || string.IsNullOrEmpty(request.Token))
                        return Error("retargetControlEdge needs groupIndex (the reward slot) and token.");
                    edits = StoryXmlWriter.SetParams(text, storyEvent, "Reward_Param", [(slot, request.Token)]);
                    label = $"Retarget control edge of '{storyEvent.Name}'";
                    break;
                }
                case "addPrereq":
                    if (string.IsNullOrEmpty(request.Token)) return Error("addPrereq needs a token.");
                    edits = StoryXmlWriter.AddPrereq(text, storyEvent, request.GroupIndex, request.Token!);
                    label = $"Add prereq to '{storyEvent.Name}'";
                    break;
                case "removePrereq":
                {
                    if (request.GroupIndex is not { } group || string.IsNullOrEmpty(request.Token))
                        return Error("removePrereq needs groupIndex and token.");
                    if (group < 0 || group >= storyEvent.PrereqGroups.Count)
                        return Error($"'{storyEvent.Name}' has no prereq group {group}.");
                    if (!storyEvent.PrereqGroups[group].Tokens.Any(t =>
                            string.Equals(t.Text, request.Token, StringComparison.OrdinalIgnoreCase)))
                        return Error($"Prereq group {group} of '{storyEvent.Name}' has no token '{request.Token}'.");
                    edits = StoryXmlWriter.RemovePrereq(text, storyEvent, group, request.Token!);
                    label = $"Remove prereq from '{storyEvent.Name}'";
                    break;
                }
                default:
                    return Error($"Unknown story command '{request.Kind}'.");
            }
        }

        if (edits.Count == 0)
            return Error("The command produced no changes.");

        var applied = await applier.ApplyAsync(ToWorkspaceEdit(uri, edits), label, ct);
        if (!applied)
            return Error("The editor rejected the edit.");

        logger.LogDebug("Story command {Kind} applied to {Uri}", request.Kind, uri);
        return new ExecuteStoryCommandResult(true);
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
            _ => (null, $"'{eventName}' names {matches.Count} events in this thread — fix the duplicate first.")
        };
    }

    // ── Plot manifest operations ─────────────────────────────────────────────

    private async Task<ExecuteStoryCommandResult> ManifestOpAsync(
        ExecuteStoryCommandParams request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.File))
            return Error("The command needs the plot manifest file.");
        if (string.IsNullOrEmpty(request.Value))
            return Error("The command needs a value (thread file or script name).");

        var manifest = ResolveXmlRelative(request.File!);
        if (manifest is null)
            return Error($"Plot manifest '{request.File}' was not found in the workspace.");
        if (ReadOnlyLayerObjection(manifest.Value.Uri) is { } objection)
            return Error(objection);

        var (uri, path, text) = manifest.Value;
        IReadOnlyList<StoryTextEdit> edits;
        var extraChanges = new List<WorkspaceEditDocumentChange>();
        string label;
        switch (request.Kind)
        {
            case "createThread":
            {
                edits = StoryManifestWriter.AddEntry(text, "Active_Plot", request.Value!);
                var threadPath = Path.Combine(Path.GetDirectoryName(path) ?? "", request.Value!);
                var threadUri = fileHelper.NormalizeUri(threadPath);
                if (textSource.GetText(threadUri) is null)
                {
                    extraChanges.Add(new WorkspaceEditDocumentChange(new CreateFile
                    {
                        Uri = DocumentUri.From(threadUri),
                        Options = new CreateFileOptions { IgnoreIfExists = true }
                    }));
                    extraChanges.Add(new WorkspaceEditDocumentChange(new TextDocumentEdit
                    {
                        TextDocument = new OptionalVersionedTextDocumentIdentifier
                        {
                            Uri = DocumentUri.From(threadUri)
                        },
                        Edits = new TextEditContainer(new TextEdit
                        {
                            Range = new Range(0, 0, 0, 0),
                            NewText = StoryManifestWriter.ThreadFileSkeleton
                        })
                    }));
                }

                label = $"Create story thread '{request.Value}'";
                break;
            }
            case "deleteThread":
                edits = StoryManifestWriter.RemoveEntry(text, ["Active_Plot", "Suspended_Plot"], request.Value!);
                if (edits.Count == 0)
                    return Error($"'{request.Value}' is not listed in '{request.File}'. " +
                                 "The file itself is never deleted — remove it manually if intended.");
                label = $"Detach story thread '{request.Value}'";
                break;
            case "setThreadState":
            {
                if (request.Flag is not { } suspended)
                    return Error("setThreadState needs flag (true = suspended).");
                edits = StoryManifestWriter.RetagEntry(text, ["Active_Plot", "Suspended_Plot"],
                    request.Value!, suspended ? "Suspended_Plot" : "Active_Plot");
                if (edits.Count == 0)
                    return Error($"'{request.Value}' is not listed in '{request.File}', " +
                                 "or it is already in the requested state.");
                label = $"Set thread state of '{request.Value}'";
                break;
            }
            case "attachLuaScript":
                edits = StoryManifestWriter.AddEntry(text, "Lua_Script", request.Value!);
                label = $"Attach Lua script '{request.Value}'";
                break;
            default:
                return Error($"Unknown story command '{request.Kind}'.");
        }

        if (edits.Count == 0)
            return Error("The command produced no changes.");

        var workspaceEdit = ToWorkspaceEdit(uri, edits, extraChanges);
        var applied = await applier.ApplyAsync(workspaceEdit, label, ct);
        return applied ? new ExecuteStoryCommandResult(true) : Error("The editor rejected the edit.");
    }

    // ── Campaign set operations ──────────────────────────────────────────────

    private async Task<ExecuteStoryCommandResult> CampaignOpAsync(
        ExecuteStoryCommandParams request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.File))
            return Error("The command needs the plot manifest file.");

        var chain = modelService.GetChainResult();
        var campaignChain = chain.Campaigns.FirstOrDefault(c =>
            string.Equals(c.Name, request.Campaign, StringComparison.OrdinalIgnoreCase));
        if (campaignChain is null || campaignChain.SourceFile.Length == 0)
            return Error($"The campaign set file declaring '{request.Campaign}' could not be located.");

        var source = ResolveXmlRelative(campaignChain.SourceFile);
        if (source is null)
            return Error($"'{campaignChain.SourceFile}' is not readable.");
        if (ReadOnlyLayerObjection(source.Value.Uri) is { } objection)
            return Error(objection);

        var (uri, path, text) = source.Value;
        IReadOnlyList<StoryTextEdit> edits;
        var extraChanges = new List<WorkspaceEditDocumentChange>();
        string label;
        if (request.Kind == "addPlotManifest")
        {
            if (string.IsNullOrEmpty(request.Faction))
                return Error("addPlotManifest needs the faction.");
            edits = StoryManifestWriter.AddCampaignStoryName(
                text, request.Campaign, request.Faction!, request.File!);
            if (edits.Count == 0)
                return Error($"Campaign '{request.Campaign}' was not found in '{campaignChain.SourceFile}'.");

            var manifestPath = Path.Combine(Path.GetDirectoryName(path) ?? "", request.File!);
            var manifestUri = fileHelper.NormalizeUri(manifestPath);
            if (textSource.GetText(manifestUri) is null)
            {
                extraChanges.Add(new WorkspaceEditDocumentChange(new CreateFile
                {
                    Uri = DocumentUri.From(manifestUri),
                    Options = new CreateFileOptions { IgnoreIfExists = true }
                }));
                extraChanges.Add(new WorkspaceEditDocumentChange(new TextDocumentEdit
                {
                    TextDocument = new OptionalVersionedTextDocumentIdentifier
                    {
                        Uri = DocumentUri.From(manifestUri)
                    },
                    Edits = new TextEditContainer(new TextEdit
                    {
                        Range = new Range(0, 0, 0, 0),
                        NewText = ManifestFileSkeleton
                    })
                }));
            }

            label = $"Attach plot manifest '{request.File}'";
        }
        else
        {
            edits = StoryManifestWriter.RemoveCampaignStoryName(text, request.Campaign, request.File!);
            if (edits.Count == 0)
                return Error($"'{request.File}' is not attached to campaign '{request.Campaign}'.");
            label = $"Detach plot manifest '{request.File}'";
        }

        var applied = await applier.ApplyAsync(ToWorkspaceEdit(uri, edits, extraChanges), label, ct);
        return applied ? new ExecuteStoryCommandResult(true) : Error("The editor rejected the edit.");
    }

    /// <summary>Resolves an xml-relative file against the workspace's xml roots, highest layer first.</summary>
    private (string Uri, string Path, string Text)? ResolveXmlRelative(string relativePath)
    {
        var roots = reloadService.LastWorkspaceConfig?.XmlDirectories ?? [];
        foreach (var root in roots.Reverse())
        {
            var path = fileHelper.FindInWorkspace([root], relativePath);
            if (path is null) continue;
            var uri = fileHelper.NormalizeUri(path);
            if (textSource.GetText(uri) is { } text)
                return (uri, path, text.Text);
        }

        return null;
    }

    // ── Rename delegation ────────────────────────────────────────────────────

    private async Task<ExecuteStoryCommandResult> RenameEventAsync(
        ExecuteStoryCommandParams request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.EventName) || string.IsNullOrWhiteSpace(request.NewName))
            return Error("renameEvent needs eventName and newName.");

        var index = indexService.Current;
        if (!StoryRenameGuard.IsStorySymbol(request.EventName!, index))
            return Error($"'{request.EventName}' is not an indexed story symbol — " +
                         "enable story symbols or wait for indexing to finish.");
        if (StoryRenameGuard.Check(request.EventName!, request.NewName, index) is { } objection)
            return Error(objection);

        var edit = XmlObjectRenameBuilder.Build(
            request.EventName!, request.NewName!, index, schema, textSource, logger);
        if (edit is null)
            return Error($"'{request.EventName}' cannot be renamed (not owned by the project layer).");

        var applied = await applier.ApplyAsync(edit, $"Rename story event '{request.EventName}'", ct);
        return applied ? new ExecuteStoryCommandResult(true) : Error("The editor rejected the edit.");
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

    private static WorkspaceEdit ToWorkspaceEdit(
        string uri, IReadOnlyList<StoryTextEdit> edits,
        IReadOnlyList<WorkspaceEditDocumentChange>? extraChanges = null)
    {
        var textEdits = edits.Select(e => new TextEdit
        {
            Range = new Range(
                e.Range.StartLine, e.Range.StartColumn, e.Range.EndLine, e.Range.EndColumn),
            NewText = e.NewText
        });
        // File creations precede the text edits so a freshly created thread/manifest file
        // exists before its skeleton is inserted.
        var changes = new List<WorkspaceEditDocumentChange>(extraChanges ?? [])
        {
            new(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier
                {
                    Uri = DocumentUri.From(uri)
                },
                Edits = new TextEditContainer(textEdits)
            })
        };
        return new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) };
    }

    private static ExecuteStoryCommandResult Error(string message)
    {
        return new ExecuteStoryCommandResult(false, message);
    }
}
