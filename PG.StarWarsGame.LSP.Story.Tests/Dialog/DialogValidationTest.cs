// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Story.Dialog;
using PG.StarWarsGame.LSP.Story.Dialog.Handlers;

namespace PG.StarWarsGame.LSP.Story.Tests.Dialog;

public sealed class DialogValidationTest
{
    private const string Uri = "file:///dialog_test.txt";

    // ── Fixture: a hand-built StoryDialogCommand schema slice ────────────────

    private static EnumValueDefinition Command(string name, bool untested = false,
        params ParamDefinition[] parameters)
    {
        return new EnumValueDefinition
        {
            Name = name,
            Untested = untested,
            Params = parameters.Length > 0 ? parameters : null
        };
    }

    private static ParamDefinition Param(int position, XmlValueType type,
        ReferenceKind referenceKind = ReferenceKind.None, string? objectType = null)
    {
        return new ParamDefinition
        {
            Position = position,
            ValueType = type,
            ReferenceKind = referenceKind,
            ObjectType = objectType is null ? null : new GameObjectTypeDefinition { TypeName = objectType }
        };
    }

    private static readonly ISchemaProvider Schema = new StubSchemaProvider(new EnumDefinition
    {
        Name = "StoryDialogCommand",
        Values =
        [
            Command("TEXT", parameters: Param(0, XmlValueType.NameReference, ReferenceKind.LocalisationKey)),
            Command("TEXTCOLOR", parameters:
            [
                Param(0, XmlValueType.UInt), Param(1, XmlValueType.UInt),
                Param(2, XmlValueType.UInt), Param(3, XmlValueType.UInt)
            ]),
            Command("DIALOG", parameters: Param(0, XmlValueType.NameReference, ReferenceKind.XmlObject, "SpeechEvent")),
            Command("WAIT", parameters: Param(0, XmlValueType.UInt)),
            Command("PAUSE", parameters: Param(0, XmlValueType.Boolean)),
            Command("WAIT_SPEECH"),
            Command("CLEAR_TEXT", untested: true)
        ]
    });

    private static IReadOnlyList<DialogDiagnostic> Validate(string text, GameIndex? index = null)
    {
        var doc = StoryDialogParser.Parse("[CHAPTER 0]\n" + text);
        var facts = new DialogFactProducer(Schema).Produce(doc, Uri);
        var registry = new DialogDiagnosticsHandlerRegistry(
        [
            new UnknownDialogCommandHandler(),
            new DialogCommandArityHandler(),
            new DialogArgValueHandler(),
            new UntestedDialogCommandHandler(),
            new DialogArgReferenceHandler()
        ]);
        return facts.SelectMany(f => registry.Dispatch(f, index ?? GameIndex.Empty)).ToList();
    }

