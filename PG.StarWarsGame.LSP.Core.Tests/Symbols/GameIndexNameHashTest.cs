// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Symbols;

/// <summary>
///     The index keeps game objects by the hash the engine files them under, updated by the same
///     per-document deltas as the definitions - so a name is hashed when it enters the index, and an
///     edit touches only its own document's names.
/// </summary>
public sealed class GameIndexNameHashTest
{
    /// <summary>Hashes by a table, so a test can make any two names collide, and counts its calls.</summary>
    private sealed class CountingHash(params (string Name, uint Hash)[] table) : IObjectNameHash
    {
        public int Calls { get; private set; }

        public uint Of(string name)
        {
            Calls++;
            foreach (var (n, h) in table)
                if (n.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return h;
            return (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(name) | 0x8000_0000u;
        }
    }

    private static GameSymbol Object(string id, string uri, string type = "GameObjectType")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, type, new FileOrigin(uri, 1, null), null);
    }

    private static DocumentIndex Doc(string uri, int version, params GameSymbol[] symbols)
    {
        return new DocumentIndex(uri, version, [.. symbols], []);
    }

    private static GameIndexService Build(IObjectNameHash hash)
    {
        return new GameIndexService(new FileHelper(new MockFileSystem()), [], NullLogger<GameIndexService>.Instance,
            nameHash: hash);
    }

    [Fact]
    public void TwoObjectsSharingAHash_SeeEachOther()
    {
        var hash = new CountingHash(("Alpha", 7), ("Bravo", 7));
        var svc = Build(hash);
        var alpha = Object("Alpha", "file:///a.xml");
        svc.InjectDocument(Doc("file:///a.xml", 1, alpha));
        svc.InjectDocument(Doc("file:///b.xml", 1, Object("Bravo", "file:///b.xml")));

        Assert.Equal(["Bravo"], svc.Current.SharingNameHash(alpha, 7).Select(s => s.Id));
    }

    [Fact]
    public void AnEditThatRemovesOneSide_ClearsTheOther()
    {
        var svc = Build(new CountingHash(("Alpha", 7), ("Bravo", 7)));
        var alpha = Object("Alpha", "file:///a.xml");
        svc.InjectDocument(Doc("file:///a.xml", 1, alpha));
        svc.InjectDocument(Doc("file:///b.xml", 1, Object("Bravo", "file:///b.xml")));

        svc.InjectDocument(Doc("file:///b.xml", 2, Object("Charlie", "file:///b.xml")));

        Assert.Empty(svc.Current.SharingNameHash(alpha, 7));
    }

    [Fact]
    public void ARemovedDocument_LeavesTheTable()
    {
        var svc = Build(new CountingHash(("Alpha", 7), ("Bravo", 7)));
        var alpha = Object("Alpha", "file:///a.xml");
        svc.InjectDocument(Doc("file:///a.xml", 1, alpha));
        svc.InjectDocument(Doc("file:///b.xml", 1, Object("Bravo", "file:///b.xml")));

        svc.RemoveDocument("file:///b.xml");

        Assert.Empty(svc.Current.SharingNameHash(alpha, 7));
    }

    [Fact]
    public void TheBulkPath_KeepsTheTableToo()
    {
        var svc = Build(new CountingHash(("Alpha", 7), ("Bravo", 7)));
        var alpha = Object("Alpha", "file:///a.xml");
        using (svc.BeginBulkUpdate())
        {
            svc.InjectDocument(Doc("file:///a.xml", 1, alpha));
            svc.InjectDocument(Doc("file:///b.xml", 1, Object("Bravo", "file:///b.xml")));
        }

        Assert.Equal(["Bravo"], svc.Current.SharingNameHash(alpha, 7).Select(s => s.Id));
    }

    [Fact]
    public void AModObject_CollidingWithTheGame_IsFound_AndTheGameIsHashedOnce()
    {
        var hash = new CountingHash(("Alpha", 9), ("Darth_Vader", 9));
        var svc = Build(new CachedObjectNameHash(hash));
        var game = Object("Darth_Vader", "DATA\\XML\\HEROES.XML");
        svc.ApplyBaseline(BaselineIndex.Empty with
        {
            Symbols = ImmutableDictionary.Create<string, GameSymbol>(StringComparer.OrdinalIgnoreCase)
                .Add(game.Id, game)
        });
        var afterBaseline = hash.Calls;

        var alpha = Object("Alpha", "file:///a.xml");
        svc.InjectDocument(Doc("file:///a.xml", 1, alpha));
        svc.InjectDocument(Doc("file:///a.xml", 2, alpha, Object("Echo", "file:///a.xml")));

        Assert.Equal(["Darth_Vader"], svc.Current.SharingNameHash(alpha, 9).Select(s => s.Id));
        // Each name is hashed once: Alpha when it arrived, Echo when it arrived - not the baseline
        // again, and not Alpha again when its document was re-applied.
        Assert.Equal(afterBaseline + 2, hash.Calls);
    }

    [Fact]
    public void OnlyGameObjects_AreInTheTable_AndOneNameIsNoCollision()
    {
        // Factions keep a table of their own in the engine; the same name in another case or layer
        // is one name to the engine too.
        var svc = Build(new CountingHash(("Alpha", 7), ("Bravo", 7)));
        var alpha = Object("Alpha", "file:///a.xml");
        svc.InjectDocument(Doc("file:///a.xml", 1, alpha));
        svc.InjectDocument(Doc("file:///b.xml", 1, Object("Bravo", "file:///b.xml", "Faction"),
            Object("ALPHA", "file:///b.xml")));

        Assert.Empty(svc.Current.SharingNameHash(alpha, 7));
    }

    [Fact]
    public void WithoutAHash_TheTableStaysEmpty()
    {
        // Minimal setups that pass none - the check then finds nothing rather than failing.
        var svc = new GameIndexService(new FileHelper(new MockFileSystem()), [], NullLogger<GameIndexService>.Instance);
        var alpha = Object("Alpha", "file:///a.xml");
        svc.InjectDocument(Doc("file:///a.xml", 1, alpha));

        Assert.True(svc.Current.WorkspaceNameHashes.IsEmpty);
    }
}