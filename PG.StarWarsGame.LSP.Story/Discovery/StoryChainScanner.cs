// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Story.Discovery;

/// <summary>
///     Walks the campaign story chain: campaign registry (campaignfiles.xml) → campaign documents
///     (<c>*_Story_Name</c> tags) → plot manifests (<c>Active_Plot</c>/<c>Suspended_Plot</c>/
///     <c>Lua_Script</c>) → story thread files, recursing into tactical plot manifests referenced
///     from thread events (STORY_LAND_TACTICAL / STORY_SPACE_TACTICAL event param 1, LINK_TACTICAL
///     reward param 7). Missing files that the shipped baseline knows are accepted silently
///     (registration only); anything else produces a <see cref="StoryChainProblem" /> anchored to
///     the referencing value - no silent guessing.
/// </summary>
public sealed class StoryChainScanner
{
    private static readonly HashSet<string> StoryNameTags = new(StringComparer.OrdinalIgnoreCase)
        { "Rebel_Story_Name", "Empire_Story_Name", "Underworld_Story_Name" };

    private static readonly HashSet<string> TacticalEventTypes = new(StringComparer.OrdinalIgnoreCase)
        { "STORY_LAND_TACTICAL", "STORY_SPACE_TACTICAL" };

    private readonly IStoryChainFileResolver _resolver;

    public StoryChainScanner(IStoryChainFileResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    ///     Runs the full chain scan starting from the best copy of the campaign registry metafile
    ///     (xml-directory-relative, e.g. <c>"campaignfiles.xml"</c>).
    /// </summary>
    public StoryChainScanResult Scan(string campaignRegistryRelativePath)
    {
        var registry = _resolver.ReadFile(ToXmlRelativePath(campaignRegistryRelativePath));
        return registry is null ? StoryChainScanResult.Empty : Scan([registry]);
    }

    /// <summary>
    ///     Runs the chain scan over every copy of the campaign registry (a mod and its
    ///     dependencies may each ship one); campaign lists are unioned, mirroring how
    ///     file-registry metafiles are already merged across layers.
    /// </summary>
    public StoryChainScanResult Scan(IReadOnlyList<StoryChainFile> registryCopies)
    {
        var state = new ScanState(_resolver);

        var campaignFiles = new List<string>();
        var campaignSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var copy in registryCopies)
        {
            var doc = XmlUtility.CreateHtmlDocument(copy.Content);
            foreach (var fileNode in doc.DocumentNode.Descendants()
                         .Where(n => n.NodeType == HtmlNodeType.Element &&
                                     n.Name.Equals("File", StringComparison.OrdinalIgnoreCase)))
            {
                var value = fileNode.InnerText.Trim();
                if (value.Length == 0) continue;
                var rel = ToXmlRelativePath(value);
                if (campaignSeen.Add(rel)) campaignFiles.Add(rel);
            }
        }

        foreach (var campaignRel in campaignFiles)
            ScanCampaignFile(campaignRel, state);

        return new StoryChainScanResult(state.Manifests, state.Threads, state.LuaScripts, state.Problems)
        {
            Campaigns = state.Campaigns,
            Manifests = state.ManifestContents,
            TacticalReferences = state.TacticalReferences
        };
    }

    // ── Chain stages ─────────────────────────────────────────────────────────

    private void ScanCampaignFile(string campaignRel, ScanState state)
    {
        // A campaign file the registry lists but no layer ships is the fileRegistry definition's
        // concern (it registers the same entries); the chain scan stays quiet about it.
        var file = _resolver.ReadFile(campaignRel);
        if (file is null) return;

        var source = SourceDocument.Parse(campaignRel, file);
        var processed = new HashSet<HtmlNode>();

        foreach (var campaignNode in source.Doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 n.Name.Equals("Campaign", StringComparison.OrdinalIgnoreCase)))
        {
            var campaignName = campaignNode.GetAttributeValue("Name", string.Empty).Trim();
            var factionManifests = new List<StoryFactionManifest>();

            foreach (var node in campaignNode.Descendants()
                         .Where(n => n.NodeType == HtmlNodeType.Element && StoryNameTags.Contains(n.Name)))
            {
                processed.Add(node);
                var value = node.InnerText.Trim();
                if (value.Length == 0) continue;

                AddManifest(value, source.At(node, value), StoryChainProblemKind.UnresolvedStoryName, state);

                // "Rebel_Story_Name" → faction "Rebel".
                var tagName = node.Name;
                var faction = tagName[..tagName.IndexOf('_')];
                faction = char.ToUpperInvariant(faction[0]) + faction[1..];
                factionManifests.Add(new StoryFactionManifest(faction, ToXmlRelativePath(value)));
            }

            if (campaignName.Length > 0 && factionManifests.Count > 0)
                state.Campaigns.Add(new StoryCampaignChain(campaignName, factionManifests)
                {
                    SourceFile = campaignRel
                });
        }

