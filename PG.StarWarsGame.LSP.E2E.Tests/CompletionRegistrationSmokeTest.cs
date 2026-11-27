// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     A client registering statically gets one completion capability for XML and Lua together (#489).
///     It used to be one handler's options, picked per start: half the starts lost the Lua trigger
///     characters and the resolve, the other half the XML ones.
/// </summary>
[Trait("Category", "E2E")]
public sealed class CompletionRegistrationSmokeTest
{
    [Fact]
    public async Task Completion_ToAStaticClient_CarriesEveryLanguagesTriggers_AndResolve()
    {
        await using var session = await RawLspSession.StartAsync(
            new { textDocument = new { completion = new { dynamicRegistration = false } } },
            new { schemaLocalPath = LspTestEnvironment.SchemaLocalPath });

        var provider = session.Capabilities["completionProvider"];
        var triggers = provider?["triggerCharacters"]?.Values<string>().ToHashSet() ?? [];

        Assert.True(provider?["resolveProvider"]?.Value<bool>() ?? false, $"completionProvider: {provider}");
        Assert.Superset(new HashSet<string?> { "<", ">", ".", ":", "\"" }, triggers);
    }
}