// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;

namespace PG.StarWarsGame.LSP.Server.Tests.Localisation;

public sealed class LocalisationEntryWriterTest
{
    private const string XmlNs = "urn:alamoenginetools:localisation:v1";

    // ── ExistsAsync ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/mod/f.csv", "key,ENGLISH\nTEXT_A,Hello\n", true)]
    [InlineData("/mod/f.csv", "key,ENGLISH\nTEXT_B,Hello\n", false)]
    [InlineData("/mod/f.properties", "TEXT_A=Hello\n", true)]
    [InlineData("/mod/f.properties", "TEXT_B=Hello\n", false)]
    public async Task ExistsAsync_ReturnsExpected(string path, string content, bool expected)
    {
        var (writer, _) = Build(path, content);

        Assert.Equal(expected, await writer.ExistsAsync(path, "TEXT_A", CancellationToken.None));
    }

    [Fact]
    public async Task ExistsAsync_Xml_ReturnsExpected()
    {
        const string path = "/mod/f.xml";
        var (writer, _) = Build(path, BuildXml(("TEXT_A", "ENGLISH", "Hello")));

        Assert.True(await writer.ExistsAsync(path, "TEXT_A", CancellationToken.None));
        Assert.False(await writer.ExistsAsync(path, "TEXT_B", CancellationToken.None));
    }

    // ── UpsertAsync: new key (insert) ────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_Csv_NewKey_AppendsRow()
    {
        const string path = "/mod/f.csv";
        var (writer, fs) = Build(path, "key,ENGLISH\nTEXT_A,Hello\n");

        var written = await writer.UpsertAsync(path, "TEXT_B", new Dictionary<string, string> { ["ENGLISH"] = "World" },
            CancellationToken.None);

        Assert.True(written);
        var lines = fs.File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("TEXT_B,World", lines[2]);
    }

    [Fact]
    public async Task UpsertAsync_Csv_ValueWithComma_IsEscaped()
    {
        const string path = "/mod/f.csv";
        var (writer, fs) = Build(path, "key,ENGLISH\n");

        await writer.UpsertAsync(path, "TEXT_A", new Dictionary<string, string> { ["ENGLISH"] = "Hello, Commander" },
            CancellationToken.None);

        var content = fs.File.ReadAllText(path);
        Assert.Contains("TEXT_A,\"Hello, Commander\"", content);
    }

    [Fact]
    public async Task UpsertAsync_Nls_NewKey_AppendsLine()
    {
        const string path = "/mod/f.properties";
        var (writer, fs) = Build(path, "TEXT_A=Hello\n");

        await writer.UpsertAsync(path, "TEXT_B", new Dictionary<string, string> { ["ENGLISH"] = "World" },
            CancellationToken.None);

        var content = fs.File.ReadAllText(path);
        Assert.Contains("TEXT_B=World", content);
        Assert.Contains("TEXT_A=Hello", content);
    }

    [Fact]
    public async Task UpsertAsync_Xml_NewKey_AddsElement()
    {
        const string path = "/mod/f.xml";
        var (writer, fs) = Build(path, BuildXml(("TEXT_A", "ENGLISH", "Hello")));

        await writer.UpsertAsync(path, "TEXT_B", new Dictionary<string, string> { ["ENGLISH"] = "World" },
            CancellationToken.None);

        var xdoc = XDocument.Parse(fs.File.ReadAllText(path));
        var ns = XNamespace.Get(XmlNs);
        var entries = xdoc.Root!.Elements(ns + "Localisation").ToList();
        Assert.Equal(2, entries.Count);
        var newEntry = entries.Single(e => e.Attribute("key")?.Value == "TEXT_B");
        Assert.Equal("World", newEntry.Descendants(ns + "Translation").First().Value);
    }

    // ── UpsertAsync: existing key (update) ───────────────────────────────────

    [Fact]
    public async Task UpsertAsync_Csv_ExistingKey_ReplacesRow_NotAppends()
    {
        const string path = "/mod/f.csv";
        var (writer, fs) = Build(path, "key,ENGLISH\nTEXT_A,Old Value\nTEXT_B,Other\n");

        await writer.UpsertAsync(path, "TEXT_A", new Dictionary<string, string> { ["ENGLISH"] = "New Value" },
            CancellationToken.None);

        var lines = fs.File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // no new row added
        Assert.Contains("TEXT_A,New Value", lines);
        Assert.Contains("TEXT_B,Other", lines);
    }

