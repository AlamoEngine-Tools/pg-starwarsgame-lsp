// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     Notes on the enum VALUE a story parameter names, as opposed to notes on the parameter slot.
///     <para>
///         The gap this closes: until now only a hardcoded-set value could carry a diagnostic, so an
///         enum member the engine ignores had nowhere to say so. <c>NOT_EQUAL_TO</c> on
///         <c>StoryFlagCompareMethod</c> is the case that prompted it - it parses, its branch does
///         nothing, and the event silently never fires.
///     </para>
/// </summary>
public sealed class StoryParamValueNotesHandlerTest
{
    private static readonly StoryParamValueNotesHandler Sut = new();

    private static readonly DiagnosticsContext Ctx =
        new(new EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "en");

    private static StoryParamFact Param(string value, params SchemaNote[] notesOnValue)
    {
        var def = new ParamDefinition
        {
            Position = 2,
            ValueType = XmlValueType.DynamicEnumValue,
            Optional = true,
            Enum = new EnumDefinition
            {
                Name = "StoryFlagCompareMethod",
                Values =
                [
                    new EnumValueDefinition {Name = "EQUAL_TO"},
                    new EnumValueDefinition {Name = "NOT_EQUAL_TO", Notes = notesOnValue}
                ]
            }
        };
        return new StoryParamFact("file:///test.xml", 1, 0, value.Length, "STORY_FLAG", false, 2, def, value);
    }

    private static SchemaNote Note(SchemaNoteKind kind, string? text = null)
    {
        return new SchemaNote(kind,
            text is null ? new Dictionary<string, string>() : new Dictionary<string, string> {["en"] = text});
    }

    [Fact]
    public void ABuggedValue_IsAnError_NamingTheValue()
    {
        var fact = Param("NOT_EQUAL_TO", Note(SchemaNoteKind.BuggedInEngine, "Its branch does nothing."));

        var d = Assert.Single(Sut.Handle(fact, Ctx));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("NOT_EQUAL_TO", d.Message);
        Assert.Contains("Its branch does nothing.", d.Message);
    }

    [Fact]
    public void ADeprecatedValue_IsAWarning_AndReadsAsStruckThrough()
    {
        var fact = Param("NOT_EQUAL_TO", Note(SchemaNoteKind.Deprecated, "Use Compare_Mode."));

        var d = Assert.Single(Sut.Handle(fact, Ctx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains(XmlDiagnosticTag.Deprecated, d.Tags ?? []);
    }

    [Fact]
    public void AnUntestedValue_IsInformation()
    {
        var fact = Param("NOT_EQUAL_TO", Note(SchemaNoteKind.Untested, "Never seen in the corpus."));

        Assert.Equal(XmlDiagnosticSeverity.Information, Assert.Single(Sut.Handle(fact, Ctx)).Severity);
    }

    [Fact]
    public void ARemarkOnAValue_IsAHint()
    {
        var fact = Param("NOT_EQUAL_TO", Note(SchemaNoteKind.Remark, "Rare."));

        Assert.Equal(XmlDiagnosticSeverity.Hint, Assert.Single(Sut.Handle(fact, Ctx)).Severity);
    }

    // Since says when something appeared. That belongs in hover, where it is read on purpose, not in
    // the problems list, where it would be noise on every use of a recent value.
    [Fact]
    public void ASinceNote_RaisesNothing()
    {
        var fact = Param("NOT_EQUAL_TO", new SchemaNote(SchemaNoteKind.Since, new Dictionary<string, string>(), "1.1"));

        Assert.Empty(Sut.Handle(fact, Ctx));
    }

    [Fact]
    public void TwoNotes_RaiseTwoDiagnostics_WorstFirst()
    {
        var fact = Param("NOT_EQUAL_TO",
            Note(SchemaNoteKind.Remark, "Rare."),
            Note(SchemaNoteKind.BuggedInEngine, "Does nothing."));

        var results = Sut.Handle(fact, Ctx).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(XmlDiagnosticSeverity.Error, results[0].Severity);
        Assert.Equal(XmlDiagnosticSeverity.Hint, results[1].Severity);
    }

    [Fact]
    public void AValueWithoutNotes_RaisesNothing()
    {
        Assert.Empty(Sut.Handle(Param("EQUAL_TO"), Ctx));
    }

    // A value nobody declared is somebody else's diagnostic - the unknown-value check already names
    // it, and saying it twice helps no one.
    [Fact]
    public void AnUnknownValue_RaisesNothing()
    {
        var fact = Param("WHATEVER", Note(SchemaNoteKind.BuggedInEngine, "Does nothing."));

        Assert.Empty(Sut.Handle(fact, Ctx));
    }

    [Fact]
    public void AnEmptyValue_RaisesNothing()
    {
        Assert.Empty(Sut.Handle(Param("", Note(SchemaNoteKind.BuggedInEngine, "Does nothing.")), Ctx));
    }

    // Values are compared the way the engine reads them, which is not case-sensitively.
    [Fact]
    public void TheValueIsMatchedIgnoringCase()
    {
        var fact = Param("not_equal_to", Note(SchemaNoteKind.BuggedInEngine, "Does nothing."));

        Assert.Single(Sut.Handle(fact, Ctx));
    }
}
