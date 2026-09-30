// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Workspace;

namespace PG.StarWarsGame.LSP.Server.Tests.Workspace;

/// <summary>
///     The directories a client has to watch to see every change this workspace cares about.
/// </summary>
/// <remarks>
///     The client creates its own file watchers from bare globs, and VS Code resolves a bare glob
///     against open workspace FOLDERS only. With the window open on a leaf mod, a referenced
///     project sits outside every folder, so editing it - its XML, its scripts, its localisation,
///     or the <c>.pgproj</c> itself - produced no <c>didChangeWatchedFiles</c> and nothing
///     re-indexed until the server was restarted. The server is the only side that knows where the
///     layers are, so it has to say.
/// </remarks>
public sealed class GetWatchDirectoriesHandlerTest
{
    private static ProjectLayer Layer(int rank, string name, string projectDir,
        string[]? xml = null, string[]? scripts = null, string[]? text = null)
    {
        return new ProjectLayer(rank, name, xml ?? [], scripts ?? [], text ?? [], [], null,
            $"{projectDir}/{name}.pgproj");
    }

    private static GetWatchDirectoriesHandler Handler(params ProjectLayer[] layers)
    {
        var map = new ProjectLayerMap(new FileHelper(new MockFileSystem()));
        map.SetLayers(layers);
        return new GetWatchDirectoriesHandler(map);
    }

    private static IReadOnlyList<string> Resolve(params ProjectLayer[] layers)
    {
        return Handler(layers)
            .Handle(new GetWatchDirectoriesParams(), CancellationToken.None)
            .GetAwaiter().GetResult()
            .Directories;
    }

    /// <summary>
    ///     Every layer's content is COVERED - by that directory itself or by an ancestor that is
    ///     also being watched. Asserting the literal paths would contradict the minimisation below,
    ///     and coverage is the property the client actually needs.
    /// </summary>
    [Fact]
    public void EveryLayersContentIsCovered()
    {
        var dirs = Resolve(
            Layer(1, "leaf", "c:/mods/rev",
                ["c:/mods/rev/data/xml"], ["c:/mods/rev/data/scripts"], ["c:/mods/rev/data/text"]),
            Layer(0, "core", "c:/mods/data",
                ["c:/mods/data/xml"], ["c:/mods/data/scripts"], ["c:/mods/data/text"]));

        string[] content =
        [
            "c:/mods/rev/data/xml", "c:/mods/rev/data/scripts", "c:/mods/rev/data/text",
            "c:/mods/data/xml", "c:/mods/data/scripts", "c:/mods/data/text"
        ];
        foreach (var path in content)
            Assert.True(dirs.Any(d => path.Equals(d, StringComparison.OrdinalIgnoreCase)
                                      || path.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)),
                $"'{path}' is watched by none of: {string.Join(", ", dirs)}");
    }

    /// <summary>
    ///     A content directory outside every project directory is reported in its own right - the
    ///     declared directories are absolute and nothing requires them to sit under the .pgproj.
    /// </summary>
    [Fact]
    public void AContentDirectoryOutsideTheProjectDirectoryIsReported()
    {
        var dirs = Resolve(Layer(0, "core", "c:/mods/data", ["d:/shared/xml"]));

        Assert.Contains("d:/shared/xml", dirs);
        Assert.Contains("c:/mods/data", dirs);
    }

    /// <summary>
    ///     The project FILE has to be watched too, not only the content it points at. Editing a
    ///     dependency's <c>.pgproj</c> is what changes the layer set, and it sits in the project
    ///     directory rather than under any declared directory.
    /// </summary>
    [Fact]
    public void ItReportsEachLayersProjectDirectory()
    {
        var dirs = Resolve(Layer(0, "core", "c:/mods/data", ["c:/mods/data/xml"]));

        Assert.Contains("c:/mods/data", dirs);
    }

    /// <summary>
    ///     A watcher per directory costs an OS handle, and a nested directory is already covered by
    ///     its parent's recursive glob.
    /// </summary>
    [Fact]
    public void ItDropsADirectoryAlreadyCoveredByAnother()
    {
        var dirs = Resolve(Layer(0, "core", "c:/mods/data",
            ["c:/mods/data/xml", "c:/mods/data/xml/ai"], ["c:/mods/data/scripts"]));

        Assert.DoesNotContain("c:/mods/data/xml/ai", dirs);
        Assert.Contains("c:/mods/data", dirs);
        // The project directory already covers everything beneath it.
        Assert.DoesNotContain("c:/mods/data/xml", dirs);
    }

    [Fact]
    public void ItReportsEachDirectoryOnce()
    {
        var dirs = Resolve(
            Layer(1, "leaf", "c:/mods/rev", ["c:/shared/xml"]),
            Layer(0, "core", "c:/mods/data", ["c:/shared/xml"]));

        Assert.Single(dirs, d => string.Equals(d, "c:/shared/xml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A workspace with no project file resolves to no layers and asks for nothing.</summary>
    [Fact]
    public void WithNoLayers_ItReportsNothing()
    {
        Assert.Empty(Resolve());
    }
}