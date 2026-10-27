// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Diagnostics;

/// <summary>
///     Suppression is applied once, on the way out of <see cref="XmlDiagnosticsPublisher.Collect" />,
///     over everything a run produced (#66, #67). The engine group's damage type rule stands in for
///     all of them: a file-scoped directive on its id or on the group's wildcard silences it, and a
///     directive naming another id leaves it alone.
/// </summary>
public sealed class XmlDiagnosticsPublisherSuppressionTest
{
    private const string Uri = "file:///gameconstants.xml";

    private static XmlDiagnosticsPublisher BuildPublisher()
    {
        var fileHelper = new FileHelper(new MockFileSystem());
        return XmlPublisherFixture.Create(
            new RecordingDiagnosticsSink(),
            new FakeGameIndexService(),
            new FakeGameWorkspaceHost(),
            new SchemaProviderBuilder().Build(),
            new XmlDiagnosticsHandlerRegistry([new DamageTypesIncompleteHandler(), new DamageTypeOutOfOrderHandler()]),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            new FakeFileTypeRegistry(),
            fileHelper,
            [new DamageTypeTailFactProducer()]);
    }

    // The required tail with its first two entries swapped: two out-of-order errors, aetswg-011-0002.
    private static string BadTailDocument(string comment = "")
    {
        var badTail = string.Join(" ", DamageTypeTailFactProducer.RequiredTail).Replace(
            "Damage_Normal Damage_Force_Whirlwind", "Damage_Force_Whirlwind Damage_Normal");
        return $"<GameConstants>\n{comment}\n<Damage_Types>{badTail}</Damage_Types>\n</GameConstants>";
    }

    [Fact]
    public void AFileScopedDirectiveOnTheId_SilencesIt()
    {
        var publisher = BuildPublisher();

        var unsuppressed = publisher.Collect(Uri, BadTailDocument(), GameIndex.Empty);
        Assert.NotEmpty(unsuppressed);

        var suppressed = publisher.Collect(Uri,
            BadTailDocument("<!-- aetswg:suppress-file aetswg-011-0002 intentional in this mod -->"), GameIndex.Empty);

        Assert.Empty(suppressed);
    }

    [Fact]
    public void TheGroupWildcard_SilencesTheWholeEngineGroup()
    {
        var suppressed = BuildPublisher().Collect(Uri,
            BadTailDocument("<!-- aetswg:suppress-file aetswg-011-* -->"), GameIndex.Empty);

        Assert.Empty(suppressed);
    }

    // A directive naming a different diagnostic must leave this one alone - the whole point of ids
    // is that suppression is targeted.
    [Fact]
    public void ADirectiveForAnotherId_LeavesItInPlace()
    {
        var kept = BuildPublisher().Collect(Uri,
            BadTailDocument("<!-- aetswg:suppress-file aetswg-011-0003 -->"), GameIndex.Empty);

        Assert.NotEmpty(kept);
    }
}