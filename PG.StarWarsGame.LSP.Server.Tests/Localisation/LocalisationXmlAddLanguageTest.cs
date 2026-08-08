// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation.Rows;

using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

/// <summary>
///     Adding a language to an eaw-translation XML file and then writing values into it.
/// </summary>
/// <remarks>
///     In this format a language exists only where a value does - there is no column list to extend -
///     so <c>addLanguage</c> has nothing to declare and does nothing. That made every following
///     write to the new language fail with "Row 0 has no 'GERMAN' translation", which is what a user
///     hit the first time they added a language to an XML file: the column appeared in the grid and
///     the save was refused. Writing a value for a language the row does not have yet is how a
///     language comes into existence here, so it has to create the element.
///     <para>
///         Its own class so the acceptance-gate file (<c>LocalisationDocumentEditorTest</c>) stays
///         untouched.
///     </para>
/// </remarks>
public sealed class LocalisationXmlAddLanguageTest
{
    private const string Ns = "urn:alamoenginetools:localisation:v1";

    private const string Xml = """
                               <?xml version="1.0" encoding="utf-8"?>
                               <Localisations xmlns="urn:alamoenginetools:localisation:v1">
                                 <Localisation key="TEXT_A">
                                   <TranslationData>
                                     <Translation Language="ENGLISH">Alpha</Translation>
                                   </TranslationData>
                                 </Localisation>
                                 <Localisation key="TEXT_B">
                                   <TranslationData>
                                     <Translation Language="ENGLISH">Beta</Translation>
                                   </TranslationData>
                                 </Localisation>
                               </Localisations>
                               """;

    private static ILocalisationDocumentEditor Editor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        services.AddSingleton<IFileHelper>(sp => new FileHelper(sp.GetRequiredService<IFileSystem>()));
        services.TryAddSingleton<ILspConfigurationProvider>(new FakeLspConfigurationProvider());
        services.AddSingleton<ILocalisationRowReader, LocalisationRowReader>();
        services.AddSingleton<ILocalisationDocumentEditor, LocalisationDocumentEditor>();
        return services.BuildServiceProvider().GetRequiredService<ILocalisationDocumentEditor>();
    }

    private static LocalisationEditResult Apply(params LocEditCommandDto[] commands)
    {
        return Editor().Apply(Xml, ".xml", commands);
    }

    private static string ValueOf(string text, string key, string language)
    {
        var ns = XNamespace.Get(Ns);
        return XDocument.Parse(text).Root!
            .Elements(ns + "Localisation")
            .First(e => e.Attribute("key")?.Value == key)
            .Elements(ns + "TranslationData")
            .Elements(ns + "Translation")
            .First(t => t.Attribute("Language")?.Value == language)
            .Value;
    }

    // The exact batch the editor sends when a language is added and filled from the baseline.
    [Fact]
    public void AddLanguageThenWriteIt_Succeeds()
    {
        var result = Apply(
            new LocEditCommandDto("addLanguage", Language: "GERMAN"),
            new LocEditCommandDto("setCell", 0, Language: "GERMAN", Value: "Alfa"),
            new LocEditCommandDto("setCell", 1, Language: "GERMAN", Value: "Beta DE"));

        Assert.True(result.Success, result.Error);
        Assert.Equal("Alfa", ValueOf(result.NewText!, "TEXT_A", "GERMAN"));
        Assert.Equal("Beta DE", ValueOf(result.NewText!, "TEXT_B", "GERMAN"));
    }

    [Fact]
    public void WritingANewLanguage_LeavesTheExistingOnesAlone()
    {
        var result = Apply(
            new LocEditCommandDto("addLanguage", Language: "GERMAN"),
            new LocEditCommandDto("setCell", 0, Language: "GERMAN", Value: "Alfa"));

        Assert.Equal("Alpha", ValueOf(result.NewText!, "TEXT_A", "ENGLISH"));
        Assert.Equal("Beta", ValueOf(result.NewText!, "TEXT_B", "ENGLISH"));
    }

    // Only the rows that were written gain the language: in this format a row without a value for a
    // language simply has no element for it, and inventing empty ones would rewrite the whole file.
    [Fact]
    public void RowsThatWereNotWritten_DoNotGainTheLanguage()
    {
        var result = Apply(
            new LocEditCommandDto("addLanguage", Language: "GERMAN"),
            new LocEditCommandDto("setCell", 0, Language: "GERMAN", Value: "Alfa"));

        var ns = XNamespace.Get(Ns);
        var second = XDocument.Parse(result.NewText!).Root!
            .Elements(ns + "Localisation").First(e => e.Attribute("key")?.Value == "TEXT_B");

        Assert.DoesNotContain(
            second.Elements(ns + "TranslationData").Elements(ns + "Translation"),
            t => t.Attribute("Language")?.Value == "GERMAN");
    }

    // The new element is indented like the one it sits beside, so adding a language does not show up
    // as a reformatting of the whole row in a diff.
    [Fact]
    public void TheNewElementIsIndentedLikeItsSiblings()
    {
        var result = Apply(
            new LocEditCommandDto("addLanguage", Language: "GERMAN"),
            new LocEditCommandDto("setCell", 0, Language: "GERMAN", Value: "Alfa"));

        var lines = result.NewText!.Replace("\r\n", "\n").Split('\n');
        var english = lines.First(l => l.Contains("\"ENGLISH\">Alpha", StringComparison.Ordinal));
        var german = lines.First(l => l.Contains("\"GERMAN\">Alfa", StringComparison.Ordinal));

        // Its own line, indented exactly like the sibling above it - not appended inline.
        static string Indent(string line)
        {
            return line[..(line.Length - line.TrimStart().Length)];
        }

        Assert.Equal(Indent(english), Indent(german));
        Assert.Equal("<Translation Language=\"GERMAN\">Alfa</Translation>", german.Trim());
    }

    // Writing an existing language still overwrites rather than adding a second element for it.
    [Fact]
    public void WritingAnExistingLanguage_ReplacesItsValue()
    {
        var result = Apply(new LocEditCommandDto("setCell", 0, Language: "ENGLISH", Value: "Changed"));

        Assert.Equal("Changed", ValueOf(result.NewText!, "TEXT_A", "ENGLISH"));

        var ns = XNamespace.Get(Ns);
        var first = XDocument.Parse(result.NewText!).Root!
            .Elements(ns + "Localisation").First(e => e.Attribute("key")?.Value == "TEXT_A");
        Assert.Single(first.Elements(ns + "TranslationData").Elements(ns + "Translation"));
    }
}
