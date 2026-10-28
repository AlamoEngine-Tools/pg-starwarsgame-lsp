// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A hardcoded-set token the game cannot resolve is an error; one it resolves but the schema
///     marks deprecated is a warning, under its own id so either can be suppressed alone.
/// </summary>
public sealed class HardcodedSetValueHandler : XmlDiagnosticsHandler<HardcodedSetValueFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.UnknownHardcodedSetValue;

    protected override IEnumerable<XmlDiagnosticResult> Handle(HardcodedSetValueFact fact, DiagnosticsContext ctx)
    {
        return fact.Deprecated
            ?
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                    ctx.Messages.Format("HardcodedSet_Deprecated", fact.Token),
                    Id: DiagnosticIds.DeprecatedHardcodedSetValue)
            ]
            :
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    ctx.Messages.Format("HardcodedSet_Unknown", fact.Token, fact.SetName),
                    Id: DiagnosticIds.UnknownHardcodedSetValue)
            ];
    }
}