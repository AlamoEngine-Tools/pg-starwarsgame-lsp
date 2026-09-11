// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     A chain past the engine's ten sweeps is reported as a warning, and worded as MAY fail.
/// </summary>
/// <remarks>
///     It is not a defect that can be confirmed from one file. Declared base-first the whole chain
///     resolves in a single pass however deep it is; declared in reverse it needs one pass per
///     link. So the same eleven-link chain is fine or broken depending on the order the files
///     happen to load in, and inserting one file can flip it either way without a word from the
///     engine. Saying "this will fail" would be wrong as often as it was right.
/// </remarks>
public sealed class VariantChainTooDeepHandler : XmlDiagnosticsHandler<VariantChainTooDeepFact>
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.VariantChainTooDeep;

    protected override IEnumerable<XmlDiagnosticResult> Handle(
        VariantChainTooDeepFact fact, DiagnosticsContext ctx)
    {
        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"Variant chain is {fact.BaseCount} deep, above the engine's 10 resolution passes " +
                $"- '{fact.ObjectId}' may inherit nothing from '{fact.RootBaseId}' depending on the " +
                "order the files load in, and the failure is silent.")
        ];
    }
}
