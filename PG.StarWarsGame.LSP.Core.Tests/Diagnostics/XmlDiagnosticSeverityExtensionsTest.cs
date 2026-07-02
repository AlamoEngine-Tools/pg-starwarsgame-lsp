// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

public sealed class XmlDiagnosticSeverityExtensionsTest
{
    [Theory]
    [InlineData(XmlDiagnosticSeverity.Error, DiagnosticSeverity.Error)]
    [InlineData(XmlDiagnosticSeverity.Warning, DiagnosticSeverity.Warning)]
    [InlineData(XmlDiagnosticSeverity.Information, DiagnosticSeverity.Information)]
    [InlineData(XmlDiagnosticSeverity.Hint, DiagnosticSeverity.Hint)]
    public void ToLsp_KnownSeverity_ReturnsMappedValue(XmlDiagnosticSeverity input, DiagnosticSeverity expected)
    {
        Assert.Equal(expected, input.ToLsp());
    }

    [Fact]
    public void ToLsp_UnknownSeverity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((XmlDiagnosticSeverity)99).ToLsp());
    }
}
