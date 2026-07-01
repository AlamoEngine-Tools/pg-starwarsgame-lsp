// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Tests.Localisation;

public sealed class LocalisationIndexOnGameIndexTest
{
    // ── GameIndex.Empty default ──────────────────────────────────────────────

    [Fact]
    public void GameIndex_Empty_LocalisationIndex_ContainsKey_ReturnsFalse()
    {
        Assert.False(GameIndex.Empty.Localisation.ContainsKey("TEXT_ANYTHING"));
    }

    [Fact]
    public void GameIndex_Empty_LocalisationIndex_Keys_IsEmpty()
    {
        Assert.Empty(GameIndex.Empty.Localisation.Keys);
    }

    // ── GameIndex with custom ILocalisationIndex ─────────────────────────────

    [Fact]
    public void GameIndex_WithCustomLocalisation_ContainsKey_ReturnsTrue()
    {
        var loc = new StubLocalisationIndex(["TEXT_UNIT_NAME_XWING"]);
        var index = GameIndex.Empty with { Localisation = loc };

        Assert.True(index.Localisation.ContainsKey("TEXT_UNIT_NAME_XWING"));
        Assert.False(index.Localisation.ContainsKey("TEXT_NONEXISTENT"));
    }

    [Fact]
    public void GameIndex_WithCustomLocalisation_Keys_YieldsAllKeys()
    {
        var expected = new[] { "TEXT_A", "TEXT_B" };
        var loc = new StubLocalisationIndex(expected);
        var index = GameIndex.Empty with { Localisation = loc };

        Assert.Equal(expected, index.Localisation.Keys);
    }

    // ── IGameIndexService.ApplyLocalisation ──────────────────────────────────

    [Fact]
    public void ApplyLocalisation_UpdatesCurrent()
    {
        var service = BuildService();
        var loc = new StubLocalisationIndex(["TEXT_TEST"]);

        service.ApplyLocalisation(loc);

        Assert.True(service.Current.Localisation.ContainsKey("TEXT_TEST"));
    }

    [Fact]
    public void ApplyLocalisation_KeyAbsent_ReturnsFalse()
    {
        var service = BuildService();
        service.ApplyLocalisation(new StubLocalisationIndex(["TEXT_A"]));

        Assert.False(service.Current.Localisation.ContainsKey("TEXT_B"));
    }

    [Fact]
    public void ApplyLocalisation_FiresIndexChanged()
    {
        var service = BuildService();
        GameIndex? received = null;
        service.IndexChanged += idx => received = idx;

        service.ApplyLocalisation(new StubLocalisationIndex(["KEY"]));

        Assert.NotNull(received);
        Assert.True(received!.Localisation.ContainsKey("KEY"));
    }

    [Fact]
    public void ApplyLocalisation_ReplacesExistingLocalisation()
    {
        var service = BuildService();
        service.ApplyLocalisation(new StubLocalisationIndex(["OLD_KEY"]));
        service.ApplyLocalisation(new StubLocalisationIndex(["NEW_KEY"]));

        Assert.True(service.Current.Localisation.ContainsKey("NEW_KEY"));
        Assert.False(service.Current.Localisation.ContainsKey("OLD_KEY"));
    }

    // ── LocalisationChanged (scoped event, decoupled from the general IndexChanged) ──────────

    [Fact]
    public void ApplyLocalisation_FiresLocalisationChanged()
    {
        var service = BuildService();
        ILocalisationIndex? received = null;
        service.LocalisationChanged += idx => received = idx;

        var loc = new StubLocalisationIndex(["TEXT_TEST"]);
        service.ApplyLocalisation(loc);

        Assert.NotNull(received);
        Assert.True(received!.ContainsKey("TEXT_TEST"));
    }

    [Fact]
    public void ApplyBaseline_DoesNotFireLocalisationChanged()
    {
        var service = BuildService();
        var fired = false;
        service.LocalisationChanged += _ => fired = true;

        service.ApplyBaseline(BaselineIndex.Empty);

        Assert.False(fired);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IGameIndexService BuildService()
    {
        return new GameIndexService(new FileHelper(new MockFileSystem()), [],
            NullLogger<GameIndexService>.Instance);
    }
}

file sealed class StubLocalisationIndex : ILocalisationIndex
{
    private readonly HashSet<string> _keys;

    public StubLocalisationIndex(IEnumerable<string> keys)
    {
        _keys = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
    }

    public bool ContainsKey(string key)
    {
        return _keys.Contains(key);
    }

    public IEnumerable<string> Keys => _keys;

    public string? GetValue(string key)
    {
        return null;
    }
}