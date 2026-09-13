// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Named handler (ID: <c>context-name-pair</c>) for tags that hold a single
///     (ContextName, ValueName) pair - e.g. <c>Music_Event_List_Ambient</c> and
///     <c>Music_Event_List_Battle</c>. Reached through <c>validationOverride</c> in YAML.
/// </summary>
/// <remarks>
///     Its <c>mode: replace</c> is here to supersede the TupleList shape check, which those tags'
///     declared type would otherwise apply to a value it does not describe. Be aware that the mode
///     is wider than that intent: <c>XmlDiagnosticsHandlerRegistry.Dispatch</c> discards EVERY
///     default handler for the fact type, so reference resolution and allowed values would stop
///     running too. Neither tag carries either, which is what makes the wider scope harmless -
///     and <c>EawSchemaReplaceOverrideScopeTest</c> fails if that stops being true.
/// </remarks>
public sealed class ContextNamePairHandler : XmlDiagnosticsHandler<XmlTagValueFact>, IXmlNamedDiagnosticsHandler
{
    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.ContextNamePair;

    public string ValidationId => "context-name-pair";

    protected override IEnumerable<XmlDiagnosticResult> Handle(XmlTagValueFact fact, DiagnosticsContext ctx)
    {
        var idx = fact.RawValue.IndexOf(',');
        if (idx < 0)
            return [Error(fact)];

        var context = fact.RawValue[..idx].Trim();
        var name = fact.RawValue[(idx + 1)..].Trim();

        if (context.Length == 0 || name.Length == 0)
            return [Error(fact)];

        var index = ctx.Index;
        if ((index.Baseline.Symbols.Count > 0 || index.WorkspaceDefinitions.Count > 0)
            && index.Resolve(name) is null)
            return
            [
                new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
                    $"'{name}' could not be resolved as a music event for <{fact.Tag.Tag}>.", Id: DiagnosticIds.ContextNamePairUnresolvedMusicEvent)
            ];

        return [];
    }

    private static XmlDiagnosticResult Error(XmlTagValueFact fact)
    {
        return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"'{fact.RawValue.Trim()}' is not a valid context-name pair for <{fact.Tag.Tag}>. Expected: ContextName, ValueName.");
    }
}