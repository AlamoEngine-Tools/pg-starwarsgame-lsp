// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Assets.ShipNames;

namespace PG.StarWarsGame.LSP.Assets.Tests.ShipNames;

/// <summary>
///     The custom ship-name pools: which objects have one, and what is in it.
/// </summary>
public sealed class ShipNameCatalogTest
{
    /// <summary>Writes a name list exactly as the game ships them: UTF-16 LE, BOM, CRLF.</summary>
    private static byte[] NameFile(params string[] names)
    {
        return Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", names)))
            .ToArray();
    }

    // ── the pair list ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsObjectIdAndPathPairs()
    {
        var pairs = ShipNameTextFiles.ParsePairs(
            "Calamari_Cruiser, Data\\Calamari_Cruiser.txt, Star_Destroyer, Data\\Star_Destroyer.txt");

        Assert.Equal(2, pairs.Count);
        Assert.Equal("Data\\Calamari_Cruiser.txt", pairs["Calamari_Cruiser"]);
        Assert.Equal("Data\\Star_Destroyer.txt", pairs["Star_Destroyer"]);
    }

    [Fact]
    public void Parse_IsWhitespaceAndNewlineTolerant()
    {
        // The shipped tag is laid out over many lines with tab alignment.
        var pairs = ShipNameTextFiles.ParsePairs(
            "\n\t\tStar_Destroyer, \t\t\tData\\Star_Destroyer.txt,\n\t\tTartan_Patrol_Cruiser,   Data\\Tartan.txt\n\t");

        Assert.Equal(2, pairs.Count);
        Assert.Equal("Data\\Star_Destroyer.txt", pairs["Star_Destroyer"]);
        Assert.Equal("Data\\Tartan.txt", pairs["Tartan_Patrol_Cruiser"]);
    }

    [Fact]
    public void Parse_ManyObjectsMayShareOneFile()
    {
        // Star_Destroyer, Generic_Star_Destroyer and Star_Destroyer_Tractor_Fighters all point at
        // the same list in the shipped data - the mapping is many-to-one, not a pairing.
        var pairs = ShipNameTextFiles.ParsePairs(
            "Star_Destroyer, Data\\SD.txt, Generic_Star_Destroyer, Data\\SD.txt");

        Assert.Equal("Data\\SD.txt", pairs["Star_Destroyer"]);
        Assert.Equal("Data\\SD.txt", pairs["Generic_Star_Destroyer"]);
    }

    [Fact]
    public void Parse_TrailingUnpairedTokenIsDropped()
    {
        // An id with no path is incomplete rather than a pool of its own; the diagnostic layer
        // reports it, and the catalog must not invent a filename for it.
        var pairs = ShipNameTextFiles.ParsePairs("Star_Destroyer, Data\\SD.txt, Orphan_Id");

        Assert.Single(pairs);
        Assert.False(pairs.ContainsKey("Orphan_Id"));
    }

    [Fact]
    public void Parse_ObjectIdLookupIsCaseInsensitive()
    {
        var pairs = ShipNameTextFiles.ParsePairs("Star_Destroyer, Data\\SD.txt");
        Assert.True(pairs.ContainsKey("STAR_DESTROYER"));
    }

    // ── the name files ───────────────────────────────────────────────────────

    [Fact]
    public void ReadNames_DecodesUtf16LeWithBom()
    {
        // The shipped files are UTF-16; read as UTF-8 they produce mojibake that looks like a data
        // bug. The curly apostrophe in "Death's Head" is a real character, not corruption.
        var names = ShipNameTextFiles.ReadNames(NameFile("Allecto", "Death’s Head", "Devastator"));

        Assert.Equal(["Allecto", "Death’s Head", "Devastator"], names);
    }

    [Fact]
    public void ReadNames_SkipsBlankLines()
    {
        var names = ShipNameTextFiles.ReadNames(NameFile("Allecto", "", "   ", "Devastator"));
        Assert.Equal(["Allecto", "Devastator"], names);
    }

    [Fact]
    public void ReadNames_EmptyFileYieldsNoNames()
    {
        Assert.Empty(ShipNameTextFiles.ReadNames(NameFile()));
    }

    [Fact]
    public void ReadNames_GarbageIsNotMistakenForNames()
    {
        // A file saved as UTF-8 by a well-meaning editor must come back empty rather than as one
        // long mojibake "name" the preview would then display.
        Assert.Empty(ShipNameTextFiles.ReadNames([0x00, 0x01, 0x02]));
    }

    // ── the catalog ──────────────────────────────────────────────────────────

    private static ShipNameCatalog Build(string pairList, Dictionary<string, byte[]> files)
    {
        return ShipNameCatalog.Build(
            pairList, path => files.TryGetValue(path, out var bytes) ? bytes : null);
    }

    [Fact]
    public void Build_ResolvesNamesPerObject()
    {
        var catalog = Build("Star_Destroyer, Data\\SD.txt",
            new Dictionary<string, byte[]> { ["Data\\SD.txt"] = NameFile("Allecto", "Devastator") });

        var pool = catalog.For("Star_Destroyer");
        Assert.NotNull(pool);
        Assert.Equal("Data\\SD.txt", pool.SourcePath);
        Assert.Equal(["Allecto", "Devastator"], pool.Names);
    }

    [Fact]
    public void Build_MissingFileStillRecordsThePoolSoTheWiringIsVisible()
    {
        // The object IS registered for custom names; the file just is not there. Dropping it would
        // make a broken mapping indistinguishable from no mapping at all.
        var catalog = Build("Star_Destroyer, Data\\Missing.txt", []);

        var pool = catalog.For("Star_Destroyer");
        Assert.NotNull(pool);
        Assert.Empty(pool.Names);
        Assert.False(pool.FileFound);
    }

    [Fact]
    public void For_UnregisteredObjectHasNoPool()
    {
        var catalog = Build("Star_Destroyer, Data\\SD.txt",
            new Dictionary<string, byte[]> { ["Data\\SD.txt"] = NameFile("Allecto") });

        Assert.Null(catalog.For("Rebel_Soldier"));
    }

    [Fact]
    public void Build_ReadsEachSharedFileOnce()
    {
        var reads = 0;
        var catalog = ShipNameCatalog.Build(
            "Star_Destroyer, Data\\SD.txt, Generic_Star_Destroyer, Data\\SD.txt",
            _ =>
            {
                reads++;
                return NameFile("Allecto");
            });

        Assert.Equal(1, reads);
        Assert.Equal(["Allecto"], catalog.For("Generic_Star_Destroyer")!.Names);
    }
}
