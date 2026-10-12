// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The Lua analyzer sends its lenses without a command and fills it on resolve (#155), so a
///     client registering statically must be told it can resolve - or it shows empty lenses.
/// </summary>
[Trait("Category", "E2E")]
public sealed class CodeLensRegistrationSmokeTest
{
    [Fact]
    public async Task CodeLensResolve_IsAdvertised_ToAStaticClient()
    {
        await using var session = await RawLspSession.StartAsync(
            new { textDocument = new { codeLens = new { dynamicRegistration = false } } },
            new { schemaLocalPath = LspTestEnvironment.SchemaLocalPath });

        Assert.True(session.Capabilities["codeLensProvider"]?["resolveProvider"]?.Value<bool>() ?? false,
            $"codeLensProvider: {session.Capabilities["codeLensProvider"]}");
    }
}
