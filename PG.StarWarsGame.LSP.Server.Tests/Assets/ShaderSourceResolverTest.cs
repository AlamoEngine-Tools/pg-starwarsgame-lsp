// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;

namespace PG.StarWarsGame.LSP.Server.Tests.Assets;

/// <summary>
///     Finding a shader's source text.
/// </summary>
/// <remarks>
///     <para>
///         Three tiers, in order: the mod's own <c>.fx</c> files, then the managed copy of the base
///         game's, then nothing - at which point the renderer falls back to its archetype materials.
///         A mod shipping its own shader must win, or previewing the mod would show the stock look.
///     </para>
///     <para>
///         The base shaders are never redistributed with this extension. They are Petroglyph's, and
///         published by Petroglyph; the managed directory is wherever the user's copy was put.
///     </para>
/// </remarks>
public sealed class ShaderSourceResolverTest
{
    private const string Managed = @"C:\managed";

    private static ShaderSourceResolver Build(
        MockFileSystem fs, string? managed = null, IReadOnlyList<string>? assetRoots = null)
    {
        var config = new StubConfiguration(new LspConfiguration { ShaderPath = managed });

        var projects = new StubProjects(assetRoots is null
            ? null
            : WorkspaceConfiguration.Empty with { AssetRoots = assetRoots });

        var assets = new GameAssetResolver(new FileHelper(fs), config, projects,
            EmptyArchives.Instance, NullLogger<GameAssetResolver>.Instance);

        return new ShaderSourceResolver(new FileHelper(fs), config, assets,
            NullLogger<ShaderSourceResolver>.Instance);
    }

    private static MockFileSystem FileSystemWith(params (string Path, string Text)[] files)
    {
        var fs = new MockFileSystem();
        foreach (var (path, text) in files)
            fs.AddFile(path, new MockFileData(text));
        return fs;
    }

    [Fact]
    public void Read_FindsAModsOwnShader()
    {
        var fs = FileSystemWith((@"C:\mod\data\art\Shaders\MeshBump.fx", "// the mod's own"));
        var resolver = Build(fs, Managed, [@"C:\mod\data\art"]);

        Assert.Equal("// the mod's own", resolver.Read("MeshBump.fx"));
    }

    [Fact]
    public void Read_FallsBackToTheManagedCopy()
    {
        var fs = FileSystemWith((Managed + @"\MeshBump.fx", "// the base game's"));

        Assert.Equal("// the base game's", Build(fs, Managed).Read("MeshBump.fx"));
    }

    [Fact]
    public void Read_PrefersTheModOverTheManagedCopy()
    {
        // A mod that replaces a stock shader must preview as the mod, not as the stock game.
        var fs = FileSystemWith(
            (@"C:\mod\data\art\Shaders\MeshBump.fx", "// the mod's own"),
            (Managed + @"\MeshBump.fx", "// the base game's"));

        Assert.Equal("// the mod's own",
            Build(fs, Managed, [@"C:\mod\data\art"]).Read("MeshBump.fx"));
    }

    [Fact]
    public void Read_LooksInsideTheShadersFolderTheArchiveUnpacksTo()
    {
        // The published archive contains a `Shaders/` directory, and people extract it as-is.
        var fs = FileSystemWith((Managed + @"\Shaders\MeshBump.fx", "// unpacked as shipped"));

        Assert.Equal("// unpacked as shipped", Build(fs, Managed).Read("MeshBump.fx"));
    }

    [Fact]
    public void Read_FindsAnIncludeHeaderToo()
    {
        // The .fx files include .fxh headers, so the translator has to be able to read those.
        var fs = FileSystemWith((Managed + @"\AlamoEngine.fxh", "// shared declarations"));

        Assert.Equal("// shared declarations", Build(fs, Managed).Read("AlamoEngine.fxh"));
    }

    [Fact]
    public void Read_ReturnsNullWhenNoTierHasIt()
    {
        // Not an error: the renderer has archetype materials precisely so a missing shader degrades
        // to a plainer look rather than a blank viewport.
        Assert.Null(Build(new MockFileSystem(), Managed).Read("Missing.fx"));
    }

    [Fact]
    public void Read_ReturnsNullWhenNoManagedDirectoryIsConfigured()
    {
        var fs = FileSystemWith((Managed + @"\MeshBump.fx", "// present but unreachable"));

        Assert.Null(Build(fs).Read("MeshBump.fx"));
    }

    [Theory]
    [InlineData(@"..\..\Windows\System32\config\SAM")]
    [InlineData("../../../secrets.txt")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("sub/dir/MeshBump.fx")]
    public void Read_RefusesAnythingButABareFileName(string name)
    {
        // The name arrives over the wire. Only a bare file name is ever legitimate, so anything with
        // a separator is refused rather than sanitised - there is no valid request this rejects.
        var fs = FileSystemWith(
            (Managed + @"\MeshBump.fx", "// fine"),
            (@"C:\Windows\win.ini", "[boot loader]"));

        Assert.Null(Build(fs, Managed).Read(name));
    }

    [Fact]
    public void Read_RefusesSomethingThatIsNotAShader()
    {
        // Only the two shader extensions, so this cannot be turned into a general file reader.
        var fs = FileSystemWith((Managed + @"\notes.txt", "secret"));

        Assert.Null(Build(fs, Managed).Read("notes.txt"));
    }

    [Fact]
    public void Tiers_ReportsWhetherTheBaseShadersAreReachable()
    {
        // So the UI can say why a preview looks plain, rather than leaving the author guessing.
        Assert.False(Build(new MockFileSystem()).HasManagedShaders);

        var fs = FileSystemWith((Managed + @"\MeshBump.fx", "// present"));
        Assert.True(Build(fs, Managed).HasManagedShaders);
    }
}
