// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Server fixture that sends a partial <c>features</c> node in <c>initializationOptions</c>,
///     turning off <c>storySimulator</c> while leaving <c>storyEditing</c> at its server-side
///     default of <c>true</c>. Uses the EaW workspace so go-to-definition has real cross-file data
///     to resolve.
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
            locale = LspTestEnvironment.Locale,
            features = new { storySimulator = false }
        };
    }
}
