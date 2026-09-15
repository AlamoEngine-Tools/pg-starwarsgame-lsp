// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

/// <summary>
///     Engine repairs that edit a tag OTHER than the one the diagnostic sits on.
/// </summary>
/// <remarks>
///     <para>
///         The value-anchored fixes replace the diagnostic's own range, which the existing provider
///         already does. These do not: the automatic-despawn rule reports against the ability
///         element and has to turn off a child flag, and the respawn rule has to exchange two
///         values. The edits are computed by the RULE, which holds the parsed nodes and the offset
///         index, rather than by re-parsing the document in the code-action layer.
///     </para>
///     <para>
///         Both repairs are measured: <c>SpecialAbilityClass::Validate_Data</c> assigns
///         <c>CausesDespawn = false</c>, and the same function calls
///         <c>std::swap(MinRespawnTime, MaxRespawnTime)</c>.
///     </para>
/// </remarks>
public sealed class EngineRepairEditTest
{
    private const string Uri = "file:///abilities/Abilities.xml";
    private static readonly DocumentUri TestUri = DocumentUri.From("file:///test.xml");

    [Fact]
    public void The_automatic_despawn_repair_turns_the_flag_off()
    {
        const string body = "<Spawn_Ability Name='A'>\n"
                            + "  <Activation_Style>Ground_Automatic</Activation_Style>\n"
                            + "  <Causes_Despawn>Yes</Causes_Despawn>\n"
                            + "</Spawn_Ability>";

        var fact = Assert.Single(Run(body).OfType<AutomaticDespawnFact>());
        var d = Assert.Single(new AutomaticDespawnHandler().Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.NotNull(d.EngineRepair);
        var repair = d.EngineRepair!;
        var edit = Assert.Single(repair.Edits);

        Assert.Equal("No", edit.NewText);
        // The value of Causes_Despawn, not the element and not the diagnostic's own range.
        Assert.Equal(LineOf(body, "<Causes_Despawn>"), edit.Line);
        Assert.Equal("Yes".Length, edit.Length);
        Assert.Contains("engine", repair.Title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The engine exchanges the two values rather than clamping either.</summary>
    [Fact]
    public void The_respawn_repair_swaps_the_two_values()
    {
        const string body = "<Spawn_Ability Name='A'>\n"
                            + "  <Min_Respawn_Time>30.0</Min_Respawn_Time>\n"
                            + "  <Max_Respawn_Time>10.0</Max_Respawn_Time>\n"
                            + "</Spawn_Ability>";

        var fact = Assert.Single(RunRespawn(body).OfType<TagComparisonFact>());
        var d = Assert.Single(new TagComparisonHandler().Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.NotNull(d.EngineRepair);
        var repair = d.EngineRepair!;
        Assert.Equal(2, repair.Edits.Count);

        // Each value is replaced by the other one, in place.
        Assert.Equal("10.0", repair.Edits[0].NewText);
        Assert.Equal(LineOf(body, "<Min_Respawn_Time>"), repair.Edits[0].Line);
        Assert.Equal("30.0", repair.Edits[1].NewText);
        Assert.Equal(LineOf(body, "<Max_Respawn_Time>"), repair.Edits[1].Line);
    }

    /// <summary>
    ///     A comparison the engine does not repair must not acquire a fix by inheritance - only the
    ///     respawn rule swaps.
    /// </summary>
    [Fact]
    public void A_comparison_with_no_measured_repair_offers_nothing()
    {
        const string body = "<Spawn_Ability Name='A'>\n"
                            + "  <Damage_Radius>500</Damage_Radius>\n"
                            + "  <Chase_Radius>100</Chase_Radius>\n"
                            + "</Spawn_Ability>";

        var fact = Assert.Single(RunRadius(body).OfType<TagComparisonFact>());
        var d = Assert.Single(new TagComparisonHandler().Handle(fact, XmlHandlerTestFixtures.EmptyCtx));

        Assert.Null(d.EngineRepair);
    }

    [Fact]
    public void The_provider_turns_the_edits_into_one_workspace_edit()
    {
        var d = new Diagnostic
        {
            Range = new LspRange(new Position(0, 0), new Position(0, 4)),
            Data = JToken.FromObject(new
            {
                engineRepair = new
                {
                    title = "Apply the engine's own correction: swap the two times",
                    edits = new[]
                    {
                        new { line = 1, column = 20, length = 4, newText = "10.0" },
                        new { line = 2, column = 20, length = 4, newText = "30.0" }
                    }
                }
            })
        };

        var action = Assert.Single(new EngineRepairCodeActionProvider()
            .Handle(new XmlCodeActionContext(TestUri, d))).CodeAction!;

        Assert.Equal("Apply the engine's own correction: swap the two times", action.Title);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);

        var edits = action.Edit!.Changes![TestUri].ToList();
        Assert.Equal(2, edits.Count);
        Assert.Equal("10.0", edits[0].NewText);
        Assert.Equal(new Position(1, 20), edits[0].Range.Start);
        Assert.Equal(new Position(1, 24), edits[0].Range.End);
    }

    [Fact]
    public void A_diagnostic_without_a_repair_yields_nothing()
    {
        var d = new Diagnostic { Range = new LspRange(new Position(0, 0), new Position(0, 4)) };

        Assert.Empty(new EngineRepairCodeActionProvider().Handle(new XmlCodeActionContext(TestUri, d)));
    }

    /// <summary>
    ///     The 0-based line holding <paramref name="needle" /> in the document as
    ///     <see cref="Produce" /> assembles it - so the expectation moves with the fixture instead
    ///     of being a hand-counted constant.
    /// </summary>
    private static int LineOf(string body, string needle)
    {
        var document = ("<Root>\n" + body + "\n</Root>").Split('\n');
        return Array.FindIndex(document, l => l.Contains(needle, StringComparison.Ordinal));
    }

    private static IReadOnlyList<XmlFact> Run(string body)
    {
        return Produce(body, new AutomaticAbilityDespawnRule());
    }

    private static IReadOnlyList<XmlFact> RunRespawn(string body)
    {
        return Produce(body, new RespawnTimeOrderRule());
    }

    private static IReadOnlyList<XmlFact> RunRadius(string body)
    {
        return Produce(body, new DamageRadiusWithinChaseRadiusRule());
    }

    private static IReadOnlyList<XmlFact> Produce(string body, IXmlCrossTagRule rule)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [rule]);

        return producer.Produce($"<Root>\n{body}\n</Root>", Uri);
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}