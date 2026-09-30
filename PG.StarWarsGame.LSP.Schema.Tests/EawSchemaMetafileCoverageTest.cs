// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     Every game file the engine opens by a FIXED name has to be declared, because nothing else
///     ever names it.
///     <para>
///         A file reached through a registry is registered by that registry. A file the engine opens
///         by a name compiled into it is registered by nothing at all, so if this list is short the
///         server cannot tell such a file from one the author forgot to register - and would have to
///         either stay silent about both or accuse the shipped game of a mistake.
///     </para>
///     <para>
///         The list below was read out of the shipped game binary, which carries each of these as a
///         literal path. It is measured, not inferred, and it is the whole set: 35 files.
///     </para>
/// </summary>
public sealed class EawSchemaMetafileCoverageTest
{
    /// <summary>
    ///     Every fixed path the game binary carries, lower-cased, without its directory.
    /// </summary>
    private static readonly string[] FixedNameFiles =
    [
        "aiterraineffectiveness.xml",
        "audio.xml",
        "campaignfiles.xml",
        "commandbarcomponentfiles.xml",
        "difficultyadjustments.xml",
        "dynamictrackfx.xml",
        "factionfiles.xml",
        "gameconstants.xml",
        "gameobjectfiles.xml",
        "graphicdetails.xml",
        "guidialogs.xml",
        "hardpointdatafiles.xml",
        "heroclash.xml",
        "lensflares.xml",
        "lightningeffecttypes.xml",
        "lightsources.xml",
        "mousepointerfiles.xml",
        "movementclasstypedefs.xml",
        "movies.xml",
        "musicevents.xml",
        "radarmap.xml",
        "sfxeventfiles.xml",
        "shadowblobmaterials.xml",
        "speechevents.xml",
        "starwars3dtextcrawl.xml",
        "surfacefx.xml",
        "tacticalcameras.xml",
        "targetingprioritysetfiles.xml",
        "terraindecalfx.xml",
        "traderoutefiles.xml",
        "traderoutelines.xml",
        "unitabilitytypes.xml",
        "weatheraudio.xml",
        "weathermodifiers.xml",
        "weatherscenarios.xml"
    ];

    /// <summary>
    ///     The directory paths the game binary carries alongside the file paths. Files under these
    ///     are loaded for sitting there, so nothing names them and nothing is missing when nothing
    ///     does.
    /// </summary>
    private static readonly string[] ScannedDirectories =
    [
        "data/xml/ai/galacticmarkup/",
        "data/xml/ai/goalfunctions/",
        "data/xml/ai/goals/",
        "data/xml/ai/hintsets/",
        "data/xml/ai/perceptualequations/",
        "data/xml/ai/players/",
        "data/xml/ai/templates/",
        "data/xml/enum/"
    ];

    private static IReadOnlyList<MetafileDefinition> Shipped()
    {
        return YamlSchemaParser.ParseMetafileFile(EawSchemaRepo.Read("meta/metafiles.yaml"));
    }

    private static IReadOnlyList<ScannedDirectoryDefinition> ShippedDirectories()
    {
        return YamlSchemaParser.ParseScannedDirectories(EawSchemaRepo.Read("meta/metafiles.yaml"));
    }

    private static string FileName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    [Fact]
    public void EveryFixedNameFileTheEngineOpensIsDeclared()
    {
        var declared = Shipped().Select(m => FileName(m.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = FixedNameFiles.Where(f => !declared.Contains(f)).ToList();

        Assert.True(missing.Count == 0,
            "The game opens these by a name compiled into it, and nothing registers them:\n  " +
            string.Join("\n  ", missing));
    }

    /// <summary>
    ///     The other direction: a declared path the binary does not carry is either a registry entry
    ///     that belongs somewhere else or a typo, and both are worth catching.
    /// </summary>
    [Fact]
    public void NothingIsDeclaredThatTheEngineDoesNotOpenByName()
    {
        var expected = FixedNameFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexpected = Shipped()
            .Select(m => FileName(m.Path))
            .Where(f => !expected.Contains(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(unexpected.Count == 0,
            "Declared, but the game does not open it by a fixed name:\n  " + string.Join("\n  ", unexpected));
    }

    /// <summary>
    ///     Every declaration names a path under the XML directory. A bare file name would silently
    ///     register nothing, because the registry keys on the resolved path.
    /// </summary>
    [Fact]
    public void EveryDeclaredPathIsUnderTheXmlDirectory()
    {
        var wrong = Shipped()
            .Where(m => !m.Path.StartsWith("data/xml/", StringComparison.Ordinal))
            .Select(m => m.Path)
            .ToList();

        Assert.True(wrong.Count == 0, "Not under data/xml/:\n  " + string.Join("\n  ", wrong));
    }

    [Fact]
    public void EveryDirectoryTheEngineWalksIsDeclared()
    {
        var declared = ShippedDirectories().Select(d => d.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = ScannedDirectories.Where(d => !declared.Contains(d)).ToList();

        Assert.True(missing.Count == 0,
            "The game walks these and takes what it finds, so nothing registers their files:\n  " +
            string.Join("\n  ", missing));
    }

    [Fact]
    public void NoDirectoryIsDeclaredThatTheEngineDoesNotWalk()
    {
        var expected = ScannedDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unexpected = ShippedDirectories().Select(d => d.Path).Where(d => !expected.Contains(d)).ToList();

        Assert.True(unexpected.Count == 0,
            "Declared as scanned, but the game does not walk it:\n  " + string.Join("\n  ", unexpected));
    }

    /// <summary>
    ///     A directory and a file are told apart downstream by the trailing slash, so a declaration
    ///     that has lost it would be read as a file that no one can open.
    /// </summary>
    [Fact]
    public void EveryScannedDirectoryEndsInASlash()
    {
        Assert.All(ShippedDirectories(), d => Assert.EndsWith("/", d.Path, StringComparison.Ordinal));
    }
}