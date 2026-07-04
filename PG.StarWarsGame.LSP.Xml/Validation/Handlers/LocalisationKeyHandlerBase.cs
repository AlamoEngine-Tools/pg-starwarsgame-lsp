// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Base for handlers that validate <see cref="ReferenceKind.LocalisationKey" /> tags. Gates on
///     both <see cref="TargetTypes" /> and <see cref="ReferenceKind.LocalisationKey" />;
///     <see cref="HandleLocalisationKey" /> is only called when both conditions are satisfied.
/// </summary>
public abstract class LocalisationKeyHandlerBase : XmlDiagnosticsHandler<XmlTagValueFact>
{
    /// <summary>
    ///     The <see cref="XmlValueType" />s this handler covers. Usually a single value, but
    ///     <see cref="LocalisationKeyListExistenceHandler" /> covers two: schema declares
    ///     Encyclopedia_Text/MP_Encyclopedia_Text as TypeReferenceList even though they're
    ///     semantically a localisation-key list like any NameReferenceList tag.
    /// </summary>
    protected abstract IReadOnlyList<XmlValueType> TargetTypes { get; }

    public override IEnumerable<XmlValueType> HandledValueTypes => TargetTypes;

    protected sealed override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        if (!TargetTypes.Contains(fact.Tag.ValueType))
            return [];

        if (fact.Tag.ReferenceKind != ReferenceKind.LocalisationKey)
            return [];

        return HandleLocalisationKey(fact, ctx);
    }

    protected abstract IEnumerable<XmlDiagnosticResult> HandleLocalisationKey(
        XmlTagValueFact fact, DiagnosticsContext ctx);

    /// <summary>
    ///     Checks whether <paramref name="key" /> exists in the loaded translation databases.
    ///     <paramref name="position" />, when given, points the diagnostic at that specific token
    ///     (e.g. one entry within a list value) instead of the default fact-wide range.
    /// </summary>
    protected static XmlDiagnosticResult? CheckKey(
        string key, DiagnosticsContext ctx, (int Line, int Col)? position = null)
    {
        if (ctx.Index.Localisation.ContainsKey(key))
            return null;

        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
            $"Localisation key '{key}' was not found in the loaded translation databases.",
            OverrideLine: position?.Line,
            OverrideColumn: position?.Col,
            OverrideLength: position is null ? null : key.Length,
            CreateLocalisationKey: key);
    }
}