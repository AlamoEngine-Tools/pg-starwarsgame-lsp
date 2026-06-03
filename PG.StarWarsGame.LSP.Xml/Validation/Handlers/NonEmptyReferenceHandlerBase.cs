// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Base for reference value handlers whose only validation is "must not be empty".
///     Subclasses supply the <see cref="SingleValueTypeHandlerBase.TargetType" /> and a
///     <see cref="ReferenceNoun" /> for the diagnostic message.
/// </summary>
public abstract class NonEmptyReferenceHandlerBase : SingleValueTypeHandlerBase
{
    /// <summary>The noun used in the diagnostic, e.g. "faction reference".</summary>
    protected abstract string ReferenceNoun { get; }

    protected sealed override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.RawValue.Trim().Length != 0)
            return [];

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                $"'' is not a valid {ReferenceNoun} for <{fact.Tag.Tag}>.")
        ];
    }
}