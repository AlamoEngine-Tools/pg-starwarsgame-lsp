// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Formatting is offered exactly once, the way the client asked for it. Counted on the wire: a
///     registration sent twice under one id gave VS Code two formatters from this extension.
/// </summary>
[Trait("Category", "E2E")]
public sealed class FormattingRegistrationSmokeTest
{
    private static readonly string[] Methods = ["textDocument/formatting", "textDocument/rangeFormatting"];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Formatting_IsOfferedOnce_TheWayTheClientAsked(bool dynamicRegistration)
    {
        var capability = new { dynamicRegistration };
        await using var session = await RawLspSession.StartAsync(
            new { textDocument = new { formatting = capability, rangeFormatting = capability } },
            new { schemaLocalPath = LspTestEnvironment.SchemaLocalPath });

        var registered = session.RegisteredMethods;
        foreach (var method in Methods)
        {
            var expectedDynamic = dynamicRegistration ? 1 : 0;
            Assert.Equal(expectedDynamic, registered.Count(m => m == method));
        }

        var expectedStatic = !dynamicRegistration;
        Assert.Equal(expectedStatic, Offered(session.Capabilities["documentFormattingProvider"]));
        Assert.Equal(expectedStatic, Offered(session.Capabilities["documentRangeFormattingProvider"]));
    }

    private static bool Offered(JToken? provider)
    {
        return provider switch
        {
            null or { Type: JTokenType.Null } => false,
            { Type: JTokenType.Boolean } => provider.Value<bool>(),
            _ => true
        };
    }
}