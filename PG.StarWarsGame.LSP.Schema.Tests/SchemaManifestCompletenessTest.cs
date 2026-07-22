// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Guards <c>eaw/_index.json</c> against drifting from the files on disk.
///     <para>
///         The two providers discover schema files differently: <c>LocalFileSchemaProvider</c>
///         enumerates the directory, while <c>HttpSchemaProvider</c> fetches <c>_index.json</c> and
///         loads only what it lists. A file added without being registered therefore works for every
///         developer and silently does not exist for anyone loading the schema over HTTP - no error,
///         just missing completion and validation. This drift is invisible to the JSON-Schema CI,
///         which checks file contents rather than manifest membership.
///     </para>
///     <para>
///         Caught 2026-07-19 with 8 unregistered files, including <c>tags/StoryPlotManifest.yaml</c>
///         (an entire tag set) and 5 enums.
///     </para>
/// </summary>
public sealed class SchemaManifestCompletenessTest
{
    private static readonly string[] ManifestSections = ["types", "tags", "enums", "hardcoded", "meta"];

    [Fact]
    public void EveryYamlFileOnDiskIsListedInTheManifest()
    {
        var (root, listed) = LoadManifest();

        var onDisk = Directory
            .EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        var unregistered = onDisk.Where(f => !listed.Contains(f)).OrderBy(f => f).ToList();

        Assert.True(unregistered.Count == 0,
            "Schema files exist on disk but are not listed in eaw/_index.json, so HttpSchemaProvider "
            + "will not load them: " + string.Join(", ", unregistered)
            + ". Add them to the manifest and re-run UpdateSchemaHash.ps1.");
    }

    [Fact]
    public void EveryManifestEntryExistsOnDisk()
    {
        var (root, listed) = LoadManifest();

        var missing = listed
            .Where(rel => !File.Exists(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))))
            .OrderBy(f => f)
            .ToList();

        Assert.True(missing.Count == 0,
            "eaw/_index.json lists files that do not exist, so HttpSchemaProvider will fail to fetch "
            + "them: " + string.Join(", ", missing));
    }

    private static (string Root, HashSet<string> Listed) LoadManifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "schema", "eaw")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException(
                "schema/eaw/ not found - ensure the schema repository is checked out alongside this one.");

        var root = Path.Combine(dir.FullName, "schema", "eaw");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_index.json")));

        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in ManifestSections)
        {
            if (!doc.RootElement.TryGetProperty(section, out var array)) continue;
            foreach (var entry in array.EnumerateArray())
                if (entry.GetString() is { } value)
                    listed.Add(value);
        }

        return (root, listed);
    }
}
