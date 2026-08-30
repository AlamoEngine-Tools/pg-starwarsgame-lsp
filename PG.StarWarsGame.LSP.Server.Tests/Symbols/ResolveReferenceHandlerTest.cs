// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Symbols;

namespace PG.StarWarsGame.LSP.Server.Tests.Symbols;

/// <summary>
///     <c>aet/resolveReference</c> - the wire shape over <see cref="IDefinitionLocator" />.
/// </summary>
/// <remarks>
///     The lookup has its own tests; what is worth pinning here is that BOTH outcomes survive the
///     trip. An error that arrived as a bare null uri would leave a panel showing "nothing here"
///     for a symbol that is merely in the base game, and the reader could not tell the two apart.
/// </remarks>
public sealed class ResolveReferenceHandlerTest
{
    private sealed class StubLocator(DefinitionLocation answer) : IDefinitionLocator
    {
        public string? SawValue { get; private set; }
        public string? SawReferenceType { get; private set; }

        public DefinitionLocation Locate(string? value, string? referenceType)
        {
            SawValue = value;
            SawReferenceType = referenceType;
            return answer;
        }
    }

    [Fact]
    public async Task Located_ReturnsTheFilePositionAndPassesTheTypeThrough()
    {
        var locator = new StubLocator(new DefinitionLocation("file:///ws/abilities.xml", 12, 4));

        var result = await new ResolveReferenceHandler(locator).Handle(
            new ResolveReferenceParams("Medic_Healing", "SpecialAbility"),
            TestContext.Current.CancellationToken);

        Assert.Equal("file:///ws/abilities.xml", result.Uri);
        Assert.Equal(12, result.Line);
        Assert.Equal(4, result.Column);
        Assert.Null(result.Error);
        Assert.Equal("Medic_Healing", locator.SawValue);
        Assert.Equal("SpecialAbility", locator.SawReferenceType);
    }

    [Fact]
    public async Task NotLocated_CarriesTheReasonRatherThanAnEmptyResult()
    {
        var locator = new StubLocator(new DefinitionLocation(Error: "defined in the base game"));

        var result = await new ResolveReferenceHandler(locator).Handle(
            new ResolveReferenceParams("Medic_Healing", "SpecialAbility"),
            TestContext.Current.CancellationToken);

        Assert.Null(result.Uri);
        Assert.Equal("defined in the base game", result.Error);
    }
}
