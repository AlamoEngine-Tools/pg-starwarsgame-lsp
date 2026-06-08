// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Base for handlers that validate <see cref="ReferenceKind.LocalisationKey" /> tags.
///     Gates on both the <see cref="SingleValueTypeHandlerBase.TargetType" /> (via the parent) and
///     <see cref="ReferenceKind.LocalisationKey" />; <see cref="HandleLocalisationKey" /> is only
///     called when both conditions are satisfied.
/// </summary>
public abstract class LocalisationKeyHandlerBase : SingleValueTypeHandlerBase
{
    protected sealed override IEnumerable<XmlDiagnosticResult> HandleValue(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (fact.Tag.ReferenceKind != ReferenceKind.LocalisationKey)
            return [];

        return HandleLocalisationKey(fact, ctx);
    }

    protected abstract IEnumerable<XmlDiagnosticResult> HandleLocalisationKey(
        XmlTagValueFact fact, DiagnosticsContext ctx);

    protected static XmlDiagnosticResult? CheckKey(string key, DiagnosticsContext ctx)
    {
        if (ctx.Index.Localisation.ContainsKey(key))
            return null;

        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
            $"Localisation key '{key}' was not found in the loaded translation databases.");
    }
}