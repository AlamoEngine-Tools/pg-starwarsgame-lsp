// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     The editor's document model: rows addressed by index rather than key.
///     <para>
///         Index addressing is what makes credits files editable at all - they allow duplicate keys
///         and their order is significant, so a key-addressed model cannot even name the row to
///         change. The verbatim per-row slices are what let a save rewrite only the rows that were
///         touched.
///     </para>
/// </summary>
public sealed class LocalisationRowReaderTest
{
    private static ILocalisationRowReader Reader()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        return services.BuildServiceProvider().GetRequiredService<ILocalisationRowReader>();
    }

    private static string Value(LocRowDto row, string language)
    {
        return row.Values.Single(v =>
            string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase)).Value;
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Csv_ReadsRowsInFileOrderWithTheirLanguages()
    {
        const string csv = "key,ENGLISH,GERMAN\nTEXT_A,Alpha,Alfa\nTEXT_B,Beta,Beta_DE\n";

        var doc = Reader().Read(csv, ".csv");

        Assert.Equal(["ENGLISH", "GERMAN"], doc.Languages);
        Assert.Equal(2, doc.Rows.Count);
        Assert.Equal("TEXT_A", doc.Rows[0].Key);
        Assert.Equal(0, doc.Rows[0].Index);
        Assert.Equal("Alfa", Value(doc.Rows[0], "GERMAN"));
        Assert.Equal(1, doc.Rows[1].Index);
    }

    // The failure this prevents: the existing writer splits CSV on raw '\n', so a quoted field
    // containing a newline is read as two records. Under index addressing that misaligns every
    // subsequent row, which is how an edit lands on the wrong line.
    [Fact]
    public void Csv_QuotedFieldContainingANewline_IsOneRow()
    {
        const string csv = "key,ENGLISH\nTEXT_A,\"line one\nline two\"\nTEXT_B,Beta\n";

        var doc = Reader().Read(csv, ".csv");

        Assert.Equal(2, doc.Rows.Count);
        Assert.Equal("line one\nline two", Value(doc.Rows[0], "ENGLISH"));
        Assert.Equal("TEXT_B", doc.Rows[1].Key);
    }

    [Fact]
    public void Csv_QuotedFieldContainingACommaOrQuote_IsPreserved()
    {
        const string csv = "key,ENGLISH\nTEXT_A,\"one, two\"\nTEXT_B,\"say \"\"hi\"\"\"\n";

        var doc = Reader().Read(csv, ".csv");

        Assert.Equal("one, two", Value(doc.Rows[0], "ENGLISH"));
        Assert.Equal("say \"hi\"", Value(doc.Rows[1], "ENGLISH"));
    }

    // The whole reason the ordered model exists.
    [Fact]
    public void Csv_DuplicateKeys_BecomeSeparateRows()
    {
        const string csv = "key,ENGLISH\nCREDIT_ROLE,Director\nCREDIT_ROLE,Producer\n";

        var doc = Reader().Read(csv, ".csv");

        Assert.Equal(2, doc.Rows.Count);
        Assert.Equal("Director", Value(doc.Rows[0], "ENGLISH"));
        Assert.Equal("Producer", Value(doc.Rows[1], "ENGLISH"));
    }

    // An omitted language would make the grid render a gap rather than an editable empty cell, and
    // the blank spacer rows a credits crawl needs are exactly this shape.
    [Fact]
    public void Csv_EmptyCell_IsAnEmptyValueNotAnOmittedLanguage()
    {
        const string csv = "key,ENGLISH,GERMAN\nTEXT_A,Alpha,\n";

        var doc = Reader().Read(csv, ".csv");

        Assert.Equal(2, doc.Rows[0].Values.Count);
        Assert.Equal("", Value(doc.Rows[0], "GERMAN"));
    }

    [Fact]
    public void Csv_BlankSpacerRow_IsKept()
    {
        const string csv = "key,ENGLISH\nCREDIT_A,Alpha\n,\nCREDIT_B,Beta\n";

        var doc = Reader().Read(csv, ".csv");

        Assert.Equal(3, doc.Rows.Count);
        Assert.Equal("", doc.Rows[1].Key);
        Assert.Equal("", Value(doc.Rows[1], "ENGLISH"));
    }

    // Declared from the header, never from "does any row have a value here" - that heuristic is
    // what used to drop a freshly added, still-empty language column.
    [Fact]
    public void Csv_LanguagesComeFromTheHeaderEvenWhenEveryValueIsEmpty()
    {
        const string csv = "key,ENGLISH,ITALIAN\nTEXT_A,Alpha,\n";

        Assert.Equal(["ENGLISH", "ITALIAN"], Reader().Read(csv, ".csv").Languages);
    }

    [Fact]
    public void Csv_HeaderOnly_YieldsNoRowsButKeepsLanguages()
    {
        var doc = Reader().Read("key,ENGLISH\n", ".csv");

        Assert.Empty(doc.Rows);
        Assert.Equal(["ENGLISH"], doc.Languages);
    }

    // ── verbatim slices ──────────────────────────────────────────────────────

    // Saving must rewrite only the rows that changed; that is only possible if every untouched row
    // can be re-emitted exactly as it was read.
    [Fact]
    public void Csv_EachRowCarriesItsVerbatimSource()
    {
        const string csv = "key,ENGLISH\nTEXT_A,\"one, two\"\nTEXT_B,Beta\n";

        var doc = Reader().Read(csv, ".csv");

        Assert.Equal("TEXT_A,\"one, two\"", doc.Rows[0].Source);
        Assert.Equal("TEXT_B,Beta", doc.Rows[1].Source);
    }

    [Fact]
    public void Csv_HeaderIsCapturedAsThePreamble()
    {
        var doc = Reader().Read("key,ENGLISH\nTEXT_A,Alpha\n", ".csv");

        Assert.Equal("key,ENGLISH", doc.Preamble);
    }

    [Theory]
    [InlineData("key,ENGLISH\nTEXT_A,Alpha\n", "\n")]
    [InlineData("key,ENGLISH\r\nTEXT_A,Alpha\r\n", "\r\n")]
    public void Csv_LineEndingIsDetected(string csv, string expected)
    {
        Assert.Equal(expected, Reader().Read(csv, ".csv").LineEnding);
    }

    // ── XML ──────────────────────────────────────────────────────────────────

    private const string XmlDoc = """
                                  <?xml version="1.0" encoding="utf-8"?>
                                  <Localisations xmlns="urn:alamoenginetools:localisation:v1">
                                    <Localisation key="TEXT_A">
                                      <TranslationData>
                                        <Translation Language="ENGLISH">Alpha</Translation>
                                        <Translation Language="GERMAN">Alfa</Translation>
                                      </TranslationData>
                                    </Localisation>
                                    <Localisation key="TEXT_B">
                                      <TranslationData>
                                        <Translation Language="ENGLISH">Beta</Translation>
                                      </TranslationData>
                                    </Localisation>
                                  </Localisations>
                                  """;

    [Fact]
    public void Xml_ReadsElementsInDocumentOrder()
    {
        var doc = Reader().Read(XmlDoc, ".xml");

        Assert.Equal(2, doc.Rows.Count);
        Assert.Equal("TEXT_A", doc.Rows[0].Key);
        Assert.Equal("TEXT_B", doc.Rows[1].Key);
        Assert.Equal("Alfa", Value(doc.Rows[0], "GERMAN"));
    }

    [Fact]
    public void Xml_LanguagesAreTheDeclaredUnionInFirstSeenOrder()
    {
        Assert.Equal(["ENGLISH", "GERMAN"], Reader().Read(XmlDoc, ".xml").Languages);
    }

    // A language declared on one element but not another must still be an editable empty cell on
    // the rows that lack it, or the column would be unfillable.
    [Fact]
    public void Xml_MissingTranslation_IsAnEmptyValue()
    {
        var doc = Reader().Read(XmlDoc, ".xml");

        Assert.Equal("", Value(doc.Rows[1], "GERMAN"));
    }

    [Fact]
    public void Xml_DuplicateKeys_BecomeSeparateRows()
    {
        const string xml = """
                           <Localisations xmlns="urn:alamoenginetools:localisation:v1">
                             <Localisation key="CREDIT_ROLE">
                               <TranslationData><Translation Language="ENGLISH">Director</Translation></TranslationData>
                             </Localisation>
                             <Localisation key="CREDIT_ROLE">
                               <TranslationData><Translation Language="ENGLISH">Producer</Translation></TranslationData>
                             </Localisation>
                           </Localisations>
                           """;

        var doc = Reader().Read(xml, ".xml");

        Assert.Equal(2, doc.Rows.Count);
        Assert.Equal("Director", Value(doc.Rows[0], "ENGLISH"));
        Assert.Equal("Producer", Value(doc.Rows[1], "ENGLISH"));
    }

    // ── .properties ──────────────────────────────────────────────────────────

    [Fact]
    public void Properties_ReadsEntriesInFileOrderAsASingleLanguage()
    {
        const string text = "TEXT_A=Alpha\nTEXT_B=Beta\n";

        var doc = Reader().Read(text, ".properties");

        Assert.Equal(2, doc.Rows.Count);
        Assert.Equal("TEXT_A", doc.Rows[0].Key);
        Assert.Single(doc.Languages);
        Assert.Equal("Beta", Value(doc.Rows[1], doc.Languages[0]));
    }

    // Comments are the only documentation a .properties file has; a save that dropped them would
    // be a destructive edit disguised as a translation change.
    [Fact]
    public void Properties_CommentsAndBlankLinesSurviveInTheSlices()
    {
        const string text = "# a note\n\nTEXT_A=Alpha\n! another note\nTEXT_B=Beta\n";

        var doc = Reader().Read(text, ".properties");

        Assert.Equal(2, doc.Rows.Count);
        Assert.Contains("# a note", doc.Preamble, StringComparison.Ordinal);
        Assert.Contains("! another note", doc.Rows[1].Leading, StringComparison.Ordinal);
    }

    // ── unsupported ──────────────────────────────────────────────────────────

    [Fact]
    public void UnsupportedExtension_Throws()
    {
        Assert.Throws<NotSupportedException>(() => Reader().Read("anything", ".dat"));
    }
}