    private static GameIndex IndexWith(string[] localisationKeys, params GameSymbol[] symbols)
    {
        var defs = symbols
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = defs,
            Localisation = new StubLocalisationIndex(localisationKeys)
        };
    }

    private static GameSymbol Symbol(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin("file:///x.xml", 0, null), null);
    }

    // ── Unknown command ──────────────────────────────────────────────────────

    [Fact]
    public void UnknownCommand_ProducesError()
    {
        // Real vanilla typo: "TEXTCOLOR255 255 255 176" in Dialog_tutorial_07.txt.
        var diags = Validate("TEXTCOLOR255 255 255 176");

        var diag = Assert.Single(diags);
        Assert.Equal(XmlDiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("TEXTCOLOR255", diag.Message);
        Assert.Equal(1, diag.Line);
        Assert.Equal(0, diag.Column);
        Assert.Equal("TEXTCOLOR255".Length, diag.EndColumn);
    }

    [Fact]
    public void KnownCommand_WithValidArgs_ProducesNothing()
    {
        Assert.Empty(Validate("TEXTCOLOR 255 255 255 176\nWAIT SPEECH"));
    }

    // ── Arity ────────────────────────────────────────────────────────────────

    [Fact]
    public void TooFewArguments_ProducesError()
    {
        var diags = Validate("TEXTCOLOR 255 255 176");

        var diag = Assert.Single(diags);
        Assert.Equal(XmlDiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("4 argument", diag.Message);
        Assert.Contains("got 3", diag.Message);
    }

    [Fact]
    public void TooManyArguments_ProducesErrorOnExtraTokens()
    {
        var diags = Validate("WAIT 500 extra");

        var diag = Assert.Single(diags);
        Assert.Equal(XmlDiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("got 2", diag.Message);
        Assert.Equal("WAIT 500 ".Length, diag.Column);
        Assert.Equal("WAIT 500 extra".Length, diag.EndColumn);
    }

    [Fact]
    public void ZeroArgCommand_WithArgument_ProducesError()
    {
        var diags = Validate("WAIT_SPEECH now");

        var diag = Assert.Single(diags);
        Assert.Contains("no arguments", diag.Message);
    }

    // ── Argument types ───────────────────────────────────────────────────────

    [Fact]
    public void NonNumericUIntArgument_ProducesErrorAtArgRange()
    {
        var diags = Validate("WAIT soon");

        var diag = Assert.Single(diags);
        Assert.Equal(XmlDiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("'soon'", diag.Message);
        Assert.Equal("WAIT ".Length, diag.Column);
        Assert.Equal("WAIT soon".Length, diag.EndColumn);
    }

    [Fact]
    public void NegativeUIntArgument_ProducesError()
    {
        Assert.Single(Validate("WAIT -5"));
    }

    [Theory]
    [InlineData("PAUSE 1")]
    [InlineData("PAUSE 0")]
    public void PauseWithZeroOrOne_ProducesNothing(string line)
    {
        Assert.Empty(Validate(line));
    }

    [Fact]
    public void PauseWithOtherValue_ProducesError()
    {
        var diag = Assert.Single(Validate("PAUSE yes"));
        Assert.Contains("1", diag.Message);
        Assert.Contains("0", diag.Message);
    }

    // ── Untested commands ────────────────────────────────────────────────────

    [Fact]
    public void UntestedCommand_ProducesWarning()
    {
        var diag = Assert.Single(Validate("CLEAR_TEXT"));
        Assert.Equal(XmlDiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("untested", diag.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── References ───────────────────────────────────────────────────────────

    [Fact]
    public void LocalisationKeyArgument_Missing_ProducesError()
    {
        var diags = Validate("TEXT TEXT_MISSING", IndexWith(["TEXT_PRESENT"]));

        var diag = Assert.Single(diags);
        Assert.Equal(XmlDiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("TEXT_MISSING", diag.Message);
        Assert.Contains("localisation key", diag.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalisationKeyArgument_Present_ProducesNothing()
    {
        Assert.Empty(Validate("TEXT TEXT_PRESENT", IndexWith(["TEXT_PRESENT"])));
    }

    [Fact]
    public void ObjectReferenceArgument_Unresolved_ProducesErrorNamingTheType()
    {
        var diags = Validate("DIALOG TAR_MISSING", IndexWith([]));

        var diag = Assert.Single(diags);
        Assert.Equal(XmlDiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("TAR_MISSING", diag.Message);
        Assert.Contains("SpeechEvent", diag.Message);
    }

    [Fact]
    public void ObjectReferenceArgument_Resolved_ProducesNothing()
    {
        var index = IndexWith([], Symbol("TAR_EVENT_00", "SpeechEvent"));

        Assert.Empty(Validate("DIALOG TAR_EVENT_00", index));
    }
}

file sealed class StubSchemaProvider(EnumDefinition dialogCommands) : ISchemaProvider
{
    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [dialogCommands];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public XmlTagDefinition? GetTag(string t)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
    {
        return [];
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string name)
    {
        return string.Equals(name, dialogCommands.Name, StringComparison.OrdinalIgnoreCase)
            ? dialogCommands
            : null;
    }

    public GameObjectTypeDefinition? GetObjectType(string t)
    {
        return null;
    }
}

file sealed class StubLocalisationIndex(IEnumerable<string> keys) : ILocalisationIndex
{
    private readonly HashSet<string> _keys = new(keys, StringComparer.OrdinalIgnoreCase);

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
