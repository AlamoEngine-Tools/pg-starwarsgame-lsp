// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;

using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Story.Model;

/// <summary>
///     One campaign FACTION's executable story model: parsed threads, suspension state, graph.
///     <para>
///         Scoped to a faction, not to a campaign. A campaign declares a plot manifest per faction,
///         and those are separate chains - the playable faction's plots are what the player runs,
///         and an unplayable faction's are triggered by the AI. The engine never runs two of them
///         as one story, so a model that merged them reported findings the engine cannot produce.
///     </para>
/// </summary>
public sealed record StoryCampaignModel(
    string CampaignName,
    string Faction,
    IReadOnlyList<StoryThread> Threads,
    IReadOnlySet<string> SuspendedThreadUris,
    StoryGraph Graph)
{
    /// <summary>Lua script names (extensionless) attached by the included plot manifests.</summary>
    public IReadOnlyList<string> LuaScripts { get; init; } = [];

    /// <summary>
    ///     Tactical plot manifest file (chain-scanner-canonical) → document URIs of the threads
    ///     it includes. Drives <see cref="StoryGraphBuilder" />'s tactical-entry edges.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> TacticalManifestThreads { get; init; } =
        new Dictionary<string, IReadOnlySet<string>>();
}

/// <summary>
///     Assembles one campaign faction's model from the chain scan associations: that faction's
///     plot manifest → threads, recursing through tactical plot references. A thread is suspended
///     when no manifest in THAT faction's closure lists it as <c>Active_Plot</c>. Thread content
///     comes through the given reader (open-buffer-first in production), so unsaved edits flow into
///     the model.
/// </summary>
public sealed class StoryCampaignAssembler(ISchemaProvider schema)
{
    /// <param name="faction">
    ///     The faction whose manifest seeds the walk. Null is returned when the campaign declares
    ///     no manifest for it - a caller asking for a faction that is not there is asking about
    ///     nothing, and answering with another faction's chain is how this went wrong before.
    /// </param>
    public StoryCampaignModel? Assemble(string campaignName, string faction,
        StoryChainScanResult chain, Func<string, (string Uri, string Text)?> readThread)
    {
        var campaigns = chain.Campaigns
            .Where(c => c.Name.Equals(campaignName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (campaigns.Count == 0) return null;

        // The campaign may be declared more than once across layers; every declaration's manifest
        // for THIS faction seeds the walk, which is what the merged version did per campaign.
        var factionManifests = campaigns
            .SelectMany(c => c.FactionManifests)
            .Where(f => f.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.ManifestFile)
            .ToList();
        if (factionManifests.Count == 0) return null;

        var manifestsByFile = new Dictionary<string, StoryManifestContents>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in chain.Manifests)
            manifestsByFile.TryAdd(manifest.ManifestFile, manifest);
        var tacticalByThread = chain.TacticalReferences
            .ToLookup(t => t.ThreadFile, StringComparer.OrdinalIgnoreCase);
        var tacticalManifestFiles = new HashSet<string>(
            chain.TacticalReferences.Select(t => t.ManifestFile), StringComparer.OrdinalIgnoreCase);

        var manifestQueue = new Queue<string>(factionManifests);
        var manifestSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var threadFiles = new List<string>();
        var threadSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var luaScripts = new List<string>();
        // Manifest file (only those reached via a tactical reference) → the raw thread files it
        // lists, resolved to document URIs once threads are read below.
        var tacticalManifestThreadFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        while (manifestQueue.Count > 0)
        {
            var manifestFile = manifestQueue.Dequeue();
            if (!manifestSeen.Add(manifestFile)) continue;
            if (!manifestsByFile.TryGetValue(manifestFile, out var contents)) continue;

            luaScripts.AddRange(contents.LuaScripts);

            if (tacticalManifestFiles.Contains(manifestFile))
                tacticalManifestThreadFiles[manifestFile] =
                    [..contents.ActiveThreads, ..contents.SuspendedThreads];

            foreach (var thread in contents.ActiveThreads)
            {
                activeFiles.Add(thread);
                AddThread(thread);
            }

            foreach (var thread in contents.SuspendedThreads)
                AddThread(thread);
        }

        var threads = new List<StoryThread>();
        var suspendedUris = new HashSet<string>(DocumentUris.Comparer);
        var uriByThreadFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var threadFile in threadFiles)
        {
            // Unreadable threads (baseline-only) are typed by discovery but cannot join the model.
            var read = readThread(threadFile);
            if (read is null) continue;
            var (uri, text) = read.Value;
            threads.Add(StoryThreadParser.Parse(text, uri));
            uriByThreadFile[threadFile] = uri;
            if (!activeFiles.Contains(threadFile))
                suspendedUris.Add(uri);
        }

        var tacticalManifestThreads = tacticalManifestThreadFiles.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlySet<string>)new HashSet<string>(
                kvp.Value.Select(f => uriByThreadFile.GetValueOrDefault(f)).Where(u => u is not null)!,
                StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        return new StoryCampaignModel(campaignName, faction, threads, suspendedUris,
            new StoryGraphBuilder(schema).Build(threads, tacticalManifestThreads))
        {
            LuaScripts = luaScripts,
            TacticalManifestThreads = tacticalManifestThreads
        };

        void AddThread(string threadFile)
        {
            if (!threadSeen.Add(threadFile)) return;
            threadFiles.Add(threadFile);
            foreach (var tactical in tacticalByThread[threadFile])
                manifestQueue.Enqueue(tactical.ManifestFile);
        }
    }
}