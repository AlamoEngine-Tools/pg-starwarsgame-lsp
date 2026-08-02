// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     A throwaway workspace for the localisation round trips, with
///     <c>tools.localisation</c> turned on (it is off by default, so nothing localisation-related
///     answers without it).
/// </summary>
/// <remarks>
///     Deliberately not the in-repo <c>eaw/</c> workspace the other fixtures use: these tests are
///     the only ones that make the server <em>write</em>, and pointing them at a checked-in
///     workspace would leave the repository dirty - or worse, pass only until someone edited those
///     files. Everything is created per run under the temp directory and deleted afterwards.
/// </remarks>
public sealed class LocalisationServerFixture : LspServerFixture
{
    /// <summary>The generated workspace root. Valid from <c>InitializeAsync</c> onwards.</summary>
    public string WorkspaceRoot { get; private set; } = string.Empty;

    public string TextDirectory => Path.Combine(WorkspaceRoot, "data", "text");
    public string MasterTextPath => Path.Combine(TextDirectory, "MasterTextFile.csv");
    /// <summary>An eaw-translation XML file, where a language exists only where a value does.</summary>
    public string XmlTextPath => Path.Combine(TextDirectory, "Translations.xml");
    public string CreditsPath => Path.Combine(TextDirectory, "creditstext_english.csv");

    protected override string ResolveWorkspacePath()
    {
        WorkspaceRoot = Path.Combine(
            Path.GetTempPath(), $"aetswg-loc-e2e-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(WorkspaceRoot, "data", "xml"));
        Directory.CreateDirectory(TextDirectory);

        File.WriteAllText(Path.Combine(WorkspaceRoot, "loc-e2e.pgproj"),
            """
            {
              "name": "Localisation E2E",
              "directories": {
                "xml": [ "data/xml" ]
              },
              "localisation": {
                "type": "csv",
                "directory": "data/text"
              },
              "projectReferences": []
            }
            """);

        File.WriteAllText(MasterTextPath,
            "key,ENGLISH,GERMAN\n"
            + "TEXT_E2E_ALPHA,Alpha,Alfa\n"
            + "TEXT_E2E_BETA,Beta,Beta\n"
            + "TEXT_E2E_GAMMA,Gamma,Gamma\n");

        File.WriteAllText(XmlTextPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Localisations xmlns="urn:alamoenginetools:localisation:v1">
              <Localisation key="TEXT_E2E_XML">
                <TranslationData>
                  <Translation Language="ENGLISH">Alpha</Translation>
                </TranslationData>
              </Localisation>
            </Localisations>
            """);

        // Duplicate keys and a spacer, which is what makes this a credits file rather than a table.
        File.WriteAllText(CreditsPath,
            "key,ENGLISH\n"
            + "HEADER,Lead Designer\n"
            + "CENTER,Alice\n"
            + "CENTER,[TBL]\n"
            + "HEADER,Lead Artist\n"
            + "CENTER,Bob\n");

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

        // After the server is down, so nothing is still holding a handle on Windows.
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
