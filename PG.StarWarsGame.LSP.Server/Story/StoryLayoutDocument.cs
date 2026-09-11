// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Nodes;
using PG.StarWarsGame.LSP.Core.Persistence;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     What <c>.aetswg/story-layout.json</c> is, as a versioned document.
/// </summary>
/// <remarks>
///     Version 1 is also where the layout stopped naming nodes by file name and event name. A base
///     name cannot tell two threads apart, is computed by the WEBVIEW rather than by the model, and
///     cannot be re-derived by anything on the server - so the server could not answer "where does
///     this event sit" for an event it was holding.
///     <para>
///         An entry now carries one key: the thread's project-relative path and the event name,
///         folded the way the engine folds and hashed TOGETHER. Whole rather than half, because a
///         key beside an event name in the clear would leak the half that was not hashed while
///         looking as though it did not. The graph buckets are hashed by the same rule. See
///         <see cref="DocumentKey.Composite" />.
///     </para>
/// </remarks>
public static class StoryLayoutDocument
{
    public const string TypeName = "aetswg.StoryLayout";

    public static readonly TypeVersion Version = TypeVersion.Of("aetswg", 1);

    public static readonly DocumentShapePin Shape = new(
        TypeName, Version, "dabc4f8e400faa4c19a93300b8ab5a1956644acfb41068fc10fdf64117aba9c4");

    /// <summary>
    ///     One node's position. Which node it is - the thread and the event together - is the key,
    ///     hashed whole; nothing beside it names either part.
    /// </summary>
    public sealed record Entry
    {
        public Guid Key { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
    }

    /// <summary>
    ///     The graphs, keyed by the campaign and faction hashed together - the same rule the entries
    ///     use, so no part of any identity is left in the clear beside a hash of the rest.
    /// </summary>
    public sealed record Payload
    {
        public Dictionary<string, List<Entry>> Graphs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The chain, given a way to resolve the base names version zero used. The resolver is
    ///     passed in because it needs the workspace, which a document type has no business holding.
    /// </summary>
    public static IReadOnlyList<IDocumentMigration> MigrationsUsing(
        Func<string, string, Guid?> nodeKeyForBaseName, Func<string, Guid> graphKey)
    {
        return [new NameNodesByKey(nodeKeyForBaseName, graphKey)];
    }

    /// <summary>
    ///     Version zero was <c>{ "campaign/faction": [ { file, eventName, x, y } ] }</c> - a bare
    ///     map with no envelope, naming threads by file name. This gives it a root object to carry
    ///     one and swaps every file name for the thread's key.
    /// </summary>
    private sealed class NameNodesByKey(
        Func<string, string, Guid?> nodeKeyForBaseName, Func<string, Guid> graphKey) : IDocumentMigration
    {
        private int _dropped;

        public string TypeName => StoryLayoutDocument.TypeName;
        public TypeVersion From => TypeVersion.Zero("aetswg");
        public TypeVersion To => Version;

        /// <summary>
        ///     Written by <see cref="Migrate" /> and read after it, because how many positions could
        ///     not be matched is only known once the file has been walked. The store runs one
        ///     migration instance per load, so there is no state to leak between them.
        /// </summary>
        public string? UserNotice => _dropped == 0
            ? null
            : $"{_dropped} saved node position{(_dropped == 1 ? "" : "s")} named a story file that no "
              + "longer matches exactly one thread, so they were dropped. Those events will be "
              + "arranged automatically the next time their graph opens.";

        public JsonNode Migrate(JsonNode document)
        {
            var graphs = new JsonObject();
            if (document is not JsonObject buckets) return new JsonObject { ["graphs"] = graphs };

            foreach (var (bucket, value) in buckets)
            {
                if (value is not JsonArray entries) continue;

                var kept = new JsonArray();
                foreach (var entry in entries)
                {
                    if (entry is not JsonObject fields) continue;

                    var file = (string?)fields["file"];
                    var eventName = (string?)fields["eventName"] ?? string.Empty;
                    // No base name, or one that names no thread or several: dropped rather than
                    // guessed. Guessing would move somebody's nodes onto the wrong graph.
                    if (file is null || nodeKeyForBaseName(file, eventName) is not { } key)
                    {
                        _dropped++;
                        continue;
                    }

                    kept.Add(new JsonObject
                    {
                        ["key"] = key.ToString(),
                        ["x"] = (double?)fields["x"] ?? 0,
                        ["y"] = (double?)fields["y"] ?? 0
                    });
                }

                // The bucket was "campaign/faction", or the campaign alone in the oldest files.
                // Either way it is hashed as the composite it always was - the caller knows which
                // shape it is looking at.
                graphs[graphKey(bucket).ToString()] = kept;
            }

            return new JsonObject { ["graphs"] = graphs };
        }
    }
}
