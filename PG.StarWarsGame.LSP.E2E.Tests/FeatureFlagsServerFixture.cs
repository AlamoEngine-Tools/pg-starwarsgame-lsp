// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Server fixture that sends a partial <c>features</c> node in <c>initializationOptions</c>,
///     turning off <c>xml.completion</c>, <c>xml.diagnostics</c>, and <c>tools.localisation</c>
///     while leaving every other flag (e.g. <c>xml.goToDefinition</c>) at its server-side default of
///     <c>true</c> — this is what proves the flags are selective rather than an all-or-nothing kill
///     switch. Uses the EaW workspace so go-to-definition has real cross-file data to resolve.
/// </summary>
public sealed class FeatureFlagsServerFixture : LspServerFixture
{
    protected override string ResolveWorkspacePath()
    {
        return LspTestEnvironment.EawWorkspacePath ?? TestDataDirectory;
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
            features = new
            {
                xml = new { completion = false, diagnostics = false },
                tools = new { localisation = false }
            }
        };
    }
}
