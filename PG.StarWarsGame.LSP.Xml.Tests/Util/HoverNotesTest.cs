// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Tests.Util;

/// <summary>
///     What a reader is told about a tag's notes when they hover it.
///     <para>
///         Hover is the one place every note kind surfaces, including the two that raise no
///         diagnostic at all. It is also where most notes have nothing but their kind: the schema
///         2.0.0 sweep converted <c>deprecated: true</c> into a Deprecated note with no text, so
///         "renders nothing without text" would silence the majority of them.
///     </para>
/// </summary>
public sealed class HoverNotesTest
{
    private static string Hover(params SchemaNote[] notes)
    {
        var tag = new XmlTagDefinition
        {
            Tag = "Tactical_Health",
            ValueType = XmlValueType.Float,
            Description = new Dictionary<string, string> { ["en"] = "Health points" },
            Notes = notes
        };
        var node = HtmlNode.CreateNode("<Tactical_Health>100</Tactical_Health>");

        return HoverUtility.BuildTagHover(tag, node, "en").Contents.MarkupContent!.Value;
    }

    private static SchemaNote Note(SchemaNoteKind kind, string? en = null, string? value = null)
    {
        return new SchemaNote(kind,
            en is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["en"] = en },
            value);
    }

    // The common case after the sweep: a kind and nothing else. Saying only "Deprecated" is little,
    // but it is the whole of what the schema knows, and silence is worse.
    [Fact]
    public void ANoteWithoutText_StillSaysItsKind()
    {
        var hover = Hover(Note(SchemaNoteKind.Deprecated));

        Assert.Contains("Deprecated", hover, StringComparison.Ordinal);
    }

    [Fact]
    public void ABuggedNoteWithoutText_StillSaysTheElementDoesNotWork()
    {
        var hover = Hover(Note(SchemaNoteKind.BuggedInEngine));

        Assert.Contains("Does not work", hover, StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteWithText_SaysBothItsKindAndItsWords()
    {
        var hover = Hover(Note(SchemaNoteKind.BuggedInEngine, "The comparison branch does nothing."));

        Assert.Contains("Does not work", hover, StringComparison.Ordinal);
        Assert.Contains("The comparison branch does nothing.", hover, StringComparison.Ordinal);
    }

    // Rank order, not the order the notes happen to arrive in. The parser ranks what it reads, but
    // hover must not depend on having been handed a ranked list.
    [Fact]
    public void Notes_AreShownWorstFirst_WhateverOrderTheyArriveIn()
    {
        var hover = Hover(
            Note(SchemaNoteKind.Remark, "Rarely used."),
            Note(SchemaNoteKind.BuggedInEngine, "Ignored by the engine."));

        var bugged = hover.IndexOf("Ignored by the engine.", StringComparison.Ordinal);
        var remark = hover.IndexOf("Rarely used.", StringComparison.Ordinal);

        Assert.True(bugged >= 0 && remark >= 0, hover);
        Assert.True(bugged < remark, $"the engine bug must come first:\n{hover}");
    }

    [Fact]
    public void ASinceNote_ShowsItsVersion()
    {
        Assert.Contains("1.1", Hover(Note(SchemaNoteKind.Since, value: "1.1")), StringComparison.Ordinal);
    }

    // A note nobody has translated yet still says something: English is better than silence.
    [Fact]
    public void AnUntranslatedNote_FallsBackToEnglish()
    {
        var tag = new XmlTagDefinition
        {
            Tag = "Speed",
            ValueType = XmlValueType.Float,
            Notes = [Note(SchemaNoteKind.Remark, "Only in space.")]
        };
        var node = HtmlNode.CreateNode("<Speed>1</Speed>");

        var hover = HoverUtility.BuildTagHover(tag, node, "de").Contents.MarkupContent!.Value;

        Assert.Contains("Only in space.", hover, StringComparison.Ordinal);
    }

    [Fact]
    public void ATagWithoutNotes_GetsNoNotesBlock()
    {
        Assert.DoesNotContain("---", Hover(), StringComparison.Ordinal);
    }

    // A tag hover puts Since and Deprecated in a status line above the description. Repeating a
    // bare one below it says the same word twice and tells the reader nothing new.
    [Fact]
    public void ATextlessDeprecation_IsNotRepeatedBelowTheStatusLine()
    {
        var hover = Hover(Note(SchemaNoteKind.Deprecated), Note(SchemaNoteKind.Since, value: "FoC 1.1"));

        Assert.Contains("**Since FoC 1.1 - Deprecated**", hover, StringComparison.Ordinal);
        Assert.DoesNotContain("> **Deprecated:**", hover, StringComparison.Ordinal);
        Assert.DoesNotContain("> **Since:**", hover, StringComparison.Ordinal);
    }

    // With words of its own it stays: the status line says a tag is retired, the note says why.
    [Fact]
    public void ADeprecationWithAReason_KeepsItsLineBelow()
    {
        var hover = Hover(Note(SchemaNoteKind.Deprecated, "Use Tactical_Health_Percent."));

        Assert.Contains("> **Deprecated:** *Use Tactical_Health_Percent.*", hover, StringComparison.Ordinal);
    }

    // The summary only exists on a tag hover, so nothing is dropped where there is no status line.
    [Fact]
    public void OnATypeHover_ATextlessDeprecationIsStillShown()
    {
        var type = new GameObjectTypeDefinition
        {
            TypeName = "SpaceUnit",
            Notes = [Note(SchemaNoteKind.Deprecated)]
        };
        var node = HtmlNode.CreateNode("<SpaceUnit Name=\"X\"></SpaceUnit>");

        var hover = HoverUtility.BuildTypeHover(type, node, "en").Contents.MarkupContent!.Value;

        Assert.Contains("> **Deprecated:**", hover, StringComparison.Ordinal);
    }
}