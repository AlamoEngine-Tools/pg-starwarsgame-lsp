// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>
///     A hardpoint's <c>Tooltip_Text</c>, resolved to the words a player reads.
/// </summary>
/// <remarks>
///     <para>
///         The tag is a localisation KEY - <c>TEXT_COMM_ARRAY_HARDPOINT</c> - and 422 shipped
///         objects write one. Sent raw, the card showed the key, which is the one thing on it a
///         player would never see and a modder can read out of their own file anyway.
///     </para>
///     <para>
///         BOTH go on the wire. The key is what the author wrote and what they would search for;
///         the text is what the game shows. The same split the ability rows already make between
///         <c>GuiName</c> and <c>Name</c>.
///     </para>
/// </remarks>
public sealed class PreviewHardpointTextTest
{
    private sealed class FakeLocalisation(params (string Key, string Value)[] rows) : ILocalisationIndex
    {
        private readonly Dictionary<string, string> _rows =
            rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Keys => _rows.Keys;

        public bool ContainsKey(string key) => _rows.ContainsKey(key);

        public string? GetValue(string key) => _rows.GetValueOrDefault(key);
    }

    [Fact]
    public void Hardpoint_KeepsTheKeyTheAuthorWrote()
    {
        var hardpoint = Build("TEXT_COMM_ARRAY_HARDPOINT",
            new FakeLocalisation(("TEXT_COMM_ARRAY_HARDPOINT", "Communications Array")));

        Assert.Equal("TEXT_COMM_ARRAY_HARDPOINT", hardpoint.TooltipKey);
    }

    [Fact]
    public void Hardpoint_ResolvesTheKeyToWhatAPlayerReads()
    {
        var hardpoint = Build("TEXT_COMM_ARRAY_HARDPOINT",
            new FakeLocalisation(("TEXT_COMM_ARRAY_HARDPOINT", "Communications Array")));

        Assert.Equal("Communications Array", hardpoint.TooltipText);
    }

    [Fact]
    public void Hardpoint_MatchesTheKeyWithoutRegardToCase()
    {
        // The shipped files are not consistent about it, and the localisation index is
        // case-insensitive, so the preview must not be stricter than the game.
        var hardpoint = Build("text_comm_array_hardpoint",
            new FakeLocalisation(("TEXT_COMM_ARRAY_HARDPOINT", "Communications Array")));

        Assert.Equal("Communications Array", hardpoint.TooltipText);
    }

    [Fact]
    public void Hardpoint_LeavesTheTextEmptyWhenNothingResolves()
    {
        // A key with no row is a real authoring mistake, but it is the LOCALISATION editor's to
        // report - inventing text here, or echoing the key as though it were text, would hide it.
        var hardpoint = Build("TEXT_MISSING", new FakeLocalisation());

        Assert.Equal("TEXT_MISSING", hardpoint.TooltipKey);
        Assert.Null(hardpoint.TooltipText);
    }

    [Fact]
    public void Hardpoint_DeclaringNoTooltip_CarriesNeither()
    {
        var hardpoint = Build(null, new FakeLocalisation(("TEXT_ANY", "Anything")));

        Assert.Null(hardpoint.TooltipKey);
        Assert.Null(hardpoint.TooltipText);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static PreviewHardpoint Build(string? tooltipKey, ILocalisationIndex localisation)
    {
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("Frigate", [Sym("Frigate", "SpaceUnit")])
                .Add("HP_Comm", [Sym("HP_Comm", "HardPoint")]),
            Localisation = localisation
        };

        var hardpointTags = new List<VariantTag>
        {
            Tag("Type", "HARD_POINT_ENGINE"),
            Tag("Attachment_Bone", "HP_E_BONE")
        };

        if (tooltipKey is not null)
            hardpointTags.Add(Tag("Tooltip_Text", tooltipKey));

        var tags = new FakeVariantTagSource()
            .With("Frigate", Tag("Space_Model_Name", "frigate.alo"), Tag("HardPoints", "HP_Comm"))
            .With("HP_Comm", [.. hardpointTags]);

        var scene = new PreviewSceneBuilder(new FakeGameIndexService(index), new NullSchemaProvider(),
            tags, new FakeAssets("frigate.alo")).BuildForObject("Frigate");

        return Assert.Single(scene.Hardpoints);
    }

    private static GameSymbol Sym(string name, string type)
    {
        return new GameSymbol(name, GameSymbolKind.XmlObject, type,
            new FileOrigin("file:///units.xml", 0, 0), null, null);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }
}