    [Fact]
    public async Task UpsertAsync_Nls_ExistingKey_ReplacesLine()
    {
        const string path = "/mod/f.properties";
        var (writer, fs) = Build(path, "TEXT_A=Old\nTEXT_B=Other\n");

        await writer.UpsertAsync(path, "TEXT_A", new Dictionary<string, string> { ["ENGLISH"] = "New" },
            CancellationToken.None);

        var lines = fs.File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("TEXT_A=New", lines);
    }

    [Fact]
    public async Task UpsertAsync_Xml_ExistingKey_ReplacesTranslationData_NotDuplicated()
    {
        const string path = "/mod/f.xml";
        var (writer, fs) = Build(path, BuildXml(("TEXT_A", "ENGLISH", "Old")));

        await writer.UpsertAsync(path, "TEXT_A", new Dictionary<string, string> { ["ENGLISH"] = "New" },
            CancellationToken.None);

        var xdoc = XDocument.Parse(fs.File.ReadAllText(path));
        var ns = XNamespace.Get(XmlNs);
        var entries = xdoc.Root!.Elements(ns + "Localisation").ToList();
        Assert.Single(entries);
        Assert.Equal("New", entries[0].Descendants(ns + "Translation").First().Value);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Csv_RemovesOnlyMatchingRow()
    {
        const string path = "/mod/f.csv";
        var (writer, fs) = Build(path, "key,ENGLISH\nTEXT_A,A\nTEXT_B,B\n");

        var deleted = await writer.DeleteAsync(path, "TEXT_A", CancellationToken.None);

        Assert.True(deleted);
        var content = fs.File.ReadAllText(path);
        Assert.DoesNotContain("TEXT_A", content);
        Assert.Contains("TEXT_B,B", content);
    }

    [Fact]
    public async Task DeleteAsync_Csv_KeyNotFound_ReturnsFalse_FileUnchanged()
    {
        const string path = "/mod/f.csv";
        const string original = "key,ENGLISH\nTEXT_A,A\n";
        var (writer, fs) = Build(path, original);

        var deleted = await writer.DeleteAsync(path, "TEXT_MISSING", CancellationToken.None);

        Assert.False(deleted);
        Assert.Equal(original, fs.File.ReadAllText(path));
    }

    [Fact]
    public async Task DeleteAsync_Nls_RemovesOnlyMatchingLine()
    {
        const string path = "/mod/f.properties";
        var (writer, fs) = Build(path, "TEXT_A=A\nTEXT_B=B\n");

        await writer.DeleteAsync(path, "TEXT_A", CancellationToken.None);

        var content = fs.File.ReadAllText(path);
        Assert.DoesNotContain("TEXT_A", content);
        Assert.Contains("TEXT_B=B", content);
    }

    [Fact]
    public async Task DeleteAsync_Xml_RemovesOnlyMatchingElement()
    {
        const string path = "/mod/f.xml";
        var (writer, fs) = Build(path, BuildXml(("TEXT_A", "ENGLISH", "A"), ("TEXT_B", "ENGLISH", "B")));

        var deleted = await writer.DeleteAsync(path, "TEXT_A", CancellationToken.None);

        Assert.True(deleted);
        var xdoc = XDocument.Parse(fs.File.ReadAllText(path));
        var ns = XNamespace.Get(XmlNs);
        var entries = xdoc.Root!.Elements(ns + "Localisation").ToList();
        Assert.Single(entries);
        Assert.Equal("TEXT_B", entries[0].Attribute("key")!.Value);
    }

    // ── AddLanguageAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddLanguageAsync_Csv_AddsHeaderColumnAndEmptyCellPerRow()
    {
        const string path = "/mod/f.csv";
        var (writer, fs) = Build(path, "key,ENGLISH\nTEXT_A,Hello\nTEXT_B,World\n");

        var added = await writer.AddLanguageAsync(path, "GERMAN", CancellationToken.None);

        Assert.True(added);
        var lines = fs.File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("key,ENGLISH,GERMAN", lines[0]);
        Assert.Equal("TEXT_A,Hello,", lines[1]);
        Assert.Equal("TEXT_B,World,", lines[2]);
    }

    [Fact]
    public async Task AddLanguageAsync_Csv_AlreadyPresent_ReturnsFalse_FileUnchanged()
    {
        const string path = "/mod/f.csv";
        const string original = "key,ENGLISH,GERMAN\nTEXT_A,Hello,Hallo\n";
        var (writer, fs) = Build(path, original);

        var added = await writer.AddLanguageAsync(path, "GERMAN", CancellationToken.None);

        Assert.False(added);
        Assert.Equal(original, fs.File.ReadAllText(path));
    }

    [Fact]
    public async Task AddLanguageAsync_Csv_EmptyColumnSurvivesRoundTrip()
    {
        // The client's old heuristic (derive languages from non-empty values) silently dropped a
        // freshly-added, still-empty language column. The header itself is now the source of
        // truth, so re-reading the file must still report the new language.
        const string path = "/mod/f.csv";
        var (writer, fs) = Build(path, "key,ENGLISH\nTEXT_A,Hello\n");

        await writer.AddLanguageAsync(path, "GERMAN", CancellationToken.None);

        var content = fs.File.ReadAllText(path);
        var header = content.Split('\n')[0];
        Assert.Equal("key,ENGLISH,GERMAN", header);
    }

    [Fact]
    public async Task AddLanguageAsync_Xml_AddsEmptyTranslationToEveryEntry()
    {
        const string path = "/mod/f.xml";
        var (writer, fs) = Build(path, BuildXml(("TEXT_A", "ENGLISH", "Hello"), ("TEXT_B", "ENGLISH", "World")));

        var added = await writer.AddLanguageAsync(path, "GERMAN", CancellationToken.None);

        Assert.True(added);
        var xdoc = XDocument.Parse(fs.File.ReadAllText(path));
        var ns = XNamespace.Get(XmlNs);
        foreach (var entry in xdoc.Root!.Elements(ns + "Localisation"))
        {
            var germanTranslation = entry.Descendants(ns + "Translation")
                .FirstOrDefault(t => t.Attribute("Language")?.Value == "GERMAN");
            Assert.NotNull(germanTranslation);
            Assert.Equal(string.Empty, germanTranslation.Value);
        }
    }

    [Fact]
    public async Task AddLanguageAsync_Xml_AlreadyPresent_ReturnsFalse()
    {
        const string path = "/mod/f.xml";
        var (writer, _) = Build(path, BuildXml(("TEXT_A", "GERMAN", "Hallo")));

        var added = await writer.AddLanguageAsync(path, "GERMAN", CancellationToken.None);

        Assert.False(added);
    }

    [Fact]
    public async Task AddLanguageAsync_Nls_NotApplicable_ReturnsFalse_FileUnchanged()
    {
        const string path = "/mod/f.properties";
        const string original = "TEXT_A=Hello\n";
        var (writer, fs) = Build(path, original);

        var added = await writer.AddLanguageAsync(path, "GERMAN", CancellationToken.None);

        Assert.False(added);
        Assert.Equal(original, fs.File.ReadAllText(path));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string BuildXml(params (string Key, string Lang, string Value)[] entries)
    {
        var ns = XNamespace.Get(XmlNs);
        var root = new XElement(ns + "LocalisationData");
        foreach (var (key, lang, value) in entries)
            root.Add(new XElement(ns + "Localisation",
                new XAttribute("key", key),
                new XElement(ns + "TranslationData",
                    new XElement(ns + "Translation", new XAttribute("Language", lang), value))));
        return new XDocument(root).ToString();
    }

    private static (LocalisationEntryWriter Writer, MockFileSystem Fs) Build(string path, string content)
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [path] = new(content) });
        var writer = new LocalisationEntryWriter(new FileHelper(fs), NullLogger<LocalisationEntryWriter>.Instance);
        return (writer, fs);
    }
}
