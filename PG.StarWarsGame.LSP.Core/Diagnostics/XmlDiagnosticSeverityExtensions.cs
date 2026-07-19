// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

public static class XmlDiagnosticSeverityExtensions
{
    public static DiagnosticSeverity ToLsp(this XmlDiagnosticSeverity severity)
    {
        return severity switch
        {
            XmlDiagnosticSeverity.Error => DiagnosticSeverity.Error,
            XmlDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            XmlDiagnosticSeverity.Information => DiagnosticSeverity.Information,
            XmlDiagnosticSeverity.Hint => DiagnosticSeverity.Hint,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
        };
    }
}