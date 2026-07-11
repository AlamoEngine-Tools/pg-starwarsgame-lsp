// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;

namespace PG.StarWarsGame.LSP.Story.Model;

/// <summary>One campaign's executable story model: parsed threads, suspension state, graph.</summary>
public sealed record StoryCampaignModel(
    string CampaignName,
    IReadOnlyList<StoryThread> Threads,
    IReadOnlySet<string> SuspendedThreadUris,
    StoryGraph Graph)
{
    /// <summary>Lua script names (extensionless) attached by the included plot manifests.</summary>
    public IReadOnlyList<string> LuaScripts { get; init; } = [];
}

/// <summary>
///     Assembles one campaign's model from the chain scan associations: faction manifests →
///     threads, recursing through tactical plot references. A thread is suspended when no
///     included manifest lists it as <c>Active_Plot</c>. Thread content comes through the given
///     reader (open-buffer-first in production), so unsaved edits flow into the model.
/// </summary>
public sealed class StoryCampaignAssembler(ISchemaProvider schema)
{
    public StoryCampaignModel? Assemble(string campaignName, StoryChainScanResult chain,
        Func<string, (string Uri, string Text)?> readThread)
    {
        var campaigns = chain.Campaigns
            .Where(c => c.Name.Equals(campaignName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (campaigns.Count == 0) return null;

        var manifestsByFile = new Dictionary<string, StoryManifestContents>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in chain.Manifests)
            manifestsByFile.TryAdd(manifest.ManifestFile, manifest);
        var tacticalByThread = chain.TacticalReferences
            .ToLookup(t => t.ThreadFile, StringComparer.OrdinalIgnoreCase);

        var manifestQueue = new Queue<string>(
            campaigns.SelectMany(c => c.FactionManifests.Select(f => f.ManifestFile)));
        var manifestSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var threadFiles = new List<string>();
        var threadSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var luaScripts = new List<string>();

        while (manifestQueue.Count > 0)
        {
            var manifestFile = manifestQueue.Dequeue();
            if (!manifestSeen.Add(manifestFile)) continue;
            if (!manifestsByFile.TryGetValue(manifestFile, out var contents)) continue;

            luaScripts.AddRange(contents.LuaScripts);

            foreach (var thread in contents.ActiveThreads)
            {
                activeFiles.Add(thread);
                AddThread(thread);
            }

            foreach (var thread in contents.SuspendedThreads)
                AddThread(thread);
        }

        var threads = new List<StoryThread>();
        var suspendedUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var threadFile in threadFiles)
        {
            // Unreadable threads (baseline-only) are typed by discovery but cannot join the model.
            var read = readThread(threadFile);
            if (read is null) continue;
            var (uri, text) = read.Value;
            threads.Add(StoryThreadParser.Parse(text, uri));
            if (!activeFiles.Contains(threadFile))
                suspendedUris.Add(uri);
        }

        return new StoryCampaignModel(campaignName, threads, suspendedUris,
            new StoryGraphBuilder(schema).Build(threads)) { LuaScripts = luaScripts };

        void AddThread(string threadFile)
        {
            if (!threadSeen.Add(threadFile)) return;
            threadFiles.Add(threadFile);
            foreach (var tactical in tacticalByThread[threadFile])
                manifestQueue.Enqueue(tactical.ManifestFile);
        }
    }
}
