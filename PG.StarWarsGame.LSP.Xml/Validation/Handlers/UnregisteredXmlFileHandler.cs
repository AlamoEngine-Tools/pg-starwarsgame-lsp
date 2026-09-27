// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Says that a file the author is looking at is not read by the game.
///     <para>
///         A warning rather than an error: the file is well-formed and everything in it may be
///         perfectly valid. What is wrong is that nothing reaches it, which is a fact about how the
///         project is wired rather than about the file - and the one thing the author cannot see by
///         reading the file.
///     </para>
/// </summary>
public sealed class UnregisteredXmlFileHandler : XmlDiagnosticsHandler<XmlUnregisteredFileFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.UnregisteredXmlFile;

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlUnregisteredFileFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"No registry names '{fact.FileName}', so the game never reads it.")
        ];
    }
}
