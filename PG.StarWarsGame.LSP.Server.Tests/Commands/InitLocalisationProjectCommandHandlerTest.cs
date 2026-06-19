// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Commands;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Commands;

public sealed class InitLocalisationProjectCommandHandlerTest
{
    [Fact]
    public async Task Handle_CsvFormat_CreatesCsvFileUnderModDataText()
    {
        var (handler, fs, _) = BuildHandler("/mod");

        await handler.Handle(Request("Csv"), CancellationToken.None);

        Assert.True(fs.AllFiles.Any(f => f.EndsWith("MasterTextFile.csv",
            StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Handle_CsvFormat_HeaderRowStartsWithKey()
    {
        var (handler, fs, _) = BuildHandler("/mod");

        await handler.Handle(Request("Csv"), CancellationToken.None);

        var content = ReadOutput(fs, "MasterTextFile.csv");
        var firstLine = content.Split('\n')[0];
        Assert.StartsWith("key,", firstLine);
    }

    [Fact]
    public async Task Handle_CsvFormat_ContainsBaselineKeys()
    {
        var (handler, fs, _) = BuildHandler("/mod");

        await handler.Handle(Request("Csv"), CancellationToken.None);

        var content = ReadOutput(fs, "MasterTextFile.csv");
        // EaW+FoC baseline has thousands of TEXT_ keys
        Assert.Contains("TEXT_", content);
        // Must have more than just the header
        Assert.True(content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 1);
    }

    [Fact]
    public async Task Handle_NlsFormat_CreatesPropertiesFile()
    {
        var (handler, fs, _) = BuildHandler("/mod");

        await handler.Handle(Request("Nls"), CancellationToken.None);

        Assert.True(fs.AllFiles.Any(f => f.EndsWith("MasterTextFile.properties",
            StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Handle_NlsFormat_ContainsKeyEqualsValueLines()
    {
        var (handler, fs, _) = BuildHandler("/mod");

        await handler.Handle(Request("Nls"), CancellationToken.None);

        var content = ReadOutput(fs, "MasterTextFile.properties");
        Assert.Contains("=", content);
        Assert.Contains("TEXT_", content);
    }

    [Fact]
    public async Task Handle_XmlFormat_CreatesXmlFile()
    {
        var (handler, fs, _) = BuildHandler("/mod");

        await handler.Handle(Request("Xml"), CancellationToken.None);

        Assert.True(fs.AllFiles.Any(f => f.EndsWith("MasterTextFile.xml",
            StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Handle_XmlFormat_FileIsWellFormedAndHasEntries()
    {
        var (handler, fs, _) = BuildHandler("/mod");

        await handler.Handle(Request("Xml"), CancellationToken.None);

        var content = ReadOutput(fs, "MasterTextFile.xml");
        var xdoc = XDocument.Parse(content);
        Assert.NotNull(xdoc.Root);
        Assert.NotEmpty(xdoc.Root.Elements());
    }

    [Fact]
    public async Task Handle_FileAlreadyExists_DoesNotOverwrite()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/Data/Text/MasterTextFile.csv"] = new("EXISTING")
        });
        var (handler, _, _) = BuildHandler("/mod", mockFs);

        await handler.Handle(Request("Csv"), CancellationToken.None);

        Assert.Equal("EXISTING", mockFs.File.ReadAllText("/mod/Data/Text/MasterTextFile.csv"));
    }

    [Fact]
    public async Task Handle_SuccessfulWrite_TriggersReload()
    {
        var (handler, _, reload) = BuildHandler("/mod");

        await handler.Handle(Request("Csv"), CancellationToken.None);

        Assert.True(reload.WasReloaded);
    }

    [Fact]
    public async Task Handle_FileAlreadyExists_DoesNotTriggerReload()
    {
        var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/mod/Data/Text/MasterTextFile.csv"] = new("EXISTING")
        });
        var (handler, _, reload) = BuildHandler("/mod", mockFs);

        await handler.Handle(Request("Csv"), CancellationToken.None);

        Assert.False(reload.WasReloaded);
    }

    [Fact]
    public async Task Handle_NoTextRoots_NoFileCreated()
    {
        // No resolved project text directory → strict warn-and-return, nothing written.
        var (handler, fs, reload) = BuildHandler(null);

        await handler.Handle(Request("Csv"), CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.WasReloaded);
    }

    [Fact]
    public async Task Handle_CsvFormat_SingleTextRoot_CreatesFileInTextRoot()
    {
        var workspaceConfig = new WorkspaceConfiguration([], [], ["/mytext"], [], null);
        var (handler, fs, _) = BuildHandler(null, workspaceConfig: workspaceConfig);

        await handler.Handle(Request("Csv"), CancellationToken.None);

        var path = fs.AllFiles.FirstOrDefault(f => f.EndsWith("MasterTextFile.csv",
            StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(path);
        // File must live directly in the declared text root, not under a Data/Text subdirectory.
        Assert.Contains("mytext", fs.Path.GetDirectoryName(path)!,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data", fs.Path.GetDirectoryName(path)!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_CsvFormat_MultipleTextRoots_CreatesFileUnderWorkspaceDataText()
    {
        var workspaceConfig = new WorkspaceConfiguration([], [], ["/mod/Data/Text1", "/mod/Data/Text2"], [], null);
        var (handler, fs, _) = BuildHandler(null,
            workspaceConfig: workspaceConfig,
            workspaceRoots: ["/mod"]);

        await handler.Handle(Request("Csv"), CancellationToken.None);

        var path = fs.AllFiles.FirstOrDefault(f => f.EndsWith("MasterTextFile.csv",
            StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(path);
        var dir = fs.Path.GetDirectoryName(path)!;
        Assert.Contains("Data", dir, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Text", dir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_NullArguments_NoFileCreated()
    {
        var (handler, fs, reload) = BuildHandler("/mod");

        await handler.Handle(new ExecuteCommandParams
        {
            Command = InitLocalisationProjectCommandHandler.CommandName
        }, CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.WasReloaded);
    }

    [Fact]
    public async Task Handle_UnknownFormat_NoFileCreated()
    {
        var (handler, fs, reload) = BuildHandler("/mod");

        await handler.Handle(Request("Dat"), CancellationToken.None);

        Assert.Empty(fs.AllFiles);
        Assert.False(reload.WasReloaded);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ExecuteCommandParams Request(string format)
    {
        return new ExecuteCommandParams
        {
            Command = InitLocalisationProjectCommandHandler.CommandName,
            Arguments = new JArray(JObject.FromObject(new { format }))
        };
    }

    private static string ReadOutput(MockFileSystem fs, string fileName)
    {
        var path = fs.AllFiles.First(f =>
            f.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        return fs.File.ReadAllText(path);
    }

    private static (InitLocalisationProjectCommandHandler handler, MockFileSystem fs, SpyReloadService reload)
        BuildHandler(
            string? modPath,
            MockFileSystem? initialFs = null,
            WorkspaceConfiguration? workspaceConfig = null,
            IReadOnlyList<string>? workspaceRoots = null)
    {
        var mockFs = initialFs ?? new MockFileSystem();

        var services = new ServiceCollection();
        // Register MockFileSystem BEFORE SupportLocalisationBaseline so TryAddSingleton skips RealFileSystem.
        services.AddSingleton<IFileSystem>(mockFs);
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        // The target directory now comes from the resolved project's text roots. A non-null modPath is
        // mapped to a single text root (<modPath>/Data/Text) so existing format/content tests still
        // produce a file; passing null leaves no text root, exercising the warn-and-return path.
        var effectiveConfig = workspaceConfig
                              ?? (modPath != null
                                  ? new WorkspaceConfiguration(
                                      [], [], [mockFs.Path.Combine(modPath, "Data", "Text")], [], null)
                                  : null);

        var reload = new SpyReloadService
        {
            LastWorkspaceConfig = effectiveConfig,
            LastWorkspaceRoots = workspaceRoots
        };
        var handler = new InitLocalisationProjectCommandHandler(
            sp.GetRequiredService<IBaselineTranslationProvider>(),
            sp.GetRequiredService<ICsvTranslationExporter>(),
            sp.GetRequiredService<IXmlTranslationExporter>(),
            sp.GetRequiredService<IPropertiesTranslationExporter>(),
            sp.GetRequiredService<ITranslationDatabaseFactory>(),
            sp.GetRequiredService<ILanguageService>(),
            new FileHelper(mockFs),
            reload,
            NullLogger<InitLocalisationProjectCommandHandler>.Instance);

        return (handler, mockFs, reload);
    }

    private sealed class SpyReloadService : IModProjectReloadService
    {
        public bool WasReloaded { get; private set; }
        public IReadOnlyList<string>? LastAssetRoots => null;
        public WorkspaceConfiguration? LastWorkspaceConfig { get; init; }
        public IReadOnlyList<string>? LastWorkspaceRoots { get; init; }

        public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReloadAsync(CancellationToken ct)
        {
            WasReloaded = true;
            return Task.CompletedTask;
        }
    }
}