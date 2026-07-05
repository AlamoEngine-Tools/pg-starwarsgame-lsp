// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Commands;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Commands;

public sealed class CreateLocalisationKeyCommandHandlerTest
{
    // ── XML ──────────────────────────────────────────────────────────────────

    private const string XmlNs = "urn:alamoenginetools:localisation:v1";
    // ── CSV ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CsvFile_AppendsNewKeyRowAtEnd()
    {
        const string path = "/mod/Data/Text/MasterTextFile.csv";
        const string existing = "key,ENGLISH\nTEXT_EXISTING,Hello World\n";
        var (handler, fs, _) = BuildHandler(path, existing);

        await handler.Handle(Request("TEXT_NEW", path, new Dictionary<string, string> { ["ENGLISH"] = "New Entry" }),
            CancellationToken.None);

        var lines = fs.File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // header + existing + new
        Assert.StartsWith("TEXT_NEW,", lines[2]);
    }

    [Fact]
    public async Task Handle_CsvFile_NewRowContainsTranslation()
    {
        const string path = "/mod/Data/Text/MasterTextFile.csv";
        const string existing = "key,ENGLISH,GERMAN\nTEXT_X,English,German\n";
        var (handler, fs, _) = BuildHandler(path, existing);

        await handler.Handle(
            Request("TEXT_NEW", path, new Dictionary<string, string> { ["ENGLISH"] = "Hello", ["GERMAN"] = "Hallo" }),
            CancellationToken.None);

        var content = fs.File.ReadAllText(path);
        Assert.Contains("TEXT_NEW,Hello,Hallo", content);
    }

    [Fact]
    public async Task Handle_CsvFile_KeyAlreadyExists_DoesNotDuplicate()
    {
        const string path = "/mod/Data/Text/MasterTextFile.csv";
        const string existing = "key,ENGLISH\nTEXT_DUPE,Value\n";
        var (handler, fs, _) = BuildHandler(path, existing);

        await handler.Handle(Request("TEXT_DUPE", path, new Dictionary<string, string> { ["ENGLISH"] = "Another" }),
            CancellationToken.None);

        var lines = fs.File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // header + original only
    }

    // ── NLS ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NlsFile_AppendsKeyEqualsValueLine()
    {
        const string path = "/mod/Data/Text/MasterTextFile.properties";
        const string existing = "TEXT_EXISTING=Hello World\n";
        var (handler, fs, _) = BuildHandler(path, existing);

        await handler.Handle(Request("TEXT_NEW", path, new Dictionary<string, string> { ["ENGLISH"] = "New Value" }),
            CancellationToken.None);

        var content = fs.File.ReadAllText(path);
        Assert.Contains("TEXT_NEW=New Value", content);
    }