        // Story-name tags outside a <Campaign> element (malformed nesting) still get their
        // manifests typed - only the campaign association is lost.
        foreach (var node in source.Doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element && StoryNameTags.Contains(n.Name)
                                                                    && !processed.Contains(n)))
        {
            var value = node.InnerText.Trim();
            if (value.Length == 0) continue;
            AddManifest(value, source.At(node, value), StoryChainProblemKind.UnresolvedStoryName, state);
        }
    }

    private void AddManifest(string rawReference, SourceLocation origin,
        StoryChainProblemKind unresolvedKind, ScanState state)
    {
        var rel = ToXmlRelativePath(rawReference);
        if (!state.ManifestSeen.Add(rel)) return;

        var file = _resolver.ReadFile(rel);
        if (file is null)
        {
            if (_resolver.IsKnownToBaseline(rel))
                state.Manifests.Add(rel); // baseline ships it - typed, but nothing to recurse into
            else
                state.AddProblem(origin, unresolvedKind,
                    $"Story plot file '{rawReference}' does not exist in any project layer or the baseline.");
            return;
        }

        state.Manifests.Add(rel);
        ScanManifest(rel, file, origin, state);
    }

    private void ScanManifest(string manifestRel, StoryChainFile file, SourceLocation origin, ScanState state)
    {
        var source = SourceDocument.Parse(manifestRel, file);
        if (!XmlUtility.TryGetRootNode(source.Doc, out var root) ||
            !root!.Name.Equals("Story_Mode_Plots", StringComparison.OrdinalIgnoreCase))
        {
            // Anchored to the entry that referenced the manifest - that is where the user can act.
            state.AddProblem(origin, StoryChainProblemKind.MalformedManifest,
                $"'{origin.Reference}' is not a valid story plot manifest: missing <Story_Mode_Plots> root element.");
            return;
        }

        var activeThreads = new List<string>();
        var suspendedThreads = new List<string>();
        var luaScripts = new List<string>();

        foreach (var node in root.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            var value = node.InnerText.Trim();
            if (value.Length == 0) continue;

            if (node.Name.Equals("Active_Plot", StringComparison.OrdinalIgnoreCase))
            {
                activeThreads.Add(ToXmlRelativePath(value));
                AddThread(value, source.At(node, value), state);
            }
            else if (node.Name.Equals("Suspended_Plot", StringComparison.OrdinalIgnoreCase))
            {
                suspendedThreads.Add(ToXmlRelativePath(value));
                AddThread(value, source.At(node, value), state);
            }
            else if (node.Name.Equals("Lua_Script", StringComparison.OrdinalIgnoreCase))
            {
                luaScripts.Add(value);
                if (state.LuaSeen.Add(value))
                    state.LuaScripts.Add(value);
            }
        }

        state.ManifestContents.Add(
            new StoryManifestContents(manifestRel, activeThreads, suspendedThreads, luaScripts));
    }

    private void AddThread(string rawReference, SourceLocation origin, ScanState state)
    {
        var rel = ToXmlRelativePath(rawReference);
        if (!state.ThreadSeen.Add(rel)) return;

        var file = _resolver.ReadFile(rel);
        if (file is null)
        {
            if (_resolver.IsKnownToBaseline(rel))
                state.Threads.Add(rel);
            else
                state.AddProblem(origin, StoryChainProblemKind.UnresolvedPlotEntry,
                    $"Story file '{rawReference}' does not exist in any project layer or the baseline.");
            return;
        }

        state.Threads.Add(rel);
        ScanThread(rel, file, state);
    }

    private void ScanThread(string threadRel, StoryChainFile file, ScanState state)
    {
        var source = SourceDocument.Parse(threadRel, file);
        foreach (var eventNode in source.Doc.DocumentNode.Descendants()
                     .Where(n => n.NodeType == HtmlNodeType.Element &&
                                 n.Name.Equals("Event", StringComparison.OrdinalIgnoreCase)))
        {
            var eventType = FindChild(eventNode, "Event_Type")?.InnerText.Trim();
            if (eventType is not null && TacticalEventTypes.Contains(eventType))
                AddTacticalReference(FindChild(eventNode, "Event_Param1"), source, state);

            var rewardType = FindChild(eventNode, "Reward_Type")?.InnerText.Trim();
            if (string.Equals(rewardType, "LINK_TACTICAL", StringComparison.OrdinalIgnoreCase))
                AddTacticalReference(FindChild(eventNode, "Reward_Param7"), source, state);
        }
    }

    private void AddTacticalReference(HtmlNode? paramNode, SourceDocument source, ScanState state)
    {
        var value = paramNode?.InnerText.Trim();
        if (string.IsNullOrEmpty(value)) return; // param is optional on tactical events

        var reference = new StoryTacticalReference(source.SourceFile, ToXmlRelativePath(value));
        if (state.TacticalSeen.Add((reference.ThreadFile, reference.ManifestFile)))
            state.TacticalReferences.Add(reference);

        AddManifest(value, source.At(paramNode!, value),
            StoryChainProblemKind.UnresolvedTacticalReference, state);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // References come in two forms: xml-dir-relative ("Story_Plots_X.xml") or game-root-relative
    // ("DATA\XML\CAMPAIGNS_X.XML"). Shared with StoryGraphBuilder's tactical-node keying so a
    // manifest reference correlates to the same node regardless of which event's raw text it
    // came from (mirrors WorkspaceIndexer.ToXmlRelativePath).
    private static string ToXmlRelativePath(string reference)
    {
        return StoryReferenceTypes.NormalizeRelativePath(reference);
    }

    private static HtmlNode? FindChild(HtmlNode parent, string tagName)
    {
        return parent.ChildNodes.FirstOrDefault(n =>
            n.NodeType == HtmlNodeType.Element &&
            string.Equals(n.Name, tagName, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ScanState(IStoryChainFileResolver resolver)
    {
        public List<string> Manifests { get; } = [];
        public HashSet<string> ManifestSeen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Threads { get; } = [];
        public HashSet<string> ThreadSeen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> LuaScripts { get; } = [];
        public HashSet<string> LuaSeen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<StoryChainProblem> Problems { get; } = [];

        public List<StoryCampaignChain> Campaigns { get; } = [];
        public List<StoryManifestContents> ManifestContents { get; } = [];
        public List<StoryTacticalReference> TacticalReferences { get; } = [];
        public HashSet<(string, string)> TacticalSeen { get; } = [];

        public void AddProblem(SourceLocation origin, StoryChainProblemKind kind, string message)
        {
            // Vanilla data ships broken story links (debug plots); a user cannot fix those in a
            // baseline-shipped file, so problems there are warnings instead of errors.
            var severity = resolver.IsKnownToBaseline(origin.SourceFile)
                ? XmlDiagnosticSeverity.Warning
                : XmlDiagnosticSeverity.Error;
            Problems.Add(new StoryChainProblem(origin.SourceFile, origin.DocumentUri,
                origin.Line, origin.Column, origin.Line, origin.Column + origin.Reference.Length,
                kind, origin.Reference, message, severity));
        }
    }

    /// <summary>A parsed source file plus everything needed to anchor problems inside it.</summary>
    private sealed record SourceDocument(string SourceFile, string? DocumentUri, HtmlDocument Doc, string[] Lines)
    {
        public static SourceDocument Parse(string sourceFile, StoryChainFile file)
        {
            return new SourceDocument(sourceFile, file.DocumentUri,
                XmlUtility.CreateHtmlDocument(file.Content), file.Content.Split('\n'));
        }

        public SourceLocation At(HtmlNode valueElement, string reference)
        {
            // HAP reports the element's 1-based line; the value itself is located textually so the
            // problem range covers exactly the reference (values sit on the element's line in
            // practice - fall back to the element start otherwise).
            var line = Math.Max(0, XmlUtility.GetLine(valueElement));
            for (var i = line; i < Lines.Length && i <= line + 3; i++)
            {
                var col = Lines[i].IndexOf(reference, StringComparison.Ordinal);
                if (col >= 0) return new SourceLocation(SourceFile, DocumentUri, i, col, reference);
            }

            return new SourceLocation(SourceFile, DocumentUri, line, 0, reference);
        }
    }

    private sealed record SourceLocation(
        string SourceFile,
        string? DocumentUri,
        int Line,
        int Column,
        string Reference);
}