// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Reports that an additive tag accumulates rather than replaces (#63). Deliberately
///     <see cref="XmlDiagnosticSeverity.Information" />: setting an additive tag on a variant is
///     legal and frequently intended, so this states the consequence instead of calling it a
///     mistake - the author still has to decide whether keeping the base's value is what they want.
/// </summary>
public sealed class VariantAdditiveMergeHandler : XmlDiagnosticsHandler<VariantAdditiveMergeFact>
{
    // Additive tags hold comma-separated lists that routinely run to hundreds of characters; the
    // full merged value is what the effective-object view is for, not a Problems-panel entry.
    private const int MaxInlineEntries = 3;

    private static readonly char[] Separators = [',', ' ', '\t', '\r', '\n'];

    /// <inheritdoc />
    public override DiagnosticId? DefaultId => DiagnosticIds.VariantAdditiveMerge;

    protected override IEnumerable<XmlDiagnosticResult> Handle(VariantAdditiveMergeFact fact, DiagnosticsContext ctx)
    {
        var inherited = Entries(fact.BaseValue);

        return
        [
            new XmlDiagnosticResult(XmlDiagnosticSeverity.Information,
                ctx.Messages.Format("VariantAdditiveMerge_IsAdditiveThisValueIs", fact.TagName,
                    Describe(inherited, ctx.Messages)))
        ];
    }

    private static List<string> Entries(string value)
    {
        return value.Split(Separators, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string Describe(List<string> entries, IMessageCatalog messages)
    {
        if (entries.Count == 0) return messages.Format("VariantAdditiveMerge_ItsValue");
        if (entries.Count <= MaxInlineEntries)
            return $"{string.Join(", ", entries.Select(e => $"'{e}'"))}";

        var shown = string.Join(", ", entries.Take(MaxInlineEntries).Select(e => $"'{e}'"));
        return messages.Format("VariantAdditiveMerge_AndMore", shown, entries.Count - MaxInlineEntries);
    }
}