    [Fact]
    public async Task Handle_NlsFile_KeyAlreadyExists_DoesNotDuplicate()
    {
        const string path = "/mod/Data/Text/MasterTextFile.properties";
        const string existing = "TEXT_DUPE=Existing\n";
        var (handler, fs, _) = BuildHandler(path, existing);

        await handler.Handle(Request("TEXT_DUPE", path, new Dictionary<string, string> { ["ENGLISH"] = "New" }),
            CancellationToken.None);

        var lines = fs.File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    [Fact]
    public async Task Handle_XmlFile_AddsLocalisationElement()
    {
        const string path = "/mod/Data/Text/MasterTextFile.xml";
        var existing = BuildXml();
        var (handler, fs, _) = BuildHandler(path, existing);

        await handler.Handle(Request("TEXT_NEW", path, new Dictionary<string, string> { ["ENGLISH"] = "Hello" }),
            CancellationToken.None);

        var xdoc = XDocument.Parse(fs.File.ReadAllText(path));
        var ns = XNamespace.Get(XmlNs);
        var entries = xdoc.Root!.Elements(ns + "Localisation").ToList();
        Assert.Single(entries.Where(e => e.Attribute("key")?.Value == "TEXT_NEW"));
    }

    [Fact]
    public async Task Handle_XmlFile_TranslationValueIsCorrect()
    {
        const string path = "/mod/Data/Text/MasterTextFile.xml";
        var (handler, fs, _) = BuildHandler(path, BuildXml());

        await handler.Handle(Request("TEXT_NEW", path, new Dictionary<string, string> { ["ENGLISH"] = "Hello XML" }),
            CancellationToken.None);

        var xdoc = XDocument.Parse(fs.File.ReadAllText(path));
        var ns = XNamespace.Get(XmlNs);
        var entry = xdoc.Root!.Elements(ns + "Localisation")
            .Single(e => e.Attribute("key")?.Value == "TEXT_NEW");
        var translation = entry.Descendants(ns + "Translation").First();
        Assert.Equal("Hello XML", translation.Value);
    }

    [Fact]
    public async Task Handle_XmlFile_KeyAlreadyExists_DoesNotDuplicate()
    {
        const string path = "/mod/Data/Text/MasterTextFile.xml";
        var existing = BuildXml(("TEXT_DUPE", "ENGLISH", "Existing"));
        var (handler, fs, _) = BuildHandler(path, existing);

        await handler.Handle(Request("TEXT_DUPE", path, new Dictionary<string, string> { ["ENGLISH"] = "New" }),
            CancellationToken.None);

        var xdoc = XDocument.Parse(fs.File.ReadAllText(path));
        var ns = XNamespace.Get(XmlNs);
        var matches = xdoc.Root!.Elements(ns + "Localisation")
            .Where(e => e.Attribute("key")?.Value == "TEXT_DUPE").ToList();
        Assert.Single(matches);
    }

    // ── feature flag ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LocalisationFlagOff_NoWriteNoReload()
    {
        const string path = "/mod/a.csv";
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Localisation = false } });
        var (handler, fs, reload) = BuildHandler(path, "key,ENGLISH\n", config);

        await handler.Handle(Request("TEXT_NEW", path, new Dictionary<string, string> { ["ENGLISH"] = "v" }),
            CancellationToken.None);

        Assert.Equal("key,ENGLISH\n", fs.File.ReadAllText(path));
        Assert.False(reload.WasReloaded);
    }

    // ── guard cases ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NullArguments_NoWrite()
    {
        var (handler, fs, reload) = BuildHandler("/mod/a.csv", "key,ENGLISH\n");

        await handler.Handle(new ExecuteCommandParams
        {
            Command = CreateLocalisationKeyCommandHandler.CommandName
        }, CancellationToken.None);

        Assert.Equal("key,ENGLISH\n", fs.File.ReadAllText("/mod/a.csv"));
        Assert.False(reload.WasReloaded);
    }

    [Fact]
    public async Task Handle_MissingKeyName_NoWrite()
    {
        const string path = "/mod/a.csv";
        var (handler, _, reload) = BuildHandler(path, "key,ENGLISH\n");

        await handler.Handle(Request("", path, new Dictionary<string, string> { ["ENGLISH"] = "v" }),
            CancellationToken.None);

        Assert.False(reload.WasReloaded);
    }

    [Fact]
    public async Task Handle_MissingFilePath_NoWrite()
    {
        const string path = "/mod/a.csv";
        var (handler, _, reload) = BuildHandler(path, "key,ENGLISH\n");

        await handler.Handle(Request("TEXT_NEW", "", new Dictionary<string, string> { ["ENGLISH"] = "v" }),
            CancellationToken.None);

        Assert.False(reload.WasReloaded);
    }

    [Fact]
    public async Task Handle_FileDoesNotExist_NoWrite()
    {
        var (handler, _, reload) = BuildHandler(null, null);

        await handler.Handle(
            Request("TEXT_NEW", "/nonexistent.csv", new Dictionary<string, string> { ["ENGLISH"] = "v" }),
            CancellationToken.None);

        Assert.False(reload.WasReloaded);
    }

    [Fact]
    public async Task Handle_SuccessfulWrite_TriggersReload()
    {
        const string path = "/mod/a.csv";
        var (handler, _, reload) = BuildHandler(path, "key,ENGLISH\n");

        await handler.Handle(Request("TEXT_NEW", path, new Dictionary<string, string> { ["ENGLISH"] = "v" }),
            CancellationToken.None);

        Assert.True(reload.WasReloaded);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ExecuteCommandParams Request(
        string keyName, string filePath, Dictionary<string, string> translations)
    {
        return new ExecuteCommandParams
        {
            Command = CreateLocalisationKeyCommandHandler.CommandName,
            Arguments = new JArray(keyName, filePath, JObject.FromObject(translations))
        };
    }

    private static string BuildXml(params (string Key, string Lang, string Value)[] entries)
    {
        var ns = XNamespace.Get(XmlNs);
        var root = new XElement(ns + "LocalisationData");
        foreach (var (key, lang, value) in entries)
            root.Add(new XElement(ns + "Localisation",
                new XAttribute("key", key),
                new XElement(ns + "TranslationData",
                    new XElement(ns + "Translation",
                        new XAttribute("Language", lang),
                        value))));
        return new XDocument(root).ToString();
    }

    private static (CreateLocalisationKeyCommandHandler handler, MockFileSystem fs, SpyReloadService2 reload)
        BuildHandler(string? filePath, string? initialContent, ILspConfigurationProvider? config = null)
    {
        var files = new Dictionary<string, MockFileData>();
        if (filePath is not null && initialContent is not null)
            files[filePath] = new MockFileData(initialContent);

        var mockFs = new MockFileSystem(files);
        var fileHelper = new FileHelper(mockFs);
        var reload = new SpyReloadService2();
        var entryWriter = new LocalisationEntryWriter(fileHelper, NullLogger<LocalisationEntryWriter>.Instance);
        var handler = new CreateLocalisationKeyCommandHandler(
            entryWriter,
            fileHelper,
            reload,
            NullLogger<CreateLocalisationKeyCommandHandler>.Instance,
            config ?? new FakeLspConfigurationProvider());
        return (handler, mockFs, reload);
    }

    private sealed class SpyReloadService2 : IModProjectReloadService
    {
        public bool WasReloaded { get; private set; }
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig => null;
        public IReadOnlyList<string>? LastWorkspaceRoots => null;

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            WasReloaded = true;
            return Task.CompletedTask;
        }

        public Task ReloadLocalisationAsync(CancellationToken ct)
        {
            WasReloaded = true;
            return Task.CompletedTask;
        }
    }
}