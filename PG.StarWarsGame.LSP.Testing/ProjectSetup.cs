// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Testing;

/// <summary>
///     A project as the server resolves it: a stack of layers, lowest first, each a project in a
///     folder of its own that references the one below. The shapes every layer- or
///     project-sensitive suite runs against (#280), so a defect that only a stack shows is not left
///     to the setups a suite happened to build.
/// </summary>
/// <remarks>
///     The layers are what <c>ModProjectResolver</c> builds from <see cref="Disk" />'s project files -
///     roots absolute, forward-slashed and spelled as the project files spell them, ranks
///     dependencies first - and a test holds them to it.
/// </remarks>
public sealed class ProjectSetup
{
    private static readonly string DriveRoot = Path.GetPathRoot(Path.GetFullPath("."))!;

    private ProjectSetup(string name, params string[] projects)
    {
        Name = name;
        Projects = projects;
        Layers = projects.Select((_, rank) => LayerOf(rank)).ToList();
    }

    /// <summary>A mod with nothing beneath it.</summary>
    public static ProjectSetup ModAlone { get; } = new("mod alone", "Mod");

    /// <summary>A mod over the game.</summary>
    public static ProjectSetup ModOverVanilla { get; } = new("mod over vanilla", "Vanilla", "Mod");

    /// <summary>A mod over another mod over the game: a layer that is neither the lowest nor the leaf.</summary>
    public static ProjectSetup ModOverModOverVanilla { get; } =
        new("mod over mod over vanilla", "Vanilla", "Base", "Mod");

    /// <summary>Every shape, smallest first.</summary>
    public static IReadOnlyList<ProjectSetup> All { get; } = [ModAlone, ModOverVanilla, ModOverModOverVanilla];

    /// <summary>The shapes' names, for theory data: a name serializes, a setup does not.</summary>
    public static IReadOnlyList<string> Names { get; } = All.Select(s => s.Name).ToList();

    /// <summary>Every shape with every rank in it, for a test that puts a file in each layer in turn.</summary>
    public static IReadOnlyList<(string Name, int Rank)> NamesAndRanks { get; } =
        All.SelectMany(s => s.Layers.Select(l => (s.Name, l.Rank))).ToList();

    public string Name { get; }

    /// <summary>The projects' names, lowest layer first.</summary>
    public IReadOnlyList<string> Projects { get; }

    public IReadOnlyList<ProjectLayer> Layers { get; }

    /// <summary>The project the server is started on, the highest layer.</summary>
    public ProjectLayer Leaf => Layers[^1];

    public int LeafRank => Layers.Count - 1;

    /// <summary>
    ///     The configuration the server is started with: the layers, and their directories merged
    ///     dependencies first, as <c>ModProjectResolver</c> merges them.
    /// </summary>
    public WorkspaceConfiguration Configuration()
    {
        return new WorkspaceConfiguration(
            [.. Layers.SelectMany(l => l.XmlDirectories)], [.. Layers.SelectMany(l => l.ScriptRoots)],
            [.. Layers.SelectMany(l => l.TextRoots)], [.. Layers.SelectMany(l => l.AssetRoots)], "CSV")
        {
            Layers = Layers,
            StoryDialogRoots = [.. Layers.SelectMany(l => l.StoryDialogRoots)]
        };
    }

    public static ProjectSetup Named(string name)
    {
        return All.Single(s => s.Name == name);
    }

    /// <summary>The folder of the project at a rank, in its case on disk.</summary>
    public string FolderOf(int rank)
    {
        return Path.Combine(DriveRoot, "Work", Projects[rank]);
    }

    /// <summary>The project file at a rank, in its case on disk.</summary>
    public string ProjectFileOf(int rank)
    {
        return Path.Combine(FolderOf(rank), Projects[rank] + ".pgproj");
    }

    /// <summary>A path under the <c>Data</c> folder of the project at a rank, in its case on disk.</summary>
    public string PathOf(int rank, string underData)
    {
        return Path.Combine(FolderOf(rank), "Data", underData);
    }

