// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.Services;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     A throwaway workspace that really is a DAT project: real <c>.dat</c> files on disk and a
///     <c>.pgproj</c> declaring <c>"type": "dat"</c>.
/// </summary>
/// <remarks>
///     Cannot be a unit test: the DAT services resolve paths against the real filesystem rather
///     than the injected <c>IFileSystem</c>, so a MockFileSystem-based test looks for the file in
///     the process's working directory and fails before reaching what is being tested.
/// </remarks>
public sealed class DatLocalisationServerFixture : LspServerFixture
{
    public string WorkspaceRoot { get; private set; } = string.Empty;
    public string DatPath { get; private set; } = string.Empty;

    protected override string ResolveWorkspacePath()
    {
        WorkspaceRoot = Path.Combine(Path.GetTempPath(), $"aetswg-dat-e2e-{Guid.NewGuid():N}");
        var textDir = Path.Combine(WorkspaceRoot, "data", "text");
        Directory.CreateDirectory(Path.Combine(WorkspaceRoot, "data", "xml"));
        Directory.CreateDirectory(textDir);

        File.WriteAllText(Path.Combine(WorkspaceRoot, "dat-e2e.pgproj"),
            """
            {
              "name": "DAT Localisation E2E",
              "directories": { "xml": [ "data/xml" ] },
              "localisation": { "type": "dat", "directory": "data/text" },
              "projectReferences": []
            }
            """);

        var services = new ServiceCollection();
        // The real filesystem on purpose: the DAT services resolve paths through it, and this
        // fixture's whole point is that the files really exist on disk.
        services.AddSingleton<IFileSystem>(new FileSystem());
        services.SupportLocalisationBaseline();
        var sp = services.BuildServiceProvider();

        var languages = sp.GetRequiredService<ILanguageService>();
        var db = sp.GetRequiredService<ITranslationDatabaseFactory>().CreateKeyed(
            languages.OfficiallySupported());
        db.SetTranslation("TEXT_FROM_DAT", languages.Default, "Hello from a DAT");

        var model = sp.GetRequiredService<IDatTranslationExporter>().Export(db, languages.Default);
        DatPath = Path.Combine(textDir, $"MasterTextFile_{languages.Default.LanguageIdentifier}.dat");
        // Through the abstraction the DAT service expects - it will not take a raw FileStream.
        var fileSystem = sp.GetRequiredService<IFileSystem>();
        using (var stream = fileSystem.File.Create(DatPath))
        {
            sp.GetRequiredService<IDatFileService>().CreateDatFile(stream, model, model.KeySortOrder);
        }

        return WorkspaceRoot;
    }

    protected override object BuildInitOptions()
    {
        return new
        {
            schemaLocalPath = LspTestEnvironment.SchemaLocalPath,
            gamePath = LspTestEnvironment.GamePath,
            baselineLocalPath = LspTestEnvironment.BaselineLocalPath,
            baselineType = LspTestEnvironment.BaselineLocalPath is null ? "None" : null,
            locale = LspTestEnvironment.Locale,
            features = new { tools = new { localisation = true } }
        };
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            if (Directory.Exists(WorkspaceRoot)) Directory.Delete(WorkspaceRoot, true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a run over.
        }
    }
}