    /// <summary>A document URI under the <c>Data</c> folder of the project at a rank, as the editor sends it.</summary>
    public string Uri(int rank, string underData)
    {
        return "file:///" + PathOf(rank, underData).Replace('\\', '/').TrimStart('/');
    }

    /// <summary>A script's URI under the <c>Data/Scripts</c> folder of the project at a rank.</summary>
    public string ScriptUri(int rank, string underScripts)
    {
        return Uri(rank, "Scripts/" + underScripts);
    }

    /// <summary>A Lua global the script at <paramref name="uri" /> defines.</summary>
    public static GameSymbol LuaGlobal(string name, string uri)
    {
        return new GameSymbol(name, GameSymbolKind.LuaGlobal, null, new FileOrigin(uri, 0, null), null);
    }

    /// <summary>A use of a Lua global in the script at <paramref name="uri" />.</summary>
    public static GameReference LuaGlobalUse(string name, string uri, int line, int character)
    {
        return new GameReference(name, GameSymbolKind.LuaGlobal, null, uri, line, character, name.Length);
    }

    /// <summary>
    ///     A script of the project at a rank, indexed as the server indexes it: under the layer's rank
    ///     and name, with the modules it requires.
    /// </summary>
    public DocumentIndex Script(int rank, string underScripts, IEnumerable<string>? requires = null,
        IEnumerable<GameSymbol>? defines = null, IEnumerable<GameReference>? uses = null)
    {
        return new DocumentIndex(ScriptUri(rank, underScripts), 1, [.. defines ?? []], [.. uses ?? []],
            [.. requires ?? []], LayerRank: rank, LayerName: Projects[rank]);
    }

    /// <summary>The index of these documents: each keyed by its URI, its symbols and uses collected by id.</summary>
    public static GameIndex Index(params DocumentIndex[] documents)
    {
        return new GameIndex(GameIndex.Empty.Baseline,
            documents.ToImmutableDictionary(d => d.DocumentUri, DocumentUris.Comparer),
            documents.SelectMany(d => d.Symbols).GroupBy(s => s.Id)
                .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray()),
            documents.SelectMany(d => d.References).GroupBy(r => r.TargetId)
                .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray()));
    }

    /// <summary>
    ///     A file system holding every project file and its folders, in their case on disk. Each
    ///     project references the one below it.
    /// </summary>
    public MockFileSystem Disk()
    {
        var disk = new MockFileSystem();
        for (var rank = 0; rank < Projects.Count; rank++)
        {
            foreach (var folder in new[] { "Xml", "Scripts", "Scripts/Story", "Text", "Art", "Audio" })
                disk.AddDirectory(PathOf(rank, folder));
            disk.AddFile(ProjectFileOf(rank), new MockFileData(ProjectFileText(rank)));
        }

        return disk;
    }

    private string ProjectFileText(int rank)
    {
        var reference = rank == 0 ? "" : $"{{ \"path\": \"../{Projects[rank - 1]}/{Projects[rank - 1]}.pgproj\" }}";
        return $$"""
                 {
                   "_type": "aetswg.ModProject",
                   "_typeVersion": "aetswg-1.0.0",
                   "name": "{{Projects[rank]}}",
                   "directories": {
                     "xml": ["Data/Xml"],
                     "scripts": ["Data/Scripts"],
                     "art": ["Data/Art"],
                     "audio": ["Data/Audio"],
                     "storyDialog": ["Data/Scripts/Story"]
                   },
                   "localisation": { "type": "CSV", "directory": "Data/Text" },
                   "projectReferences": [{{reference}}]
                 }
                 """;
    }

    private ProjectLayer LayerOf(int rank)
    {
        var root = Forward(FolderOf(rank));
        return new ProjectLayer(rank, Projects[rank], [root + "/Data/Xml"], [root + "/Data/Scripts"],
            [root + "/Data/Text"], [root + "/Data/Art", root + "/Data/Audio"], "CSV", Forward(ProjectFileOf(rank)))
        {
            StoryDialogRoots = [root + "/Data/Scripts/Story"]
        };
    }

    private static string Forward(string path)
    {
        return path.Replace('\\', '/');
    }
}